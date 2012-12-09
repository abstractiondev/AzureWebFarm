using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.AccessControl;
using AzureWebFarm.Services;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.StorageClient;

namespace AzureWebFarm
{
    public class WebFarmRole
    {
        // Core references from here http://weblogs.thinktecture.com/cweyer/2011/01/trying-to-troubleshoot-windows-azure-compute-role-startup-issues.html
        public static void WriteExceptionToBlobStorage(Exception ex)
        {
            var storageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue("Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString"));

            var container = storageAccount.CreateCloudBlobClient().GetContainerReference("exceptions");
            container.CreateIfNotExist();

            var blob = container.GetBlobReference(string.Format("exception-{0}-{1}.log", RoleEnvironment.CurrentRoleInstance.Id, DateTime.UtcNow.Ticks));
            blob.UploadText(ex.ToString());
        }

        private SyncService _syncService;

        public void OnStart()
        {
            Trace.TraceInformation("WebRole.OnStart");
            try
            {
                ServicePointManager.DefaultConnectionLimit = 12;
                CloudStorageAccount.SetConfigurationSettingPublisher((configName, configSetter) =>
                {
                    string configuration = RoleEnvironment.IsAvailable ?
                        RoleEnvironment.GetConfigurationSettingValue(configName) :
                        ConfigurationManager.AppSettings[configName];

                    configSetter(configuration);
                });

                if (RoleEnvironment.IsAvailable && !RoleEnvironment.IsEmulated)
                    ConfigureDiagnosticMonitor();

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

                // Create the sync service and background worker service and update the sites status
                _syncService = new SyncService(localSitesPath, localTempPath, directoriesToExclude, "DataConnectionstring");
                var backgroundWorker = new BackgroundWorkerService(localSitesPath, localExecutionPath);
                _syncService.Start();
            }
            catch (Exception e)
            {
                Trace.TraceError(e.ToString());
                WriteExceptionToBlobStorage(e);
                throw;
            }
        }

        // ReSharper disable FunctionNeverReturns
        public void Run()
        {
            try
            {
                Trace.TraceInformation("WebRole.Run");
                var syncInterval = int.Parse(RoleEnvironment.GetConfigurationSettingValue("SyncIntervalInSeconds"), CultureInfo.InvariantCulture);
                _syncService.SyncForever(TimeSpan.FromSeconds(syncInterval));
                while (true)
                {
                    System.Threading.Thread.Sleep(10000);
                }
            }
            catch (Exception e)
            {
                Trace.TraceError(e.ToString());
                WriteExceptionToBlobStorage(e);
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

        private static void ConfigureDiagnosticMonitor()
        {
            var transferPeriod = TimeSpan.FromMinutes(5);
            const int bufferQuotaInMb = 100;

            // Add Windows Azure Trace Listener
            Trace.Listeners.Add(new DiagnosticMonitorTraceListener());

            // Enable Collection of Crash Dumps
            CrashDumps.EnableCollection(true);

            // Get the Default Initial Config
            var config = DiagnosticMonitor.GetDefaultInitialConfiguration();

            // Windows Azure Logs
            config.Logs.ScheduledTransferPeriod = transferPeriod;
            config.Logs.BufferQuotaInMB = bufferQuotaInMb;
            config.Logs.ScheduledTransferLogLevelFilter = LogLevel.Information;

            // File-based logs
            config.Directories.ScheduledTransferPeriod = transferPeriod;
            config.Directories.BufferQuotaInMB = bufferQuotaInMb;

            config.DiagnosticInfrastructureLogs.ScheduledTransferPeriod = transferPeriod;
            config.DiagnosticInfrastructureLogs.BufferQuotaInMB = bufferQuotaInMb;
            config.DiagnosticInfrastructureLogs.ScheduledTransferLogLevelFilter = LogLevel.Warning;

            // Windows Event logs
            config.WindowsEventLog.DataSources.Add("Application!*");
            config.WindowsEventLog.DataSources.Add("System!*");
            config.WindowsEventLog.ScheduledTransferPeriod = transferPeriod;
            config.WindowsEventLog.ScheduledTransferLogLevelFilter = LogLevel.Information;
            config.WindowsEventLog.BufferQuotaInMB = bufferQuotaInMb;

            // Performance Counters
            var counters = new List<string> {
                @"\Processor(_Total)\% Processor Time",
                @"\Memory\Available MBytes",
                @"\ASP.NET Applications(__Total__)\Requests Total",
                @"\ASP.NET Applications(__Total__)\Requests/Sec",
                @"\ASP.NET\Requests Queued",
            };

            counters.ForEach(counter => config.PerformanceCounters.DataSources.Add(new PerformanceCounterConfiguration { CounterSpecifier = counter, SampleRate = TimeSpan.FromSeconds(60) }));
            config.PerformanceCounters.ScheduledTransferPeriod = transferPeriod;
            config.PerformanceCounters.BufferQuotaInMB = bufferQuotaInMb;

            DiagnosticMonitor.Start("Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString", config);
        }

        private static string GetLocalResourcePathAndSetAccess(string localResourceName)
        {
            string resourcePath = RoleEnvironment.GetLocalResource(localResourceName).RootPath.TrimEnd('\\');

            var localDataSec = Directory.GetAccessControl(resourcePath);
            localDataSec.AddAccessRule(new FileSystemAccessRule(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(resourcePath, localDataSec);

            return resourcePath;
        }
    }
}
