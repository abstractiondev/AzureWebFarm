using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using AzureWebFarm.Entities;
using AzureWebFarm.Extensions;
using AzureWebFarm.Helpers;
using AzureWebFarm.Storage;
using Microsoft.Web.Administration;
using Microsoft.Web.Deployment;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.StorageClient;

namespace AzureWebFarm.Services
{
    public class SyncService
    {
        private const string BlobStopName = "stop";

        private readonly WebSiteRepository _sitesRepository;
        private readonly CertificateRepository _certificateRepository;
        private readonly SyncStatusRepository _syncStatusRepository;

        private readonly string _localSitesPath;
        private readonly string _localTempPath;
        private readonly IEnumerable<string> _directoriesToExclude;

        private readonly CloudBlobContainer _container;

        private readonly IDictionary<string, FileEntry> _entries;
        private readonly Dictionary<string, DateTime> _siteDeployTimes;

        private static readonly string RoleWebSiteName = RoleEnvironment.CurrentRoleInstance.Id + "_" + "Web";

        public SyncService(string localSitesPath, string localTempPath, IEnumerable<string> directoriesToExclude, string storageSettingName)
            : this (
                new WebSiteRepository(storageSettingName),
                new CertificateRepository(),
                new SyncStatusRepository(storageSettingName),
                CloudStorageAccount.FromConfigurationSetting(storageSettingName),
                localSitesPath,
                localTempPath,
                directoriesToExclude
            )
        {}

        public SyncService(WebSiteRepository sitesRepository, CertificateRepository certificateRepository, SyncStatusRepository syncStatusRepository, CloudStorageAccount storageAccount, string localSitesPath, string localTempPath, IEnumerable<string> directoriesToExclude)
        {
            _sitesRepository = sitesRepository;
            _certificateRepository = certificateRepository;
            _syncStatusRepository = syncStatusRepository;

            _localSitesPath = localSitesPath;
            _localTempPath = localTempPath;
            _directoriesToExclude = directoriesToExclude;
            _entries = new Dictionary<string, FileEntry>();
            _siteDeployTimes = new Dictionary<string, DateTime>();

            var sitesContainerName = RoleEnvironment.GetConfigurationSettingValue("SitesContainerName").ToLowerInvariant();
            _container = storageAccount.CreateCloudBlobClient().GetContainerReference(sitesContainerName);
            _container.CreateIfNotExist();
        }

        public void UpdateAllSitesSyncStatus(string roleInstanceId, bool isOnline)
        {
            foreach (var syncStatus in _syncStatusRepository.RetrieveSyncStatusByInstanceId(roleInstanceId))
            {
                var newSyncStatus = new SyncStatus
                {
                    SiteName = syncStatus.SiteName,
                    RoleInstanceId = roleInstanceId,
                    Status = syncStatus.Status,
                    IsOnline = isOnline
                };

                _syncStatusRepository.UpdateStatus(newSyncStatus);
            }
        }

        public void Start()
        {
            // Always sync once in case the role has been reimaged.
            SyncOnce();
        }

        // ReSharper disable FunctionNeverReturns
        public void SyncForever(TimeSpan interval)
        {
            var blobStop = _container.GetBlobReference(BlobStopName);
            var lastHeartbeat = DateTime.MinValue;

            while (true)
            {
                var isPaused = blobStop.Exists();
 
                var currentTime = DateTime.Now;
                if ((currentTime - lastHeartbeat).Minutes > 15)
                {
                    Trace.TraceInformation("SyncService - Synchronization is {0}...", isPaused ? "paused" : "active");
                    lastHeartbeat = currentTime;
                }

                if (!isPaused)
                {
                    SyncOnce();
                }

                Thread.Sleep(interval);
            }
        }
        // ReSharper restore FunctionNeverReturns

        private void SyncOnce()
        {
            Trace.TraceInformation("SyncService - Synchronizing role instances...");

            try
            {
                UpdateIISSitesFromTableStorage();
            }
            catch (Exception e)
            {
                Trace.TraceError("SyncService [Table => IIS] - Failed to update IIS site information from table storage.{0}{1}", Environment.NewLine, e.TraceInformation());
            }

            try
            {
                SyncBlobToLocal();
            }
            catch (Exception e)
            {
                Trace.TraceError("SyncService [Blob => Local Storage] - Failed to synchronize local site folders and blob storage.{0}{1}", Environment.NewLine, e.TraceInformation());
            }

            try
            {
                DeploySitesFromLocal();
            }
            catch (Exception e)
            {
                Trace.TraceError("SyncService [Local Storage => IIS] - Failed to deploy MSDeploy package in local storage to IIS.{0}{1}", Environment.NewLine, e.TraceInformation());
            }

            try
            {
                PackageSitesToLocal();
            }
            catch (Exception e)
            {
                Trace.TraceError("SyncService [IIS => Local Storage] - Failed to create an MSDeploy package in local storage from updates in IIS.{0}{1}", Environment.NewLine, e.TraceInformation());
            }

            Trace.TraceInformation("SyncService - Synchronization completed.");
        }

