using System;
using System.Threading;
using AzureWebFarm.Helpers;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using NUnit.Framework;

namespace AzureWebFarm.Tests.Helpers
{
    [TestFixture]
    public class AutoRenewLeaseShould
    {
        private CloudBlobContainer _container;

        [SetUp]
        public void Setup()
        {
            var storageAccount = CloudStorageAccount.DevelopmentStorageAccount;
            _container = storageAccount.CreateCloudBlobClient().GetContainerReference("webdeploylease");
            _container.CreateIfNotExist();
        }

        [Test]
        public void Hold_lease_while_in_using_block()
        {
            var blob = _container.GetBlockBlobReference("webdeploy-lease.blob");

            using (var lease = new AutoRenewLease(blob, renewLeaseSeconds: 1, leaseLengthSeconds: 2))
            {
                // Inside a lease we cannot perform operations without the lease ID
                Assert.Throws<StorageClientException>(blob.SetMetadata);
                
                // We should be able to perform this operation with the lease ID
                blob.SetMetadata(lease.LeaseId);
                
                // Should still be leased 4 seconds later
                Thread.Sleep(TimeSpan.FromSeconds(4));
                Assert.Throws<StorageClientException>(blob.SetMetadata);
            }

            // Should no longer be a lease on this blob
            blob.SetMetadata();
        }
    }
}