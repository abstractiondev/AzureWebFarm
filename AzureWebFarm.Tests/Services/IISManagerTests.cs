using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using AzureWebFarm.Entities;
using AzureWebFarm.Services;
using NUnit.Framework;

namespace AzureWebFarm.Tests.Services
{
    [TestFixture]
    public class IISManagerTests
    {
        private static string contosoWebSiteName = "contosotest";
        private static string fabrikamWebSiteName = "fabrikamtest";
        private static string localSitesPath = Path.Combine(Environment.CurrentDirectory, "testLocalSites");
        private static string tempSitesPath = Path.Combine(Environment.CurrentDirectory, "testTempSites");

        [SetUp]
        public void MyTestInitialize()
        {
            Cleanup();
            Setup();
        }

        [TearDown]
        public void MyTestCleanup()
        {
            Cleanup();
        }

        [Test]
        public void UpdateSitesWithInitialBindings()
        {
            var contosoWebSite = new WebSite
            {
                Name = contosoWebSiteName,
                Bindings = new List<Binding>
                {
                    new Binding { Protocol = "http", IpAddress = "*", Port = 8081, HostName = "contoso.com" }
                }
            };
            
            var fabrikamWebSite = new WebSite
            {
                Name = fabrikamWebSiteName, 
                Bindings = new List<Binding>
                {
                    new Binding { Protocol = "https", IpAddress = "*", Port = 8443, CertificateThumbprint = "12354" },
                    new Binding { Protocol = "http", IpAddress = "127.0.0.1", Port = 8082 }
                }
            };

            var iisManager = new IISManager(localSitesPath, tempSitesPath, null);
            var sites = new List<WebSite> { contosoWebSite, fabrikamWebSite };

            iisManager.UpdateSites(sites, false);

            // Asserts
            Assert.AreEqual(sites.Count, RetrieveWebSites().Count());
            
            var contoso = RetrieveWebSite(contosoWebSiteName);

            Assert.IsNotNull(contoso);
            Assert.AreEqual(contosoWebSite.Name, contoso.Name);
            Assert.AreEqual(contosoWebSite.Bindings.Count(), contoso.Bindings.Count);

            Assert.AreEqual(contosoWebSite.Bindings.First().HostName, contoso.Bindings.First().Host);
            Assert.AreEqual(contosoWebSite.Bindings.First().Protocol, contoso.Bindings.First().Protocol);
            Assert.AreEqual("0.0.0.0", contoso.Bindings.First().EndPoint.Address.ToString());
            Assert.AreEqual(contosoWebSite.Bindings.First().Port, contoso.Bindings.First().EndPoint.Port);
            Assert.IsNull(contoso.Bindings.First().CertificateHash);

            var fabrikam = RetrieveWebSite(fabrikamWebSiteName);

            Assert.IsNotNull(fabrikam);
            Assert.AreEqual(fabrikamWebSite.Name, fabrikam.Name);
            Assert.AreEqual(fabrikamWebSite.Bindings.Count(), fabrikam.Bindings.Count);

            Assert.IsTrue(string.IsNullOrEmpty(fabrikam.Bindings.First().Host));
            Assert.AreEqual(fabrikamWebSite.Bindings.First().Protocol, fabrikam.Bindings.First().Protocol);
            Assert.AreEqual(string.Empty, fabrikam.Bindings.First().Host);
            Assert.AreEqual("0.0.0.0", fabrikam.Bindings.First().EndPoint.Address.ToString());
            Assert.AreEqual(fabrikamWebSite.Bindings.First().Port, fabrikam.Bindings.First().EndPoint.Port);
            Assert.AreEqual(StoreName.My.ToString().ToUpperInvariant(), fabrikam.Bindings.First().CertificateStoreName.ToUpperInvariant());
            Assert.IsNotNull(fabrikam.Bindings.First().CertificateHash);

            Assert.IsTrue(string.IsNullOrEmpty(fabrikam.Bindings.Last().Host));
            Assert.AreEqual(fabrikamWebSite.Bindings.Last().Protocol, fabrikam.Bindings.Last().Protocol);
            Assert.AreEqual(fabrikamWebSite.Bindings.Last().IpAddress, fabrikam.Bindings.Last().EndPoint.Address.ToString());
            Assert.AreEqual(fabrikamWebSite.Bindings.Last().Port, fabrikam.Bindings.Last().EndPoint.Port);
            Assert.IsNull(fabrikam.Bindings.Last().CertificateHash);
        }

