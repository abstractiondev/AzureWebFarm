using System;
using System.Collections.Generic;
using System.Linq;
using AzureToolkit;
using AzureWebFarm.Entities;
using AzureWebFarm.Storage;
using Microsoft.WindowsAzure;
using NUnit.Framework;

namespace AzureWebFarm.Tests.Storage
{
    [TestFixture]
    public class WebSiteRepositoryShould
    {
        #region Setup/Teardown

        [SetUp]
        public void Setup()
        {
            _webSiteTable = new AzureTable<WebSiteRow>(CloudStorageAccount.DevelopmentStorageAccount, "WebSitesTest");
            _bindingTable = new AzureTable<BindingRow>(CloudStorageAccount.DevelopmentStorageAccount, "BindingsTest");
            _webSiteTable.Initialize();
            _bindingTable.Initialize();
            _repository = new WebSiteRepository(_webSiteTable, _bindingTable);
        }

        #endregion

        private WebSiteRepository _repository;
        private AzureTable<WebSiteRow> _webSiteTable;
        private AzureTable<BindingRow> _bindingTable;

        private static IEnumerable<WebSiteRow> CreateAndSaveWebSiteRows(AzureTable<WebSiteRow> table, int count)
        {
            var sites = new List<WebSiteRow>();

            for (var k = 0; k < count; k++)
            {
                sites.Add(CreateWebSiteRow());
            }

            table.Add(sites);

            return sites;
        }

        private static WebSiteRow CreateWebSiteRow()
        {
            var id = Guid.NewGuid();

            return new WebSiteRow(id)
            {
                Name = "Web Site " + id.ToString(),
                Description = "Description " + id.ToString()
            };
        }

        private WebSite CreateWebSiteWithBindings(int nbindings)
        {
            var id = Guid.NewGuid();
            var bindings = new List<Binding>();

            var site = new WebSite(id)
            {
                Name = "Test Web Site " + id.ToString(),
                Description = "Description Test Web Site " + id.ToString()
            };

            var binding = new Binding
            {
                Protocol = "http",
                IpAddress = string.Empty,
                Port = 80,
                HostName = "www.test0.com"
            };

            _repository.CreateWebSiteWithBinding(site, binding);
            bindings.Add(binding);

            for (var k = 1; k < nbindings; k++)
            {
                var otherBinding = new Binding
                {
                    Protocol = "http",
                    IpAddress = string.Empty,
                    Port = 80 + k,
                    HostName = string.Format("www.test{0}.com", k)
                };

                _repository.AddBindingToWebSite(site.Id, otherBinding);
                bindings.Add(otherBinding);
            }

            site.Bindings = bindings;

            return site;
        }

        [Test]
        public void Create_and_remove_web_site_with_initial_binding()
        {
            var newsite = default(WebSiteRow);
            var newbindings = default(IEnumerable<BindingRow>);

            try
            {
                var site = CreateWebSiteWithBindings(1);
                var binding = site.Bindings.First();

                var id = site.Id.ToString();
                var idb = binding.Id.ToString();

                newsite = _webSiteTable.Query.Where(t => t.RowKey == id).FirstOrDefault();
                newbindings = _bindingTable.Query.Where(b => b.RowKey == idb);

                Assert.IsNotNull(newsite);

                _repository.RemoveWebSite(site.Id);

                newsite = _webSiteTable.Query.Where(t => t.RowKey == id).FirstOrDefault();
                Assert.IsNull(newsite);

                newbindings = _bindingTable.Query.Where(b => b.RowKey == idb);

                Assert.IsNotNull(newbindings);
                Assert.AreEqual(0, newbindings.Count());
            }
            finally
            {
                if (newsite != null)
                {
                    _webSiteTable.Delete(newsite);
                }

                if (newbindings != null && newbindings.Any())
                {
                    _bindingTable.Delete(newbindings);
                }
            }
        }

        [Test]
        public void Create_new_web_site()
        {
            var site = new WebSite
            {
                Name = "Test Web Site",
                Description = "Description Test Web Site"
            };

            _repository.CreateWebSite(site);

            var id = site.Id.ToString();

            var newsite = _webSiteTable.Query.Where(t => t.RowKey == id).FirstOrDefault();

            Assert.IsNotNull(newsite);
            _webSiteTable.Delete(newsite);
        }

