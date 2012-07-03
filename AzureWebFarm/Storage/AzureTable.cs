using System.Collections.Generic;
using System.Data.Services.Client;
using System.Globalization;
using System.Linq;
using AzureWebFarm.Extensions;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace AzureWebFarm.Storage
{
    public class AzureTable<T> : IAzureTable<T> where T : TableServiceEntity, new()
    {
        private readonly CloudStorageAccount account;
        private readonly string tableName;

        public AzureTable()
            : this(CloudStorageAccount.DevelopmentStorageAccount)
        {
        }

        public AzureTable(CloudStorageAccount account)
            : this(account, typeof (T).Name)
        {
        }

        public AzureTable(CloudStorageAccount account, string tableName)
        {
            this.tableName = tableName;
            this.account = account;
        }

        #region IAzureTable<T> Members

        public IQueryable<T> Query
        {
            get
            {
                TableServiceContext context = CreateContext();
                return context.CreateQuery<T>(tableName).AsTableServiceQuery();
            }
        }

        public bool CreateIfNotExist()
        {
            var cloudTableClient = new CloudTableClient(account.TableEndpoint.ToString(), account.Credentials);
            return cloudTableClient.CreateTableIfNotExist<T>(tableName);
        }

        public bool DeleteIfExist()
        {
            var cloudTableClient = new CloudTableClient(account.TableEndpoint.ToString(), account.Credentials);
            return cloudTableClient.DeleteTableIfExist(tableName);
        }

        public void AddEntity(T obj)
        {
            AddEntity(new[] {obj});
        }

        public void AddEntity(IEnumerable<T> objs)
        {
            TableServiceContext context = CreateContext();

            foreach (var obj in objs)
            {
                context.AddObject(tableName, obj);
            }

            var saveChangesOptions = SaveChangesOptions.None;
            if (objs.Distinct(new PartitionKeyComparer()).Count() == 1)
            {
                saveChangesOptions = SaveChangesOptions.Batch;
            }

            context.SaveChanges(saveChangesOptions);
        }

        public void AddOrUpdateEntity(T obj)
        {
            AddOrUpdateEntity(new[] {obj});
        }

        public void AddOrUpdateEntity(IEnumerable<T> objs)
        {
            foreach (var obj in objs)
            {
                var pk = obj.PartitionKey;
                var rk = obj.RowKey;
                T existingObj = null;

                try
                {
                    existingObj = (from o in Query
                                   where o.PartitionKey == pk && o.RowKey == rk
                                   select o).SingleOrDefault();
                }
                catch
                {
                }

                if (existingObj == null)
                {
                    AddEntity(obj);
                }
                else
                {
                    TableServiceContext context = CreateContext();
                    context.AttachTo(tableName, obj, "*");
                    context.UpdateObject(obj);
                    context.SaveChanges(SaveChangesOptions.ReplaceOnUpdate);
                }
            }
        }

        public void DeleteEntity(T obj)
        {
            DeleteEntity(new[] {obj});
        }

        public void DeleteEntity(IEnumerable<T> objs)
        {
            TableServiceContext context = CreateContext();
            foreach (var obj in objs)
            {
                context.AttachTo(tableName, obj, "*");
                context.DeleteObject(obj);
            }

            try
            {
                context.SaveChanges();
            }
            catch (DataServiceRequestException ex)
            {
                var dataServiceClientException = ex.InnerException as DataServiceClientException;
                if (dataServiceClientException != null)
                {
                    if (dataServiceClientException.StatusCode == 404)
                    {
                        return;
                    }
                }

                throw;
            }
        }

        #endregion

        private TableServiceContext CreateContext()
        {
            var context = new TableServiceContext(account.TableEndpoint.ToString(), this.account.Credentials)
            {
                ResolveType = t => typeof(T),
                RetryPolicy = RetryPolicies.RetryExponential(RetryPolicies.DefaultClientRetryCount, RetryPolicies.DefaultClientBackoff)
            };

            return context;
        }

        #region Nested type: PartitionKeyComparer

        private class PartitionKeyComparer : IEqualityComparer<TableServiceEntity>
        {
            #region IEqualityComparer<TableServiceEntity> Members

            public bool Equals(TableServiceEntity x, TableServiceEntity y)
            {
                return string.Compare(x.PartitionKey, y.PartitionKey, true, CultureInfo.InvariantCulture) == 0;
            }

            public int GetHashCode(TableServiceEntity obj)
            {
                return obj.PartitionKey.GetHashCode();
            }

            #endregion
        }

        #endregion
    }
}