        [Test]
        public void UpdateSitesAddingBindings()
        {
            var contosoWebSite = new WebSite
            {
                Name = contosoWebSiteName,
                Bindings = new List<Binding>
                {
                    new Binding { Protocol = "http", IpAddress = "10.0.0.1", Port = 8081, HostName = "contoso.com" }
                }
            };

            var iisManager = new IISManager(localSitesPath, tempSitesPath, null);
            var sites = new List<WebSite> { contosoWebSite };

            iisManager.UpdateSites(sites, false);

            var contoso = RetrieveWebSite(contosoWebSiteName);

            Assert.IsNotNull(contoso);
            Assert.AreEqual(contosoWebSite.Name, contoso.Name);
            Assert.AreEqual(contosoWebSite.Bindings.Count(), contoso.Bindings.Count);

            // Add a new binding (https)
            var contosoBindings = contosoWebSite.Bindings.ToList();
            contosoBindings.Add(new Binding { Protocol = "https", IpAddress = "10.0.0.1", Port = 8443, CertificateThumbprint = "12345" });
            contosoWebSite.Bindings = contosoBindings;

            iisManager.UpdateSites(sites, false);

            // Asserts
            Assert.AreEqual(sites.Count, RetrieveWebSites().Count());

            contoso = RetrieveWebSite(contosoWebSiteName);

            Assert.IsNotNull(contoso);
            Assert.AreEqual(contosoWebSite.Name, contoso.Name);
            Assert.AreEqual(2, contoso.Bindings.Count);
            
            Assert.AreEqual(contosoWebSite.Bindings.First().HostName, contoso.Bindings.First().Host);
            Assert.AreEqual(contosoWebSite.Bindings.First().Protocol, contoso.Bindings.First().Protocol);
            Assert.AreEqual(contosoWebSite.Bindings.First().IpAddress, contoso.Bindings.First().EndPoint.Address.ToString());
            Assert.AreEqual(contosoWebSite.Bindings.First().Port, contoso.Bindings.First().EndPoint.Port);
            Assert.IsNull(contoso.Bindings.First().CertificateHash);

            Assert.IsTrue(string.IsNullOrEmpty(contoso.Bindings.Last().Host));
            Assert.AreEqual(contosoWebSite.Bindings.Last().Protocol, contoso.Bindings.Last().Protocol);
            Assert.AreEqual(contosoWebSite.Bindings.Last().IpAddress, contoso.Bindings.Last().EndPoint.Address.ToString());
            Assert.AreEqual(contosoWebSite.Bindings.Last().Port, contoso.Bindings.Last().EndPoint.Port);
            Assert.AreEqual(StoreName.My.ToString().ToUpperInvariant(), contoso.Bindings.Last().CertificateStoreName.ToUpperInvariant());
            Assert.IsNotNull(contoso.Bindings.Last().CertificateHash);
        }

        [Test]
        public void UpdateSitesRemovingBindings()
        {
            var fabrikamWebSite = new WebSite
            {
                Name = fabrikamWebSiteName,
                Bindings = new List<Binding>
                {
                    new Binding { Protocol = "https", IpAddress = "127.0.0.1", Port = 8443, CertificateThumbprint = "12345" },
                    new Binding { Protocol = "http", IpAddress = "127.0.0.1", Port = 8082 }
                }
            };

            var iisManager = new IISManager(localSitesPath, tempSitesPath, null);
            var sites = new List<WebSite> { fabrikamWebSite };

            iisManager.UpdateSites(sites, false);

            var fabrikam = RetrieveWebSite(fabrikamWebSiteName);

            Assert.IsNotNull(fabrikam);
            Assert.AreEqual(fabrikamWebSite.Name, fabrikam.Name);
            Assert.AreEqual(2, fabrikam.Bindings.Count);

            var fabrikamBindings = fabrikamWebSite.Bindings.ToList();
            fabrikamBindings.RemoveAt(1);
            fabrikamWebSite.Bindings = fabrikamBindings;

            iisManager.UpdateSites(sites, false);

            // Asserts
            Assert.AreEqual(sites.Count(), RetrieveWebSites().Count());

            fabrikam = RetrieveWebSite(fabrikamWebSiteName);

            Assert.IsNotNull(fabrikam);
            Assert.AreEqual(fabrikamWebSite.Name, fabrikam.Name);
            Assert.AreEqual(1, fabrikam.Bindings.Count);

            Assert.IsTrue(string.IsNullOrEmpty(fabrikam.Bindings.First().Host));
            Assert.AreEqual(fabrikamWebSite.Bindings.First().Protocol, fabrikam.Bindings.First().Protocol);
            Assert.AreEqual(fabrikamWebSite.Bindings.First().IpAddress, fabrikam.Bindings.First().EndPoint.Address.ToString());
            Assert.AreEqual(fabrikamWebSite.Bindings.First().Port, fabrikam.Bindings.First().EndPoint.Port);
            Assert.AreEqual(StoreName.My.ToString().ToUpperInvariant(), fabrikam.Bindings.First().CertificateStoreName.ToUpperInvariant());
            Assert.IsNotNull(fabrikam.Bindings.First().CertificateHash);
        }

