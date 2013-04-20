using System;
using System.Reflection;
using System.Threading;
using AzureWebFarm.Helpers;
using Castle.Core.Logging;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace AzureWebFarm.Services
{
    public class WebDeployService
    {
        private readonly CloudBlobContainer _container;
        private readonly ILogger _logger;
        private Thread _leaseThread;
        private string leaseId;

        public WebDeployService(CloudStorageAccount storageAccount, ILoggerFactory loggerFactory, LoggerLevel logLevel)
        {
            _logger = loggerFactory.Create(GetType(), logLevel);

            _container = storageAccount.CreateCloudBlobClient().GetContainerReference(AzureRoleEnvironment.GetConfigurationSettingValue(Constants.WebDeployLeaseBlobContainerName));
            _container.CreateIfNotExist();
        }

        public void Start()
        {
            _leaseThread = new Thread(() =>
            {
                _logger.Debug("Starting web deploy leasing thread...");

                while (true)
                {
                    try
                    {
                        var blob = _container.GetBlockBlobReference("webdeploy-lease.blob");
                        using (var lease = new AutoRenewLease(blob))
                        {
                            _logger.DebugFormat("Leasing thread checking...HasLease: {0}", lease.HasLease);

                            while (lease.HasLease)
                            {
                                leaseId = lease.LeaseId;
                                _logger.DebugFormat("This instance ({0}) has the lease, updating blob with the instance ID.", AzureRoleEnvironment.CurrentRoleInstanceId());

                                blob.Metadata["InstanceId"] = AzureRoleEnvironment.CurrentRoleInstanceId();
                                blob.SetMetadata(lease.LeaseId);
                                Thread.Sleep(TimeSpan.FromSeconds(10));
                            }
                            leaseId = null;
                        }
                        Thread.Sleep(TimeSpan.FromSeconds(30));
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorFormat(ex, "Failed to manage lease on {0}", AzureRoleEnvironment.CurrentRoleInstanceId());
                    }
                    Thread.Sleep(TimeSpan.FromSeconds(30));
                }
            });
            _leaseThread.Start();
        }

        public void Stop()
        {
            if (_leaseThread != null)
            {
                _leaseThread.Abort();
                _leaseThread = null;
            }
            if (leaseId == null) return;

            var blob = _container.GetBlockBlobReference("webdeploy-lease.blob");
            blob.TryReleaseLease(leaseId);
            blob.Metadata.Remove("InstanceId");
            blob.SetMetadata();
        }
    }
}