        public static bool IsSyncEnabled()
        {
            var blobStop = GetCloudBlobStop();
            var enable = !blobStop.Exists();
            return enable;
        }

        public static void SyncEnable()
        {
            var blobStop = GetCloudBlobStop();
            blobStop.DeleteIfExists();

            Trace.TraceInformation("SyncService - Synchronization resumed.");
        }

        public static void SyncDisable()
        {
            var blobStop = GetCloudBlobStop();
            if (!blobStop.Exists())
            {
                blobStop.UploadText(string.Empty); 
            }

            Trace.TraceInformation("SyncService - Synchronization paused.");
        }

        private static CloudBlob GetCloudBlobStop()
        {
            var storageAccount = CloudStorageAccount.FromConfigurationSetting("DataConnectionstring");
            var sitesContainerName = RoleEnvironment.GetConfigurationSettingValue("SitesContainerName").ToLowerInvariant();
            var container = storageAccount.CreateCloudBlobClient().GetContainerReference(sitesContainerName);
            var blobStop = container.GetBlobReference(BlobStopName);
            return blobStop;
        }

        private static DateTime GetFolderLastModifiedTimeUtc(string sitePath)
        {
            try
            {
                var lastModifiedTime = File.GetLastWriteTimeUtc(sitePath);

                foreach (var filePath in Directory.EnumerateFileSystemEntries(sitePath, "*", SearchOption.AllDirectories))
                {
                    var fileLastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
                    if (fileLastWriteTimeUtc > lastModifiedTime)
                    {
                        lastModifiedTime = fileLastWriteTimeUtc;
                    }
                }

                return lastModifiedTime;
            }
            catch (PathTooLongException e)
            {
                Trace.TraceError("SyncService - Failed to retrieve last modified time.{0}{1}", Environment.NewLine, e.TraceInformation());

                return DateTime.MinValue;
            }
        }

        private void UpdateIISSitesFromTableStorage()
        {
            var allSites = _sitesRepository.RetrieveWebSitesWithBindings();

            if (!AzureRoleEnvironment.IsComputeEmulatorEnvironment)
            {
                var iisManager = new IISManager(_localSitesPath, _localTempPath, _syncStatusRepository);
                iisManager.UpdateSites(allSites);
            }

            // Cleanup
            for (int i = _siteDeployTimes.Count - 1; i >= 0; i--)
            {
                var siteName = _siteDeployTimes.ElementAt(i).Key;
                if (!allSites.Any(s => s.Name.Equals(siteName, StringComparison.OrdinalIgnoreCase)))
                {
                    _siteDeployTimes.Remove(siteName);
                    _syncStatusRepository.RemoveWebSiteStatus(siteName);

                    var sitePath = Path.Combine(_localSitesPath, siteName);
                    var tempSitePath = Path.Combine(_localTempPath, siteName);

                    FilesHelper.RemoveFolder(sitePath);
                    FilesHelper.RemoveFolder(tempSitePath);

                    if (_entries.ContainsKey(siteName))
                    {
                        // Remove blob
                        _container.GetBlobReference(siteName).DeleteIfExists();
                        _container.GetBlobReference(siteName + "/" + siteName + ".zip").DeleteIfExists();
                        
                        _entries.Remove(siteName);
                    }
                }
            }
        }

