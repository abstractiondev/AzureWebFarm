using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace AzureWebFarm.Services
{
    public class BackgroundWorkerService
    {
        private readonly string _localPath;
        private readonly string _executablePath;

        public BackgroundWorkerService(string localPath, string executablePath)
        {
            _localPath = localPath;
            _executablePath = executablePath;
            // todo: scan for exes and build definition
        }

        public void Update(string siteName)
        {
            // todo: is it a new site?
            // todo: are there any console apps that aren't there any more?
            // todo: are there any new console apps?
            // todo: kill old one

            var subDirs = Directory.EnumerateDirectories(Path.Combine(_localPath, siteName));
            foreach (var d in subDirs)
            {
                var subDir = d.Split(Path.DirectorySeparatorChar).Last();
                var exe = Path.Combine(d, string.Format("{0}.exe", subDir));
                if (!File.Exists(exe))
                    continue;

                var exeDir = Path.Combine(_executablePath, siteName, subDir);
                var dest = Path.Combine(exeDir, string.Format("{0}.exe", subDir));
                if (!Directory.Exists(exeDir))
                    Directory.CreateDirectory(exeDir);
                File.Copy(exe, dest, true);

                var startInfo = new ProcessStartInfo(dest)
                {
                    WorkingDirectory = exeDir,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                }
            }
        }
    }
}
