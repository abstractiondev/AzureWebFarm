using System;
using System.Collections.Generic;
using System.Linq;
using AzureToolkit;
using AzureWebFarm.Entities;
using AzureWebFarm.Extensions;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace AzureWebFarm.Storage
{
    public class SyncStatusRepository
    {
        private readonly IAzureTable<SyncStatusRow> _table;

        public SyncStatusRepository()
            : this("DataConnectionString")
        {
        }

        public SyncStatusRepository(string settingName)
            : this(CloudStorageAccount.FromConfigurationSetting(settingName), "WebSitesSyncStatus")
        {
        }

        public SyncStatusRepository(CloudStorageAccount account)
            : this(account, "WebSitesSyncStatus")
        {
        }

        public SyncStatusRepository(CloudStorageAccount account, string tableName)
            : this(new AzureTable<SyncStatusRow>(account, tableName))
        {
        }

        public SyncStatusRepository(IAzureTable<SyncStatusRow> table)
        {
            _table = table;
            _table.Initialize();
        }

        public void RemoveWebSiteStatus(string webSiteName)
        {
            var webSiteStatus = RetrieveSyncStatus(webSiteName);
            if (webSiteStatus != null && webSiteStatus.Count() > 0)
            {
                _table.Delete(webSiteStatus.Select(s => s.ToRow()));
            }
        }

        public void UpdateStatus(SyncStatus syncStatus)
        {
            _table.AddOrUpdate(syncStatus.ToRow());
        }

        public IEnumerable<SyncStatus> RetrieveSyncStatus(string webSiteName)
        {
            return _table.Query
                .Where(
                    s =>
                    s.PartitionKey.Equals(RoleEnvironment.DeploymentId, StringComparison.OrdinalIgnoreCase) &&
                    s.SiteName.Equals(webSiteName, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .Select(s => s.ToModel());
        }

        public IEnumerable<SyncStatus> RetrieveSyncStatusByInstanceId(string roleInstanceId)
        {
            return _table.Query
                .Where(
                    s =>
                    s.PartitionKey.Equals(RoleEnvironment.DeploymentId, StringComparison.OrdinalIgnoreCase) &&
                    s.RoleInstanceId.Equals(roleInstanceId, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .Select(s => s.ToModel());
        }
    }
}