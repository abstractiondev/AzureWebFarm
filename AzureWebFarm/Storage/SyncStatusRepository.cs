using System;
using System.Collections.Generic;
using System.Linq;
using AzureToolkit;
using AzureWebFarm.Entities;
using AzureWebFarm.Extensions;
using AzureWebFarm.Helpers;

namespace AzureWebFarm.Storage
{
    public interface ISyncStatusRepository
    {
        void RemoveWebSiteStatus(string webSiteName);
        void UpdateStatus(SyncStatus syncStatus);
        IEnumerable<SyncStatus> RetrieveSyncStatus(string webSiteName);
        IEnumerable<SyncStatus> RetrieveSyncStatusByInstanceId(string roleInstanceId);
    }

    public class SyncStatusRepository : ISyncStatusRepository
    {
        private readonly IAzureTable<SyncStatusRow> _table;

        public SyncStatusRepository(IAzureStorageFactory storageFactory)
        {
            _table = storageFactory.GetTable<SyncStatusRow>(typeof (SyncStatusRow).Name);
            _table.Initialize();
        }

        public void RemoveWebSiteStatus(string webSiteName)
        {
            var webSiteStatus = RetrieveSyncStatus(webSiteName);
            if (webSiteStatus != null && webSiteStatus.Any())
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
                    s.PartitionKey.Equals(AzureRoleEnvironment.DeploymentId(), StringComparison.OrdinalIgnoreCase) &&
                    s.SiteName.Equals(webSiteName, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .Select(s => s.ToModel());
        }

        public IEnumerable<SyncStatus> RetrieveSyncStatusByInstanceId(string roleInstanceId)
        {
            return _table.Query
                .Where(
                    s =>
                    s.PartitionKey.Equals(AzureRoleEnvironment.DeploymentId(), StringComparison.OrdinalIgnoreCase) &&
                    s.RoleInstanceId.Equals(roleInstanceId, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .Select(s => s.ToModel());
        }
    }
}