        [Test]
        public void CreateNewWebSiteWithInitialBinding()
        {
            var newsite = default(WebSiteRow);
            var newbinding = default(BindingRow);

            try
            {
                var site = CreateWebSiteWithBindings(1);
                var binding = site.Bindings.First();

                var id = site.Id.ToString();
                var idb = binding.Id.ToString();

                newsite = _webSiteTable.Query.Where(t => t.RowKey == id).FirstOrDefault();

                Assert.IsNotNull(newsite);
                Assert.AreEqual(site.Name, newsite.Name);
                Assert.AreEqual(site.Description, newsite.Description);

                newbinding = _bindingTable.Query.Where(b => b.RowKey == idb).FirstOrDefault();

                Assert.IsNotNull(newbinding);
                Assert.AreEqual(binding.WebSiteId, newbinding.WebSiteId);
                Assert.AreEqual(binding.IpAddress, newbinding.IpAddress);
                Assert.AreEqual(binding.HostName, newbinding.HostName);
                Assert.AreEqual(binding.Port, newbinding.Port);
                Assert.AreEqual(binding.Protocol, newbinding.Protocol);

                var sites = _repository.RetrieveWebSitesWithBindings();

                Assert.IsTrue(sites.Any(s => s.Id == site.Id && s.Bindings != null && s.Bindings.Count() == 1));
            }
            finally
            {
                if (newsite != null)
                {
                    _webSiteTable.Delete(newsite);
                }

                if (newbinding != null)
                {
                    _bindingTable.Delete(newbinding);
                }
            }
        }

        [Test]
        public void Create_new_web_site_with_many_bindings()
        {
            var newsite = default(WebSiteRow);
            var newbindings = default(IEnumerable<BindingRow>);

            try
            {
                var site = CreateWebSiteWithBindings(10);

                var id = site.Id.ToString();

                newsite = _webSiteTable.Query.Where(t => t.RowKey == id).FirstOrDefault();

                Assert.IsNotNull(newsite);
                Assert.AreEqual(site.Name, newsite.Name);
                Assert.AreEqual(site.Description, newsite.Description);

                newbindings = _bindingTable.Query.Where(b => b.WebSiteId == site.Id);

                Assert.IsNotNull(newbindings);
                Assert.AreEqual(10, newbindings.Count());

                WebSite siteb = _repository.RetrieveWebSiteWithBindings(site.Id);

                Assert.IsNotNull(siteb);
                Assert.IsNotNull(siteb.Bindings);
                Assert.AreEqual(10, siteb.Bindings.Count());
            }
            finally
            {
                if (newsite != null)
                {
                    _webSiteTable.Delete(newsite);
                }

                if (newbindings != null && newbindings.Any())
                {
                    _bindingTable.Delete(newbindings);
                }
            }
        }

        [Test]
        public void Remove_binding()
        {
            var newsite = default(WebSiteRow);
            var newbindings = default(IEnumerable<BindingRow>);

            try
            {
                var site = CreateWebSiteWithBindings(2);
                var binding = site.Bindings.First();

                var id = site.Id.ToString();
                var idb = binding.Id.ToString();

                newsite = _webSiteTable.Query.Where(t => t.RowKey == id).FirstOrDefault();

                _repository.RemoveBinding(binding.Id);

                newbindings = _bindingTable.Query.Where(b => b.WebSiteId == site.Id);
                Assert.IsNotNull(newbindings);
                Assert.AreEqual(1, newbindings.Count());
            }
            finally
            {
                if (newsite != null)
                {
                    _webSiteTable.Delete(newsite);
                }

                if (newbindings != null && newbindings.Any())
                {
                    _bindingTable.Delete(newbindings);
                }
            }
        }

