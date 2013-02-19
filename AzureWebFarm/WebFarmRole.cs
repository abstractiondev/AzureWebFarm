using System;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using AzureWebFarm.Helpers;
using AzureWebFarm.Services;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace AzureWebFarm
{
    public class WebFarmRole
    {
        private SyncService _syncService;
        private BackgroundWorkerService _backgroundWorker;

        public WebFarmRole()
        {
            ExceptionRaised += ExceptionHandler;
        }

        public void OnStart()
        {
            Trace.TraceInformation("WebRole.OnStart");
            try
            {
                ServicePointManager.DefaultConnectionLimit = 12;
                CloudStorageAccount.SetConfigurationSettingPublisher((configName, configSetter) =>
                {
                    var configuration = RoleEnvironment.IsAvailable ?
                        RoleEnvironment.GetConfigurationSettingValue(configName) :
                        ConfigurationManager.AppSettings[configName];

                    configSetter(configuration);
                });

                if (RoleEnvironment.IsAvailable && !RoleEnvironment.IsEmulated)
                    DiagnosticsHelper.ConfigureDiagnosticMonitor();

                // Initialize local resources
                var localSitesPath = GetLocalResourcePathAndSetAccess("Sites");
                var localTempPath = GetLocalResourcePathAndSetAccess("TempSites");
                var localExecutionPath = GetLocalResourcePathAndSetAccess("Execution");

                // Get settings
                var directoriesToExclude = RoleEnvironment.GetConfigurationSettingValue("DirectoriesToExclude").Split(';');

                // WebDeploy creates temporary files during package creation. The default TEMP location allows for a 100MB
                // quota (see http://msdn.microsoft.com/en-us/library/gg465400.aspx#Y976). 
                // For large web deploy packages, the synchronization process will raise an IO exception because the "disk is full" 
                // unless you ensure that the TEMP/TMP target directory has sufficient space
                Environment.SetEnvironmentVariable("TMP", localTempPath);
                Environment.SetEnvironmentVariable("TEMP", localTempPath);

                // Create the sync service and background worker
                _syncService = new SyncService(localSitesPath, localTempPath, directoriesToExclude, "DataConnectionstring");
                _backgroundWorker = new BackgroundWorkerService(localSitesPath, localExecutionPath);

                // Subscribe the background worker to relevant events in the sync service
                _syncService.Ping += (sender, args) => _backgroundWorker.Ping();
                _syncService.SiteUpdated += (sender, args, siteName) => _backgroundWorker.Update(siteName);
                _syncService.SiteDeleted += (sender, args, siteName) => _backgroundWorker.DisposeSite(siteName);
                // Update the sites with initial state
                _syncService.Start();
            }
            catch (Exception e)
            {
                OnException(e);
                DiagnosticsHelper.WriteExceptionToBlobStorage(e);
                throw;
            }
        }

        // ReSharper disable FunctionNeverReturns
        public void Run()
        {
            try
            {
                Trace.TraceInformation("WebRole.Run");
                var syncInterval = Int32.Parse(RoleEnvironment.GetConfigurationSettingValue("SyncIntervalInSeconds"), CultureInfo.InvariantCulture);
                _syncService.SyncForever(TimeSpan.FromSeconds(syncInterval));
                while (true)
                {
                    Thread.Sleep(10000);
                }
            }
            catch (Exception e)
            {
                OnException(e);
                DiagnosticsHelper.WriteExceptionToBlobStorage(e);
                throw;
            }
        }
        // ReSharper restore FunctionNeverReturns

        public void OnStop()
        {
            Trace.TraceInformation("WebRole.OnStop");

            // Set the sites as not synced for this instance
            var roleInstanceId = RoleEnvironment.IsAvailable ? RoleEnvironment.CurrentRoleInstance.Id : Environment.MachineName;
            _syncService.UpdateAllSitesSyncStatus(roleInstanceId, false);
        }

        private static event ExceptionEventHandler ExceptionRaised;
        public delegate void ExceptionEventHandler(Exception ex);
        
        public static void OnException(Exception ex)
        {
            var handler = ExceptionRaised;
            if (handler != null)
                handler(ex);
        }

        /// <summary>
        /// Override this method to provide a handler for logging (or performing other actions as a result of) exceptions.
        /// </summary>
        /// <param name="e">The exception raised by AzureWebFarm</param>
        protected virtual void ExceptionHandler(Exception e)
        {
            Trace.TraceError(e.ToString());
        }
        
        private static string GetLocalResourcePathAndSetAccess(string localResourceName)
        {
            var resourcePath = RoleEnvironment.GetLocalResource(localResourceName).RootPath.TrimEnd('\\');

            var localDataSec = Directory.GetAccessControl(resourcePath);
            localDataSec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(resourcePath, localDataSec);

            return resourcePath;
        }
    }
}