        private void SyncBlobToLocal()
        {
            var seen = new HashSet<string>();

            foreach (var thing in EnumerateLocalEntries())
            {
                var path = thing.Item1;
                var entry = thing.Item2;

                seen.Add(path);

                if (!_entries.ContainsKey(path) || _entries[path].LocalLastModified < entry.LocalLastModified)
                {
                    var newBlob = _container.GetBlobReference(path);
                    if (entry.IsDirectory)
                    {
                        newBlob.Metadata["IsDirectory"] = bool.TrueString;
                        newBlob.UploadByteArray(new byte[0]);
                    }
                    else
                    {
                        using (var stream = File.Open(Path.Combine(_localTempPath, path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            newBlob.Metadata["IsDirectory"] = bool.FalseString;
                            newBlob.UploadFromStream(stream);
                        }
                    }

                    entry.CloudLastModified = newBlob.Properties.LastModifiedUtc;
                    _entries[path] = entry;
                }
            }

            foreach (var path in _entries.Keys.Where(k => !seen.Contains(k)).ToArray())
            {
                // Try deleting all the unused files and directories
                try
                {
                    if (_entries[path].IsDirectory)
                    {
                        Directory.Delete(path);
                    }
                    else
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception) {}

                _entries.Remove(path);
            }

            seen = new HashSet<string>();

            var blobs = _container.ListBlobs(
                new BlobRequestOptions 
                { 
                    UseFlatBlobListing = true, 
                    BlobListingDetails = BlobListingDetails.Metadata
                }
            ).OfType<CloudBlob>();

            foreach (var blob in blobs)
            {
                var path = blob.Uri.ToString().Substring(_container.Uri.ToString().Length + 1);
                var entry = new FileEntry
                {
                    CloudLastModified = blob.Properties.LastModifiedUtc,
                    IsDirectory = blob.Metadata.AllKeys.Any(k => k.Equals("IsDirectory")) && 
                                  blob.Metadata["IsDirectory"].Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase)
                };

                seen.Add(path);

                if (!_entries.ContainsKey(path) || _entries[path].CloudLastModified < entry.CloudLastModified)
                {
                    var tempPath = Path.Combine(_localTempPath, path);
                    
                    if (entry.IsDirectory)
                    {
                        Directory.CreateDirectory(tempPath);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.Combine(_localTempPath, Path.GetDirectoryName(path)));
                        Trace.TraceInformation("SyncService [Blob => Local Storage] - Downloading file: '{0}'", path);

                        using (var stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                        {
                            blob.DownloadToStream(stream);
                        }
                    }

                    entry.LocalLastModified = new FileInfo(tempPath).LastWriteTimeUtc;
                    _entries[path] = entry;
                }
            }

            foreach (var path in _entries.Keys.Where(k => !seen.Contains(k)).ToArray())
            {
                if (_entries[path].IsDirectory)
                {
                    Directory.Delete(Path.Combine(_localTempPath, path), true);
                }
                else
                {
                    try
                    {
                        File.Delete(Path.Combine(_localTempPath, path));
                    }
                    catch (Exception) {}
                }

                _entries.Remove(path);
            }
        }

        private void DeploySitesFromLocal()
        {
            Trace.TraceInformation("SyncService [Local Storage => IIS] - Site deploy times: {0}", string.Join(",", _siteDeployTimes.Select(t => t.Key + " - " + t.Value).ToArray()));

            foreach (var site in Directory.EnumerateDirectories(_localTempPath).Select(d => Path.GetFileName(d).ToLowerInvariant()))
            {
                var sitePath = Path.Combine(_localSitesPath, site);
                var tempSitePath = Path.Combine(_localTempPath, site);

                if (Directory.Exists(tempSitePath))
                {
                    // Sync from package to IIS App using MSDeploy
                    string packageFile = null;
                    try
                    {
                        packageFile = Directory.EnumerateFiles(tempSitePath).SingleOrDefault(f => f.ToLowerInvariant().EndsWith(".zip"));
                    }
                    catch (InvalidOperationException e)
                    {
                        if (string.IsNullOrEmpty(e.Message))
                        {
                            throw new InvalidOperationException("Multiple packages exist for the site '" + site + "'.");
                        }

                        throw;
                    }

                    if (packageFile != null)
                    {
                        if (!_siteDeployTimes.ContainsKey(site))
                        {
                            _siteDeployTimes.Add(site, DateTime.MinValue);
                        }

                        var packageLastModifiedTime = Directory.GetLastWriteTimeUtc(packageFile);
                        Trace.TraceInformation("SyncService [Local Storage => IIS] - Package last modified time: '{0}'", packageLastModifiedTime);

                        if (_siteDeployTimes[site] < packageLastModifiedTime)
                        {
                            Trace.TraceInformation("SyncService [Local Storage => IIS] - Deploying the package '{0}' to '{1}' with MSDeploy", packageFile, sitePath);

                            try
                            {
                                using (DeploymentObject deploymentObject = DeploymentManager.CreateObject(DeploymentWellKnownProvider.Package, packageFile))
                                {
                                    deploymentObject.SyncTo(DeploymentWellKnownProvider.DirPath, sitePath, new DeploymentBaseOptions(), new DeploymentSyncOptions());
                                }

                                UpdateSyncStatus(site, SyncInstanceStatus.Deployed);
                                _siteDeployTimes[site] = DateTime.UtcNow;
                            }
                            catch (Exception)
                            {
                                UpdateSyncStatus(site, SyncInstanceStatus.Error);
                                throw;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Packages sites that are in IIS but not in local temp storage.
        /// There are new sites that have been deployed to this instance using Web Deploy.
        /// </summary>
        private void PackageSitesToLocal()
        {
            Trace.TraceInformation("SyncService [IIS => Local Storage] - Site deploy times: {0}", string.Join(",", _siteDeployTimes.Select(t => t.Key + " - " + t.Value).ToArray()));

            using (var serverManager = new ServerManager())
            {
                foreach (var site in serverManager.Sites.ToArray())
                {
                    var siteName = site.Name.Replace("-", ".").ToLowerInvariant();
                    
                    if (!site.Name.Equals(RoleWebSiteName, StringComparison.OrdinalIgnoreCase))
                    {                        
                        var sitePath = Path.Combine(_localSitesPath, siteName);
                        var siteLastModifiedTime = GetFolderLastModifiedTimeUtc(sitePath);

                        if (!_siteDeployTimes.ContainsKey(siteName))
                        {
                            _siteDeployTimes.Add(siteName, siteLastModifiedTime);
                        }

                        Trace.TraceInformation("SyncService [IIS => Local Storage] - Site last modified time: '{0}'", siteLastModifiedTime);

                        if (_siteDeployTimes[siteName] < siteLastModifiedTime && siteLastModifiedTime.AddSeconds(30) < DateTime.UtcNow)
                        {
                            UpdateSyncStatus(siteName, SyncInstanceStatus.Deployed);

                            var tempSitePath = Path.Combine(_localTempPath, siteName);
                            if (!Directory.Exists(tempSitePath))
                            {
                                Directory.CreateDirectory(tempSitePath);
                            }
                            
                            var packageFile = Path.Combine(tempSitePath, siteName + ".zip");

                            // Create a package of the site and move it to local temp sites
                            Trace.TraceInformation("SyncService [IIS => Local Storage] - Creating a package of the site '{0}' and moving it to local temp sites '{1}'", siteName, packageFile);

                            try
                            {
                                using (DeploymentObject deploymentObject = DeploymentManager.CreateObject(DeploymentWellKnownProvider.DirPath, sitePath))
                                {
                                    deploymentObject.SyncTo(DeploymentWellKnownProvider.Package, packageFile, new DeploymentBaseOptions(), new DeploymentSyncOptions());
                                }

                                _siteDeployTimes[siteName] = DateTime.UtcNow;
                            }
                            catch (Exception)
                            {
                                UpdateSyncStatus(siteName, SyncInstanceStatus.Error);
                                throw;
                            }
                        }
                    }
                }
            }
        }

        private IEnumerable<Tuple<string, FileEntry>> EnumerateLocalEntries()
        {
            foreach (var filePath in Directory.EnumerateFileSystemEntries(_localTempPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = filePath.Substring(_localTempPath.Length + 1).Replace('\\', '/');
                var info = new FileInfo(filePath);
                var entry = new FileEntry
                {
                    LocalLastModified = info.LastWriteTimeUtc,
                    IsDirectory = info.Attributes.HasFlag(FileAttributes.Directory)
                };

                if (IsExcluded(relativePath))
                {
                    continue;
                }

                yield return new Tuple<string, FileEntry>(relativePath, entry);
            }
        }

        private bool IsExcluded(string topPath)
        {
            var position = topPath.IndexOf('/');

            if (position <= 0)
            {
                return false;
            }

            // Remove Site name
            string path = topPath.Substring(position + 1);

            if (_directoriesToExclude.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var toExclude in _directoriesToExclude)
            {
                if (path.StartsWith(toExclude + "/"))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateSyncStatus(string webSiteName, SyncInstanceStatus status)
        {
            var syncStatus = new SyncStatus
            {
                SiteName = webSiteName,
                RoleInstanceId = RoleEnvironment.IsAvailable ? RoleEnvironment.CurrentRoleInstance.Id : Environment.MachineName,
                Status = status,
                IsOnline = true
            };

            _syncStatusRepository.UpdateStatus(syncStatus);
        }
    }
}