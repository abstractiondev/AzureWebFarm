using System;
using System.Linq;
using AzureToolkit;
using AzureWebFarm.Entities;
using AzureWebFarm.Example.Web.Controllers;
using AzureWebFarm.Example.Web.Models;
using AzureWebFarm.Storage;
using Microsoft.WindowsAzure;
using NUnit.Framework;

namespace AzureWebFarm.Tests.Controllers
{
    [TestFixture]
    public class WebSiteControllerShould
    {
        #region Setup/Teardown

        [SetUp]
        public void Setup()
        {
            _certificateRepository = new CertificateRepository();
            var factory = new AzureStorageFactory(CloudStorageAccount.DevelopmentStorageAccount);
            _webSiteRepository = new WebSiteRepository(factory);
            _webSiteTable = factory.GetTable<WebSiteRow>(typeof (WebSiteRow).Name);
            _bindingTable = factory.GetTable<BindingRow>(typeof (BindingRow).Name);
            _controller = new WebSiteController(_webSiteRepository, _certificateRepository);
        }

        #endregion

        private WebSiteRepository _webSiteRepository;
        private CertificateRepository _certificateRepository;
        private IAzureTable<WebSiteRow> _webSiteTable;
        private IAzureTable<BindingRow> _bindingTable;
        private WebSiteController _controller;

        [Test]
        public void Create_web_site_with_bindings()
        {
            var id = Guid.NewGuid();

            var model = new WebSiteCreateModel
            {
                Name = "testsite" + id.ToString().ToLowerInvariant(),
                Description = "Test Description",
                Port = 80,
                IpAddress = string.Empty,
                HostName = "www.mydomain.com",
                Protocol = "http"
            };

            _controller.Create(model);

            var newsite = _webSiteRepository.RetrieveWebSites().FirstOrDefault(ws => ws.Name == model.Name);

            try
            {
                Assert.IsNotNull(newsite);
                Assert.AreEqual(model.Name, newsite.Name);
                Assert.AreEqual(model.Description, newsite.Description);

                WebSite site = _webSiteRepository.RetrieveWebSiteWithBindings(newsite.Id);

                Assert.IsNotNull(site);
                Assert.IsNotNull(site.Bindings);
                Assert.AreEqual(1, site.Bindings.Count());

                Binding binding = site.Bindings.First();

                Assert.AreEqual(model.Port, binding.Port);
                Assert.AreEqual(site.Id, binding.WebSiteId);
                Assert.AreEqual("*", binding.IpAddress);
                Assert.AreEqual(model.HostName, binding.HostName);
                Assert.AreEqual(model.Protocol, binding.Protocol);
            }
            finally
            {
                if (newsite != null)
                {
                    var key = newsite.Id.ToString();
                    _bindingTable.Delete(_bindingTable.Query.Where(b => b.WebSiteId == newsite.Id));
                    _webSiteTable.Delete(_webSiteTable.Query.Where(b => b.RowKey == key));
                }
            }
        }
    }
}