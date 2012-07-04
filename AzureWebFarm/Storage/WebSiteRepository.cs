using System;
using System.Collections.Generic;
using System.Linq;
using AzureToolkit;
using AzureWebFarm.Entities;
using AzureWebFarm.Extensions;
using Microsoft.WindowsAzure;

namespace AzureWebFarm.Storage
{
    public class WebSiteRepository
    {
        private readonly AzureTable<BindingRow> _bindingTable;
        private readonly AzureTable<WebSiteRow> _webSiteTable;

        public WebSiteRepository()
            : this("DataConnectionString")
        {
        }

        public WebSiteRepository(string settingName)
            : this(CloudStorageAccount.FromConfigurationSetting(settingName), "WebSites", "Bindings")
        {
        }

        public WebSiteRepository(CloudStorageAccount account)
            : this(account, "WebSites", "Bindings")
        {
        }

        public WebSiteRepository(CloudStorageAccount account, string webSiteTableName, string bindingTableName)
            : this(new AzureTable<WebSiteRow>(account, webSiteTableName), new AzureTable<BindingRow>(account, bindingTableName))
        {
        }

        public WebSiteRepository(AzureTable<WebSiteRow> webSiteTable, AzureTable<BindingRow> bindingTable)
        {
            _webSiteTable = webSiteTable;
            _bindingTable = bindingTable;

            _webSiteTable.Initialize();
            _bindingTable.Initialize();
        }

        public void CreateWebSite(WebSite webSite)
        {
            _webSiteTable.Add(webSite.ToRow());
        }

        public void CreateWebSiteWithBinding(WebSite webSite, Binding binding)
        {
            binding.WebSiteId = webSite.Id;

            _webSiteTable.Add(webSite.ToRow());
            _bindingTable.Add(binding.ToRow());
        }

        public void AddBindingToWebSite(Guid webSiteId, Binding binding)
        {
            binding.WebSiteId = webSiteId;
            _bindingTable.Add(binding.ToRow());
        }

        public void RemoveBinding(Guid bindingId)
        {
            string key = bindingId.ToString();
            _bindingTable.Delete(_bindingTable.Query.Where(b => b.RowKey == key));
        }

        public void EditBinding(Binding binding)
        {
            _bindingTable.AddOrUpdate(binding.ToRow());
        }

        public void UpdateWebSite(WebSite webSite)
        {
            _webSiteTable.AddOrUpdate(webSite.ToRow());
        }

        public void UpdateBinding(Binding binding)
        {
            _bindingTable.AddOrUpdate(binding.ToRow());
        }

        public void RemoveWebSite(Guid webSiteId)
        {
            string key = webSiteId.ToString();

            var websites = _webSiteTable.Query.Where(ws => ws.RowKey == key);
            var bindings = _bindingTable.Query.Where(b => b.WebSiteId == webSiteId);

            _webSiteTable.Delete(websites);
            _bindingTable.Delete(bindings);
        }

        public WebSite RetrieveWebSite(Guid webSiteId)
        {
            string key = webSiteId.ToString();

            return _webSiteTable.Query.Where(ws => ws.RowKey == key).FirstOrDefault().ToModel();
        }

        public Binding RetrieveBinding(Guid bindingId)
        {
            string key = bindingId.ToString();

            return _bindingTable.Query.Where(b => b.RowKey == key).FirstOrDefault().ToModel();
        }

        public WebSite RetrieveWebSiteWithBindings(Guid webSiteId)
        {
            WebSite website = RetrieveWebSite(webSiteId);

            website.Bindings = RetrieveWebSiteBindings(webSiteId);

            return website;
        }

        public IEnumerable<Binding> RetrieveWebSiteBindings(Guid webSiteId)
        {
            return _bindingTable.Query.Where(b => b.WebSiteId == webSiteId).ToList().Select(b => b.ToModel()).ToList();
        }

        public IEnumerable<Binding> RetrieveCertificateBindings(string certificateHash)
        {
            var bindings = _bindingTable.Query.Where(b => b.CertificateThumbprint == certificateHash).ToList().Select(b => b.ToModel()).ToList();

            var sites = new Dictionary<Guid, WebSite>();

            foreach (var binding in bindings)
            {
                if (!sites.ContainsKey(binding.WebSiteId))
                {
                    sites[binding.WebSiteId] = RetrieveWebSite(binding.WebSiteId);
                }

                binding.WebSite = sites[binding.WebSiteId];
            }

            return bindings;
        }

        public IEnumerable<Binding> RetrieveBindingsForPort(int port)
        {
            return _bindingTable.Query.Where(b => b.Port == port).ToList().Select(b => b.ToModel()).ToList();
        }

        public void AddBindingToWebSite(WebSite webSite, Binding binding)
        {
            binding.WebSiteId = webSite.Id;
            _bindingTable.Add(binding.ToRow());
        }

        public IEnumerable<WebSite> RetrieveWebSites()
        {
            return _webSiteTable.Query.ToList().OrderBy(t => t.Name).Select(ws => ws.ToModel()).ToList();
        }

        public IEnumerable<WebSite> RetrieveWebSitesWithBindings()
        {
            var sites = RetrieveWebSites();

            foreach (var site in sites)
            {
                site.Bindings = RetrieveWebSiteBindings(site.Id);
            }

            return sites;
        }
    }
}