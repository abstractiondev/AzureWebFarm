using System.IO;
using System.Reflection;
using AzureWebFarm.Services;
using NUnit.Framework;

namespace AzureWebFarm.Tests.Services
{
    [TestFixture]
    class BackgroundWorkerServiceShould
    {
        private static readonly string TestPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase.Replace("file:///", ""));

        private static readonly string SitesPath = Path.Combine(TestPath, "Sites");
        private static readonly string ExePath = Path.Combine(TestPath, "Executables");

        private static readonly string TestAppPath = "TestApp{0}";
        private static readonly string TestAppExe = Path.Combine(TestAppPath, "TestApp{0}.exe");

        private BackgroundWorkerService _service;

        [SetUp]
        public void Setup()
        {
            if (Directory.Exists(SitesPath))
                Directory.Delete(SitesPath, true);

            if (Directory.Exists(ExePath))
                Directory.Delete(ExePath, true);

            Directory.CreateDirectory(SitesPath);
            Directory.CreateDirectory(ExePath);

            _service = new BackgroundWorkerService(SitesPath, ExePath);
        }

        private static void SetupTestApp(int app, string siteName)
        {
            Directory.CreateDirectory(Path.Combine(SitesPath, siteName, "bin", string.Format(TestAppPath, app)));
            File.Copy(Path.Combine(TestPath, string.Format("TestApp{0}.exe", app)), Path.Combine(SitesPath, siteName, "bin", string.Format(TestAppExe, app)));
        }

        [Test]
        public void Run_executable()
        {
            const string siteName = "test";
            const int app = 1;
            SetupTestApp(app, siteName);

            _service.Update(siteName);

            _service.Wait();
            var outputFile = Path.Combine(ExePath, siteName, string.Format(TestAppPath, app), "file.txt");
            Assert.That(File.Exists(outputFile));
            Assert.That(File.ReadAllText(outputFile), Is.EqualTo(app.ToString()));
        }
    }
}
