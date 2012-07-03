using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
using System.IO;
using System.Web.Helpers;
using AzureWebFarm.Entities;
using AzureWebFarm.Extensions;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace AzureWebFarm.Storage
{
    public class CertificateRepository
    {

        private string certsFilePath;

        public CertificateRepository()
        {
            this.certsFilePath = Path.Combine(RoleEnvironment.GetLocalResource("Config").RootPath, "certs.json");
        }

        public void PopulateRepository()
        {
            var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);

            var certificates = new List<Certificate>();
            foreach (var cert in store.Certificates)
            {
                    certificates.Add(new Certificate
                    {
                        Thumbprint = cert.Thumbprint,
                    });
            }
            store.Close();

            var json = Json.Encode(certificates);
            File.WriteAllText(certsFilePath, json);
        }

        public List<Certificate> RetrieveCertificates()
        {
            try
            {
                var json = File.ReadAllText(certsFilePath);
                return Json.Decode<List<Certificate>>(json);
            }
            catch (Exception ex)
            {
                Trace.TraceError("Could not retrieve certificates. See next message for details.");
                Trace.TraceError(ex.TraceInformation());
                return new List<Certificate>();
            }
        }

    }
}