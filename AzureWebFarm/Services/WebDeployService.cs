using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AzureWebFarm.Helpers;
using Castle.Core.Logging;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace AzureWebFarm.Services
{
    public class WebDeployService
    {
        private readonly ILogger _logger;
        private Task _leaseTask;
        private string _leaseId;
        private ILoggerFactory _loggerFactory;
        private readonly LoggerLevel _logLevel;
        private CancellationTokenSource _cancellationToken;
        private readonly object _lock = new object();

        public WebDeployService(ILoggerFactory loggerFactory, LoggerLevel logLevel)
        {
            _loggerFactory = loggerFactory;
            _logLevel = logLevel;
            _logger = loggerFactory.Create(GetType(), logLevel);
        }

        public void Start()
        {
            _cancellationToken = new CancellationTokenSource();

            _leaseTask = Task.Factory.StartNew(() =>
            {
                _logger.Debug("Starting web deploy leasing thread...");

                while (true)
                {
                    try
                    {
                        var blob = AzureRoleEnvironment.WebDeployLeaseBlob();
                        using (var lease = new AutoRenewLease(_loggerFactory, _logLevel, blob))
                        {
                            _logger.DebugFormat("Leasing thread checking...HasLease: {0}", lease.HasLease);

                            while (lease.HasLease)
                            {
                                if (_leaseId != lease.LeaseId)
                                {
                                    _logger.DebugFormat("This instance ({0}) has the lease, updating blob with the instance ID.", AzureRoleEnvironment.CurrentRoleInstanceId());
                                    blob.Metadata["InstanceId"] = AzureRoleEnvironment.CurrentRoleInstanceId();
                                    blob.SetMetadata(lease.LeaseId);
                                    _leaseId = lease.LeaseId;
                                }

                                lock (_lock)
                                {
                                    Monitor.Wait(_lock, TimeSpan.FromSeconds(10));
                                    if (_cancellationToken.IsCancellationRequested)
                                        break;
                                }
                            }
                            if (!_cancellationToken.IsCancellationRequested)
                                _leaseId = null;
                        }

                        lock (_lock)
                        {
                            Monitor.Wait(_lock, TimeSpan.FromSeconds(30));
                            if (_cancellationToken.IsCancellationRequested)
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorFormat(ex, "Failed to manage lease on {0}", AzureRoleEnvironment.CurrentRoleInstanceId());
                        Thread.Sleep(TimeSpan.FromSeconds(30));
                    }
                }
            }, _cancellationToken.Token);
        }

        public void Stop()
        {
            if (_cancellationToken != null && !_cancellationToken.IsCancellationRequested)
            {
                try
                {
                    lock (_lock)
                    {
                        _cancellationToken.Cancel();
                        Monitor.Pulse(_lock);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("An error occured aborting the web deploy lease thread.", ex);
                }
            }
            if (_leaseId == null) return;

            try
            {
                var blob = AzureRoleEnvironment.WebDeployLeaseBlob();
                blob.TryReleaseLease(_leaseId);
                blob.Metadata.Remove("InstanceId");
                blob.SetMetadata();
                _leaseId = null;
            }
            catch (Exception ex)
            {
                _logger.Error("An exception occured when attempting to clear the InstanceId from the web deploy lease metadata.", ex);
            }
        }
    }
}