        [Test]
        public void UpdateSitesRemovingSite()
        {
            var contosoWebSite = new WebSite
            {
                Name = contosoWebSiteName,
                Bindings = new List<Binding>
                {
                    new Binding { Protocol = "http", IpAddress = "127.0.0.1", Port = 8081, HostName = "contoso.com" }
                }
            };

            var fabrikamWebSite = new WebSite
            {
                Name = fabrikamWebSiteName,
                Bindings = new List<Binding>
                {
                    new Binding { Protocol = "https", IpAddress = "127.0.0.1", Port = 8443, CertificateThumbprint = "12345" }
                }
            };

            var iisManager = new IISManager(localSitesPath, tempSitesPath, null);
            var sites = new List<WebSite> { contosoWebSite, fabrikamWebSite };

            iisManager.UpdateSites(sites, false);

            Assert.AreEqual(2, RetrieveWebSites().Count());

            sites.RemoveAt(0);
            iisManager.UpdateSites(sites, false);

            // Asserts
            Assert.AreEqual(1, RetrieveWebSites().Count());

            var contoso = RetrieveWebSite(contosoWebSiteName);
            var fabrikam = RetrieveWebSite(fabrikamWebSiteName);

            Assert.IsNull(contoso);
            Assert.IsNotNull(fabrikam);
        }

        private static void Setup()
        {
            Directory.CreateDirectory(localSitesPath);
            Directory.CreateDirectory(tempSitesPath);
        }

        private static void Cleanup()
        {
            RemoveWebSite(contosoWebSiteName);
            RemoveWebSite(fabrikamWebSiteName);

            RemoveDirectory(localSitesPath);
            RemoveDirectory(tempSitesPath);
        }

        private static IEnumerable<Microsoft.Web.Administration.Site> RetrieveWebSites()
        {
            using (var serverManager = new Microsoft.Web.Administration.ServerManager())
            {
                return serverManager.Sites.Where(s => s.Name != "Default Web Site").ToList();
            }
        }

        private static Microsoft.Web.Administration.Site RetrieveWebSite(string siteName)
        {
            using (var serverManager = new Microsoft.Web.Administration.ServerManager())
            {
                return serverManager.Sites.SingleOrDefault(s => s.Name == siteName);
            }
        }

        private static void RemoveWebSite(string siteName)
        {
            using (var serverManager = new Microsoft.Web.Administration.ServerManager())
            {
                var testSite = serverManager.Sites.SingleOrDefault(s => s.Name == siteName);
                if (testSite != null)
                {
                    serverManager.Sites.Remove(testSite);
                    serverManager.CommitChanges();
                }

                var testAppPool = serverManager.ApplicationPools.SingleOrDefault(s => s.Name == siteName);
                if (testAppPool != null)
                {
                    serverManager.ApplicationPools.Remove(testAppPool);
                    serverManager.CommitChanges();
                }
            }
        }

        private static void RemoveDirectory(string directoryPath)
        {
            if (Directory.Exists(directoryPath))
            {
                try
                {
                    Directory.Delete(tempSitesPath);
                }
                catch (DirectoryNotFoundException)
                { 
                }
            }
        }
    }
}