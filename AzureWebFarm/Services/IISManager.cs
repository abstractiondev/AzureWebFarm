using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using AzureWebFarm.Entities;
using AzureWebFarm.Extensions;
using AzureWebFarm.Helpers;
using AzureWebFarm.Storage;
using Microsoft.Web.Administration;
using Microsoft.WindowsAzure.ServiceRuntime;
using Binding = AzureWebFarm.Entities.Binding;

namespace AzureWebFarm.Services
{
    public class IISManager
    {
        private readonly SyncStatusRepository _syncStatusRepository;
        private readonly string _localSitesPath;
        private readonly string _tempSitesPath;

        public static string RoleWebSiteName = RoleEnvironment.IsAvailable ? RoleEnvironment.CurrentRoleInstance.Id + "_" + "Web" : "Default Web Site";

        public IISManager(string localSitesPath, string tempSitesPath, SyncStatusRepository syncStatusRepository)
        {
            _syncStatusRepository = syncStatusRepository;
            _localSitesPath = localSitesPath;
            _tempSitesPath = tempSitesPath;
        }

        public void UpdateSites(IEnumerable<WebSite> sites, bool removeOtherSites = true)
        {
            Trace.TraceInformation("IISManager.Sites list from table: {0}", string.Join(",", sites.Select(s => s.Name)));

            if (removeOtherSites)
            {
                using (var serverManager = new ServerManager())
                {
                    var iisSites = serverManager.Sites;

                    Trace.TraceInformation("IISManager.Sites list from IIS: {0}", string.Join(",", iisSites.Select(s => s.Name)));

                    // Find sites that need to be removed
                    foreach (var iisSite in iisSites.ToArray())
                    {
                        var name = iisSite.Name.ToLowerInvariant();

                        // Never delete "webRoleSiteName", which is the website for this web role
                        if (!name.Equals(RoleWebSiteName, StringComparison.OrdinalIgnoreCase) &&
                            !sites.Select(s => s.Name.ToLowerInvariant()).Contains(name))
                        {
                            // Remove site
                            Trace.TraceInformation("IISManager.Removing site '{0}'", iisSite.Name);

                            serverManager.Sites.Remove(iisSite);

                            // Remove TEST and CDN applications
                            RemoveApplications(iisSites, name);

                            // Remove site path
                            try
                            {
                                var sitePath = Path.Combine(_localSitesPath, iisSite.Name);
                                var tempSitePath = Path.Combine(_tempSitesPath, iisSite.Name);

                                FilesHelper.RemoveFolder(sitePath);
                                FilesHelper.RemoveFolder(tempSitePath);
                            }
                            catch (Exception e)
                            {
                                Trace.TraceWarning("IISManager.Remove Site Path Error{0}{1}", Environment.NewLine, e.TraceInformation());
                            }

                            // Remove appPool
                            var appPool = serverManager.ApplicationPools.SingleOrDefault(ap => ap.Name.Equals(iisSite.Name, StringComparison.OrdinalIgnoreCase));
                            if (appPool != null)
                            {
                                Trace.TraceInformation("IISManager.Removing appPool '{0}'", appPool.Name);

                                serverManager.ApplicationPools.Remove(appPool);
                            }
                        }
                    }

                    try
                    {
                        serverManager.CommitChanges();
                    }
                    catch (Exception e)
                    {
                        Trace.TraceError("IISManager.CommitChanges (Cleanup IIS){0}{1}", Environment.NewLine, e.TraceInformation());
                    }
                }
            }

            foreach (var site in sites)
            {
                using (var serverManager = new ServerManager())
                {
                    var siteName = site.Name.ToLowerInvariant().Replace(" ", string.Empty);
                    var iisSite = serverManager.Sites.SingleOrDefault(ap => ap.Name.Equals(siteName, StringComparison.OrdinalIgnoreCase));
                    var sitePath = Path.Combine(_localSitesPath, siteName);

                    // Add new sites
                    if (iisSite == null)
                    {
                        // Update Status
                        UpdateSyncStatus(siteName, SyncInstanceStatus.NotCreated);

                        // Create physical path
                        if (!Directory.Exists(sitePath))
                        {
                            Directory.CreateDirectory(sitePath);
                        }

                        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AzureWebFarm.Resources.LandingPage.html"))
                        {
                            var fileContent = new StreamReader(stream).ReadToEnd().Replace("{WebSiteName}", siteName);
                            File.WriteAllText(Path.Combine(sitePath, "index.html"), fileContent);
                        }

                        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AzureWebFarm.Resources.LandingStyle.css"))
                        {
                            var fileContent = new StreamReader(stream).ReadToEnd();
                            File.WriteAllText(Path.Combine(sitePath, "Site.css"), fileContent);
                        }

                        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AzureWebFarm.Resources.PublishImage.png"))
                        {
                            var bitmap = new Bitmap(stream);
                            bitmap.Save(Path.Combine(sitePath, "publish.png"));
                        }

                        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AzureWebFarm.Resources.SolutionImage.png"))
                        {
                            var bitmap = new Bitmap(stream);
                            bitmap.Save(Path.Combine(sitePath, "solution.png"));
                        }

                        // Add web site
                        Trace.TraceInformation("IISManager.Adding site '{0}'", siteName);

                        var defaultBinding = site.Bindings.First();

                        X509Certificate2 cert = null;

                        if (!String.IsNullOrEmpty(defaultBinding.CertificateThumbprint))
                        {
                            cert = GetCertificate(defaultBinding.CertificateThumbprint);
                        }

                        if (cert != null)
                        {
                            Trace.TraceInformation("IISManager.Adding WebSite '{0}' with Binding Information '{1}' and Certificate '{2}'", site.Name, defaultBinding.BindingInformation, cert.Thumbprint);

                            iisSite = serverManager.Sites.Add(
                                siteName,
                                defaultBinding.BindingInformation,
                                sitePath,
                                cert.GetCertHash());
                        }
                        else
                        {
                            Trace.TraceInformation("IISManager.Adding WebSite '{0}' with Binding Information '{1}'", site.Name, defaultBinding.BindingInformation);

                            iisSite = serverManager.Sites.Add(
                                siteName,
                                defaultBinding.Protocol,
                                defaultBinding.BindingInformation,
                                sitePath);
                        }

                        // Create a new AppPool
                        var appPool = serverManager.ApplicationPools.SingleOrDefault(ap => ap.Name.Equals(siteName, StringComparison.OrdinalIgnoreCase));
                        if (appPool == null)
                        {
                            Trace.TraceInformation("IISManager.Adding AppPool '{0}' for site '{0}'", siteName);

                            appPool = serverManager.ApplicationPools.Add(siteName);
                            appPool.ManagedRuntimeVersion = "v4.0";
                            appPool.ProcessModel.IdentityType = ProcessModelIdentityType.NetworkService;
                        }

                        iisSite.ApplicationDefaults.ApplicationPoolName = appPool.Name;

                        // Update TEST and CDN applications
                        UpdateApplications(site, serverManager, siteName, sitePath, appPool);

                        // Update Sync Status
                        UpdateSyncStatus(siteName, SyncInstanceStatus.Created);
                    }
                    else
                    {
                        // Update TEST and CDN applications
                        var appPool = serverManager.ApplicationPools.SingleOrDefault(ap => ap.Name.Equals(siteName, StringComparison.OrdinalIgnoreCase));
                        UpdateApplications(site, serverManager, siteName, sitePath, appPool);
                    }

                    // Find bindings that need to be removed
                    foreach (var binding in iisSite.Bindings.ToArray())
                    {
                        if (!site.Bindings.Any(b => AreEqualsBindings(binding, b)))
                        {
                            Trace.TraceInformation("IISManager.Removing binding with protocol: '{0}'", binding.Protocol);
                            iisSite.Bindings.Remove(binding);
                        }
                    }

                    // Add new bindings
                    foreach (var binding in site.Bindings)
                    {
                        var iisBinding = iisSite.Bindings.SingleOrDefault(b => AreEqualsBindings(b, binding));
                        if (iisBinding == null)
                        {
                            X509Certificate2 cert = null;

                            if (!String.IsNullOrEmpty(binding.CertificateThumbprint))
                            {
                                cert = GetCertificate(binding.CertificateThumbprint);
                            }

                            if (cert != null)
                            {
                                Trace.TraceInformation("IISManager.Adding Binding '{0}' for WebSite '{1}' with Binding Information '{2}' and Certificate '{3}'", binding.Id, site.Name, binding.BindingInformation, cert.Thumbprint);
                                iisSite.Bindings.Add(binding.BindingInformation, cert.GetCertHash(), StoreName.My.ToString());
                            }
                            else
                            {
                                Trace.TraceInformation("IISManager.Adding Binding '{0}' for WebSite '{1}' with Binding Information '{2}'", binding.Id, site.Name, binding.BindingInformation);
                                iisSite.Bindings.Add(binding.BindingInformation, binding.Protocol);
                            }
                        }
                    }

                    try
                    {
                        Trace.TraceInformation("IISManager.Committing Changes for site '{0}'", site.Name);
                        serverManager.CommitChanges();
                    }
                    catch (Exception e)
                    {
                        UpdateSyncStatus(siteName, SyncInstanceStatus.Error);
                        Trace.TraceError("IISManager.CommitChanges for site '{0}'{1}{2}", site.Name, Environment.NewLine, e.TraceInformation());
                    }
                }
            }
        }

