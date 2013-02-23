using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using AzureToolkit;
using AzureWebFarm.Entities;
using AzureWebFarm.Helpers;
using AzureWebFarm.Storage;
using Microsoft.Web.Administration;
using Microsoft.Web.Deployment;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace AzureWebFarm.Services
{
    public class SyncService
    {
        #region Setup / Constructor

        private readonly IWebSiteRepository _sitesRepository;
        private readonly ISyncStatusRepository _syncStatusRepository;
        private readonly CloudBlobContainer _container;

        private readonly string _localSitesPath;
        private readonly string _localTempPath;
        private readonly IEnumerable<string> _directoriesToExclude;
        private readonly IEnumerable<string> _sitesToExclude;

        private readonly IDictionary<string, FileEntry> _entries;
        private readonly Dictionary<string, DateTime> _siteDeployTimes;

        private readonly Func<bool> _syncEnabled;

        public static int SyncWait = 30;

        public SyncService(IWebSiteRepository sitesRepository, ISyncStatusRepository syncStatusRepository, CloudStorageAccount storageAccount, string localSitesPath, string localTempPath, IEnumerable<string> directoriesToExclude, IEnumerable<string> sitesToExclude, Func<bool> syncEnabled)
        {
            _sitesRepository = sitesRepository;
            _syncStatusRepository = syncStatusRepository;

            _localSitesPath = localSitesPath;
            _localTempPath = localTempPath;
            _directoriesToExclude = directoriesToExclude;
            _sitesToExclude = sitesToExclude;
            _syncEnabled = syncEnabled;
            _entries = new Dictionary<string, FileEntry>();
            _siteDeployTimes = new Dictionary<string, DateTime>();

            var sitesContainerName = AzureRoleEnvironment.GetConfigurationSettingValue(Constants.WebDeployPackagesBlobContainerName).ToLowerInvariant();
            _container = storageAccount.CreateCloudBlobClient().GetContainerReference(sitesContainerName);
            _container.CreateIfNotExist();
        }
        #endregion

        #region Public methods
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
        public void SyncForever(Func<TimeSpan> interval)
        {
            var lastHeartbeat = DateTime.MinValue;

            while (true)
            {
                var currentTime = DateTime.Now;
                if ((currentTime - lastHeartbeat).Minutes > 15)
                {
                    Trace.TraceInformation("SyncService - Synchronization is {0}...", _syncEnabled() ? "paused" : "active");
                    lastHeartbeat = currentTime;
                }

                if (!_syncEnabled())
                {
                    SyncOnce();
                }

                OnPing();

                Thread.Sleep(interval());
            }
        }
        // ReSharper restore FunctionNeverReturns
        #endregion

        #region Sync Once

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
                SyncLocalToBlob();
            }
            catch (Exception e)
            {
                Trace.TraceError("SyncService [Local Storage => Blob] - Failed to synchronize blob storage and local site folders.{0}{1}", Environment.NewLine, e.TraceInformation());
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

        #endregion

        #region Update IIS from table storage

        public void UpdateIISSitesFromTableStorage()
        {
            var allSites = _sitesRepository.RetrieveWebSitesWithBindings();

            if (!AzureRoleEnvironment.IsComputeEmulatorEnvironment())
            {
                var iisManager = new IISManager(_localSitesPath, _localTempPath, _syncStatusRepository);
                iisManager.UpdateSites(allSites, _sitesToExclude.ToList());
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

                    OnSiteDeleted(siteName);
                }
            }
        }
        #endregion

        #region Update blob storage from temp dir

        public void SyncLocalToBlob()
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
                        Trace.TraceInformation("SyncService [Local Storage => Blob] - Uploading file: '{0}'", path);
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
                catch (Exception e)
                {
                    Trace.TraceWarning("Error deleting unused file: {0}", e);
                }

                _entries.Remove(path);
            }
        }

        #endregion

        #region Update temp dir from blob storage

        public void SyncBlobToLocal()
        {
            var seen = new HashSet<string>();

            var blobs = _container.ListBlobs(
                new BlobRequestOptions
                {
                    UseFlatBlobListing = true,
                    BlobListingDetails = BlobListingDetails.Metadata
                }).OfType<CloudBlob>();

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
                    catch (Exception e)
                    {
                        Trace.TraceWarning("Error cleaning up unused directory: {0}", e);
                    }
                }

                _entries.Remove(path);
            }
        }
        #endregion

        #region Update IIS from temp dir

        public void DeploySitesFromLocal()
        {
            Trace.TraceInformation("SyncService [Local Storage => IIS] - Site deploy times: {0}", string.Join(",", _siteDeployTimes.Select(t => t.Key + " - " + t.Value).ToArray()));

            foreach (var site in Directory.EnumerateDirectories(_localTempPath).Select(d => Path.GetFileName(d).ToLowerInvariant()))
            {
                var sitePath = Path.Combine(_localSitesPath, site);
                var tempSitePath = Path.Combine(_localTempPath, site);

                if (Directory.Exists(tempSitePath))
                {
                    // Sync from package to IIS App using MSDeploy
                    string packageFile;
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
                                OnSiteUpdated(site);
                                _siteDeployTimes[site] = DateTime.UtcNow;
                            }
                            catch (Exception ex)
                            {
                                UpdateSyncStatus(site, SyncInstanceStatus.Error, ex);
                                throw;
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region Package new/updated sites to temp dir

        /// <summary>
        /// Packages sites that are in IIS but not in local temp storage.
        /// There are new sites that have been deployed to this instance using Web Deploy.
        /// </summary>
        public void PackageSitesToLocal()
        {
            Trace.TraceInformation("SyncService [IIS => Local Storage] - Site deploy times: {0}", string.Join(",", _siteDeployTimes.Select(t => t.Key + " - " + t.Value).ToArray()));

            using (var serverManager = new ServerManager())
            {
                foreach (var site in serverManager.Sites.Where(s => !_sitesToExclude.Contains(s.Name)))
                {
                    var siteName = site.Name.Replace("-", ".").ToLowerInvariant();

                    if (!site.Name.Equals(AzureRoleEnvironment.RoleWebsiteName(), StringComparison.OrdinalIgnoreCase))
                    {
                        var sitePath = Path.Combine(_localSitesPath, siteName);
                        var siteLastModifiedTime = GetFolderLastModifiedTimeUtc(sitePath);

                        if (!_siteDeployTimes.ContainsKey(siteName))
                        {
                            _siteDeployTimes.Add(siteName, DateTime.MinValue);
                        }

                        Trace.TraceInformation("SyncService [IIS => Local Storage] - Site last modified time: '{0}'", siteLastModifiedTime);

                        // If the site has been modified since the last deploy, but not within the last {SyncWait}s (otherwise it might be mid-sync)
                        if (_siteDeployTimes[siteName] < siteLastModifiedTime && siteLastModifiedTime < DateTime.UtcNow.AddSeconds(-SyncWait))
                        {
                            // Update status to deployed
                            UpdateSyncStatus(siteName, SyncInstanceStatus.Deployed);

                            // Ensure the temp path exists
                            var tempSitePath = Path.Combine(_localTempPath, siteName);
                            if (!Directory.Exists(tempSitePath))
                            {
                                Directory.CreateDirectory(tempSitePath);
                            }

                            // Create a package of the site and move it to local temp sites
                            var packageFile = Path.Combine(tempSitePath, siteName + ".zip");
                            Trace.TraceInformation("SyncService [IIS => Local Storage] - Creating a package of the site '{0}' and moving it to local temp sites '{1}'", siteName, packageFile);
                            try
                            {
                                using (var deploymentObject = DeploymentManager.CreateObject(DeploymentWellKnownProvider.DirPath, sitePath))
                                {
                                    deploymentObject.SyncTo(DeploymentWellKnownProvider.Package, packageFile, new DeploymentBaseOptions(), new DeploymentSyncOptions());
                                }

                                _siteDeployTimes[siteName] = DateTime.UtcNow;
                            }
                            catch (Exception ex)
                            {
                                UpdateSyncStatus(siteName, SyncInstanceStatus.Error, ex);
                                throw;
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region Helpers

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
            var path = topPath.Substring(position + 1);
            if (_directoriesToExclude.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            return _directoriesToExclude.Any(toExclude => path.StartsWith(toExclude + "/"));
        }

        private void UpdateSyncStatus(string webSiteName, SyncInstanceStatus status, Exception lastError = null)
        {
            var syncStatus = new SyncStatus
            {
                SiteName = webSiteName,
                RoleInstanceId = AzureRoleEnvironment.CurrentRoleInstanceId(),
                Status = status,
                IsOnline = true,
                LastError = lastError == null ? null : lastError.TraceInformation()
            };

            try
            {
                _syncStatusRepository.UpdateStatus(syncStatus);
            }
            catch (Exception ex)
            {
                Trace.TraceError("An error occured updating site sync status: {0}", ex.TraceInformation());
            }
        }
        #endregion

        #region Events

        public delegate void PingEventHandler(object sender, EventArgs e);
        public event PingEventHandler Ping;

        protected virtual void OnPing()
        {
            var handler = Ping;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        public delegate void SiteUpdatedEventHandler(object sender, EventArgs e, string siteName);
        public event SiteUpdatedEventHandler SiteUpdated;

        protected virtual void OnSiteUpdated(string siteName)
        {
            var handler = SiteUpdated;
            if (handler != null)
                handler(this, EventArgs.Empty, siteName);
        }

        public event SiteUpdatedEventHandler SiteDeleted;

        protected virtual void OnSiteDeleted(string siteName)
        {
            var handler = SiteDeleted;
            if (handler != null)
                handler(this, EventArgs.Empty, siteName);
        }
        #endregion
    }
}