        [Test]
        public void Remove_web_site()
        {
            var newsite = default(WebSiteRow);
            var newbindings = default(IEnumerable<BindingRow>);

            try
            {
                var site = CreateWebSiteWithBindings(10);

                var id = site.Id.ToString();

                newsite = _webSiteTable.Query.Where(t => t.RowKey == id).FirstOrDefault();
                newbindings = _bindingTable.Query.Where(b => b.WebSiteId == site.Id);

                _repository.RemoveWebSite(site.Id);

                newsite = _webSiteTable.Query.Where(t => t.RowKey == id).FirstOrDefault();
                newbindings = _bindingTable.Query.Where(b => b.WebSiteId == site.Id);

                Assert.IsNull(newsite);
                Assert.IsNotNull(newbindings);
                Assert.AreEqual(0, newbindings.Count());
            }
            finally
            {
                if (newsite != null)
                {
                    _webSiteTable.Delete(newsite);
                }

                if (newbindings != null && newbindings.Any())
                {
                    _bindingTable.Delete(newbindings);
                }
            }
        }

        [Test]
        public void Retrieve_web_sites()
        {
            var siteInfos = CreateAndSaveWebSiteRows(_webSiteTable, 10);

            try
            {
                var sites = _repository.RetrieveWebSites();

                Assert.IsNotNull(sites);
                Assert.IsTrue(sites.Count() >= 10);

                foreach (WebSite site in sites)
                {
                    Assert.AreNotEqual(Guid.Empty, site.Id);
                }

                foreach (WebSiteRow siteInfo in siteInfos)
                {
                    Assert.IsTrue(sites.Any(s => s.Id.ToString() == siteInfo.RowKey));
                }
            }
            finally
            {
                _webSiteTable.Delete(siteInfos);
            }
        }

        [Test]
        public void Update_binding()
        {
            var newsite = default(WebSiteRow);
            var newbindings = default(IEnumerable<BindingRow>);

            try
            {
                var site = CreateWebSiteWithBindings(1);
                var binding = site.Bindings.First();

                var id = site.Id.ToString();
                var idb = binding.Id.ToString();

                newsite = _webSiteTable.Query.Where(t => t.RowKey == id).FirstOrDefault();
                newbindings = _bindingTable.Query.Where(b => b.RowKey == idb);

                binding.HostName = "www.newhost.com";
                binding.IpAddress = "127.0.0.2";
                binding.Protocol = "https";
                binding.CertificateThumbprint = Guid.NewGuid().ToString();
                _repository.UpdateBinding(binding);

                BindingRow newbinding = _bindingTable.Query.Where(b => b.RowKey == idb).FirstOrDefault();
                Assert.IsNotNull(newbinding);
                Assert.AreEqual(binding.HostName, newbinding.HostName);
                Assert.AreEqual(binding.IpAddress, newbinding.IpAddress);
                Assert.AreEqual(binding.Protocol, newbinding.Protocol);
                Assert.AreEqual(binding.CertificateThumbprint, newbinding.CertificateThumbprint);
            }
            finally
            {
                if (newsite != null)
                {
                    _webSiteTable.Delete(newsite);
                }

                if (newbindings != null && newbindings.Any())
                {
                    _bindingTable.Delete(newbindings);
                }
            }
        }

        [Test]
        public void Update_web_site()
        {
            var newsite = default(WebSiteRow);
            var newbindings = default(IEnumerable<BindingRow>);

            try
            {
                var site = CreateWebSiteWithBindings(1);
                var binding = site.Bindings.First();

                var id = site.Id.ToString();
                var idb = binding.Id.ToString();

                newsite = _webSiteTable.Query.Where(t => t.RowKey == id).FirstOrDefault();
                newbindings = _bindingTable.Query.Where(b => b.RowKey == idb);

                site.Name = "New Name";
                site.Description = "New Description";
                _repository.UpdateWebSite(site);

                newsite = _webSiteTable.Query.Where(t => t.RowKey == id).FirstOrDefault();
                Assert.IsNotNull(newsite);
                Assert.AreEqual(site.Name, newsite.Name);
                Assert.AreEqual(site.Description, newsite.Description);
            }
            finally
            {
                if (newsite != null)
                {
                    _webSiteTable.Delete(newsite);
                }

                if (newbindings != null && newbindings.Any())
                {
                    _bindingTable.Delete(newbindings);
                }
            }
        }
    }
}