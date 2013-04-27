using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Castle.Core.Logging;
using Microsoft.WindowsAzure.StorageClient;

namespace AzureWebFarm.Helpers
{
    /// <summary>
    /// Helper library to maintain a lease while in a using block. Attempts to autorenew a 90 second lease every 40 seconds (customisable) rather than indefinitely, meaning the lease isn't locked forever if the instance crashes.
    /// Based on https://github.com/smarx/WazStorageExtensions pending a pull request we have sent to this project.
    /// </summary>
    internal class AutoRenewLease : IDisposable
    {
        private readonly CloudBlob _blob;
        private bool _disposed;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ILogger _logger;
        private readonly ManualResetEvent _resetEvent;

        public bool HasLease
        {
            get { return LeaseId != null; }
        }

        public string LeaseId
        {
            get; 
            private set;
        }

        public AutoRenewLease(ILoggerFactory loggerFactory, LoggerLevel logLevel, CloudBlob blob, int renewLeaseSeconds = 40, int leaseLengthSeconds = 90)
        {
            _logger = loggerFactory.Create(GetType(), logLevel);
            var autoRenewLease = this;
            _blob = blob;
            blob.Container.CreateIfNotExist();
            try
            {
                blob.UploadByteArray(new byte[0], new BlobRequestOptions { AccessCondition = AccessCondition.IfNoneMatch("*")});
            }
            catch (StorageClientException ex)
            {
                if (ex.ErrorCode != StorageErrorCode.BlobAlreadyExists)
                {
                    if (ex.StatusCode != HttpStatusCode.PreconditionFailed)
                        throw;
                }
            }
            LeaseId = blob.TryAcquireLease(leaseLengthSeconds);
            if (!HasLease)
                return;
            _cancellationTokenSource = new CancellationTokenSource();
            _resetEvent = new ManualResetEvent(false);
            Task.Factory.StartNew(() =>
            {
                try
                {
                    while (true)
                    {
                        _resetEvent.WaitOne(TimeSpan.FromSeconds(renewLeaseSeconds));
                        if (_cancellationTokenSource.IsCancellationRequested)
                            break;

                        blob.RenewLease(autoRenewLease.LeaseId);
                    }
                }
                catch (Exception e)
                {
                    LeaseId = null; // Release the lease
                    _logger.Error("Error renewing blob lease", e);
                }
            }, _cancellationTokenSource.Token);
        }

        ~AutoRenewLease()
        {
            Dispose(false);
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;
            if (disposing && _cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                _resetEvent.Set();

                try
                {
                    _blob.TryReleaseLease(LeaseId);
                }
                catch (Exception ex)
                {
                    _logger.Error("An error occured aborting the web deploy lease thread.", ex);
                }
            }
            _disposed = true;
        }
    }
}