        private X509Certificate2 GetCertificate(string certificateHash)
        {
            var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            var certs = store.Certificates.Find(X509FindType.FindByThumbprint, certificateHash, true);
            store.Close();

            X509Certificate2 cert = null;
            if (certs.Count == 1)
            {
                cert = certs[0];
            }

            return cert;
        }

        private static void UpdateApplications(WebSite site, ServerManager serverManager, string siteName, string sitePath, ApplicationPool appPool)
        {
            var iisSites = serverManager.Sites;
            var adminSite = iisSites[RoleWebSiteName];

            var testApplication = adminSite.Applications.FirstOrDefault(
                app => app.Path.EndsWith("/test/" + siteName, StringComparison.OrdinalIgnoreCase));
            var cdnApplication = adminSite.Applications.FirstOrDefault(
                app => app.Path.EndsWith("/cdn/" + siteName, StringComparison.OrdinalIgnoreCase));

            if (site.EnableTestChildApplication)
            {
                if (testApplication == null)
                {
                    Trace.TraceInformation("IISManager.Adding Test application for site '{0}'", siteName);
                    testApplication = adminSite.Applications.Add("/test/" + siteName, sitePath);
                    testApplication.ApplicationPoolName = appPool.Name;
                }
            }
            else
            {
                if (testApplication != null)
                {
                    Trace.TraceInformation("IISManager.Removing Test application for site '{0}'", siteName);
                    adminSite.Applications.Remove(testApplication);
                }
            }

            if (site.EnableCDNChildApplication)
            {
                if (cdnApplication == null)
                {
                    Trace.TraceInformation("IISManager.Adding CDN application for site '{0}'", siteName);
                    cdnApplication = adminSite.Applications.Add("/cdn/" + siteName, Path.Combine(sitePath, "cdn"));
                    cdnApplication.ApplicationPoolName = appPool.Name;
                }
            }
            else
            {
                if (cdnApplication != null)
                {
                    Trace.TraceInformation("IISManager.Removing CDN application for site '{0}'", siteName);
                    adminSite.Applications.Remove(cdnApplication);
                }
            }
        }

