using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Web.Script.Serialization;
using System.Xml.Serialization;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace AzureWebFarm.Storage
{
    public class AzureBlobContainer<T> : IAzureBlobContainer<T>
    {
        private readonly CloudStorageAccount account;
        private readonly CloudBlobContainer container;

        public AzureBlobContainer(CloudStorageAccount account)
            : this(account, typeof (T).Name.ToLowerInvariant())
        {
        }

        public AzureBlobContainer(CloudStorageAccount account, string containerName)
        {
            this.account = account;

            CloudBlobClient client = this.account.CreateCloudBlobClient();
            client.RetryPolicy = RetryPolicies.Retry(3, TimeSpan.FromSeconds(5));

            container = client.GetContainerReference(containerName.ToLowerInvariant());
        }

        #region IAzureBlobContainer<T> Members

        public void EnsureExist()
        {
            container.CreateIfNotExist();
        }

        public void EnsureExist(bool publicContainer)
        {
            container.CreateIfNotExist();
            var permissions = new BlobContainerPermissions();

            if (publicContainer)
            {
                permissions.PublicAccess = BlobContainerPublicAccessType.Container;
            }

            container.SetPermissions(permissions);
        }

        public void Save(string objId, T obj)
        {
            CloudBlob blob = container.GetBlobReference(objId);
            blob.Properties.ContentType = "application/json";
            var serializer = new JavaScriptSerializer();
            blob.UploadText(serializer.Serialize(obj));
        }

        public void SaveAsXml(string objId, T obj)
        {
            CloudBlob blob = container.GetBlobReference(objId);
            blob.Properties.ContentType = "text/xml";
            var serializer = new XmlSerializer(typeof (T));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, obj);
                blob.UploadText(writer.ToString());
            }
        }

        public string SaveFile(string objId, byte[] content, string contentType)
        {
            CloudBlob blob = container.GetBlobReference(objId);
            blob.Properties.ContentType = contentType;
            blob.UploadByteArray(content);
            return blob.Uri.ToString();
        }

        public string SaveFile(string objId, byte[] content, string contentType, TimeSpan timeOut)
        {
            TimeSpan currentTimeOut = container.ServiceClient.Timeout;
            container.ServiceClient.Timeout = timeOut;
            string result = SaveFile(objId, content, contentType);
            container.ServiceClient.Timeout = currentTimeOut;
            return result;
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "If we dispose the stream the clien won't be able to use it")]
        public Stream GetFile(string objId)
        {
            Stream stream = new MemoryStream();
            CloudBlob blob = container.GetBlobReference(objId);
            blob.DownloadToStream(stream);
            stream.Seek(0, 0);
            return stream;
        }

        public byte[] GetBytes(string objId)
        {
            using (var stream = new MemoryStream())
            {
                try
                {
                    CloudBlob blob = container.GetBlobReference(objId);
                    blob.DownloadToStream(stream);
                    return stream.ToArray();
                }
                catch (StorageClientException)
                {
                    return null;
                }
            }
        }

        public T Get(string objId)
        {
            CloudBlob blob = container.GetBlobReference(objId);
            try
            {
                var serializer = new JavaScriptSerializer();
                return serializer.Deserialize<T>(blob.DownloadText());
            }
            catch (StorageClientException)
            {
                return default(T);
            }
        }

        public T GetFromXml(string objId)
        {
            CloudBlob blob = container.GetBlobReference(objId);
            try
            {
                var serializer = new XmlSerializer(typeof (T));
                using (var reader = new StringReader(blob.DownloadText()))
                {
                    return (T) serializer.Deserialize(reader);
                }
            }
            catch (StorageClientException)
            {
                return default(T);
            }
        }

        public void Delete(string objId)
        {
            CloudBlob blob = container.GetBlobReference(objId);
            blob.DeleteIfExists();
        }

        #endregion
    }
}