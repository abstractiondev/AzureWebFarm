using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using AzureWebFarm.Entities;

namespace AzureWebFarm.Storage
{
    public class CertificateRepository
    {
        public List<Certificate> RetrieveCertificates()
        {
            var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);

            var certificates = (
                from X509Certificate2 cert
                in store.Certificates
                select new Certificate
                {
                    Thumbprint = cert.Thumbprint
                }
            ).ToList();

            store.Close();

            return certificates;
        }
    }
}