        private static bool AreEqualsBindings(Microsoft.Web.Administration.Binding iisBinding, Binding binding)
        {
            var bindingAdress = binding.IpAddress == "*" ? "0.0.0.0" : binding.IpAddress;

            return
                iisBinding.Protocol.Equals(binding.Protocol, StringComparison.OrdinalIgnoreCase) &&
                iisBinding.EndPoint.Address.ToString().Equals(bindingAdress, StringComparison.OrdinalIgnoreCase) &&
                iisBinding.EndPoint.Port == binding.Port &&
                iisBinding.Host.Equals(binding.HostName, StringComparison.OrdinalIgnoreCase);
        }

        private static void RemoveApplications(SiteCollection iisSites, string siteName)
        {
            var adminSite = iisSites[RoleWebSiteName];

            var applicationsToRemove = from app in adminSite.Applications
                                       where app.Path.EndsWith("/test/" + siteName, StringComparison.OrdinalIgnoreCase) ||
                                       app.Path.EndsWith("/cdn/" + siteName, StringComparison.OrdinalIgnoreCase)
                                       select app;

            Trace.TraceInformation("IISManager.Removing Test and CDN applications for site '{0}'", siteName);

            foreach (var app in applicationsToRemove.ToArray())
            {
                adminSite.Applications.Remove(app);
            }
        }

        private void UpdateSyncStatus(string webSiteName, SyncInstanceStatus status)
        {
            if (_syncStatusRepository != null)
            {
                var syncStatus = new SyncStatus
                {
                    SiteName = webSiteName,
                    RoleInstanceId = AzureRoleEnvironment.CurrentRoleInstanceId(),
                    DeploymentId = AzureRoleEnvironment.DeploymentId(),
                    Status = status,
                    IsOnline = true
                };

                _syncStatusRepository.UpdateStatus(syncStatus);
            }
        }
    }
}