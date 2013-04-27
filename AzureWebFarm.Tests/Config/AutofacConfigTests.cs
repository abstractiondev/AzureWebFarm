using Autofac;
using AzureWebFarm.Config;
using AzureWebFarm.Helpers;
using AzureWebFarm.Services;
using Castle.Core.Logging;
using Microsoft.WindowsAzure;
using NUnit.Framework;

namespace AzureWebFarm.Tests.Config
{
    class AutofacConfigShould
    {
        private IContainer _container;

        [TestFixtureSetUp]
        public void FixtureSetup()
        {
            AzureRoleEnvironment.GetLocalResourcePath = c => "c:\\temp\\" + c;
            AzureRoleEnvironment.GetConfigurationSettingValue = c => c;
            _container = AutofacConfig.BuildContainer(CloudStorageAccount.DevelopmentStorageAccount, new ConsoleFactory(), LoggerLevel.Debug);
        }

        [Test]
        public void AllowSyncServiceToBeResolved()
        {
            var syncService = _container.Resolve<SyncService>();

            Assert.That(syncService, Is.Not.Null);
        }

        [Test]
        public void AllowWebDeployServiceToBeResolved()
        {
            var webDeployService = _container.Resolve<WebDeployService>();

            Assert.That(webDeployService, Is.Not.Null);
        }

        [Test]
        public void AllowBackgroundWorkerServiceToBeResolved()
        {
            var backgroundWorkerService = _container.Resolve<BackgroundWorkerService>();

            Assert.That(backgroundWorkerService, Is.Not.Null);
        }
    }
}
