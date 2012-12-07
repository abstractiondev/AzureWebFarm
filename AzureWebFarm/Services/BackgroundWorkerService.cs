using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace AzureWebFarm.Services
{
    public class BackgroundWorkerService
    {
        private readonly string _sitesPath;
        private readonly string _executablePath;
        private readonly Dictionary<string, List<Executable>> _executables;
        private ExecutableFinder _executableFinder;

        public BackgroundWorkerService(string sitesPath, string executablePath)
        {
            _sitesPath = sitesPath;
            _executablePath = executablePath;
            _executables = new Dictionary<string, List<Executable>>();
            _executableFinder = new ExecutableFinder(sitesPath);
        }

        public void Update(string siteName)
        {
            if (!_executables.ContainsKey(siteName))
                _executables[siteName] = new List<Executable>();

            var currentState = _executableFinder.FindExecutables(siteName);
            
            // todo: are there any console apps that aren't there any more?
            // todo: are there any new console apps?

            foreach (var e in currentState)
            {
                e.Copy(Path.Combine(_executablePath, siteName));
                e.Execute();
                // todo: remove this
                e.Wait();
            }
        }
    }

    public class ExecutableFinder
    {
        private readonly string _sitesPath;

        public ExecutableFinder(string sitesPath)
        {
            _sitesPath = sitesPath;
        }

        public IEnumerable<Executable> FindExecutables(string siteName)
        {
            var subDirs = Directory.EnumerateDirectories(Path.Combine(_sitesPath, siteName));
            foreach (var d in subDirs)
            {
                var subDir = d.Split(Path.DirectorySeparatorChar).Last();
                var exe = new Executable(Path.Combine(_sitesPath, siteName), subDir);
                
                if (exe.Exists())
                    yield return exe;
            }
        } 
    }

    public class Executable : IDisposable
    {
        private readonly string _basePath;
        private string _executionPath;
        private readonly string _exeName;
        private Process _process;

        public Executable(string basePath, string exeName)
        {
            _basePath = basePath;
            _exeName = exeName;
        }

        public string GetOriginalExePath()
        {
            return Path.Combine(_basePath, _exeName, string.Format("{0}.exe", _exeName));
        }

        public string GetExecutionDirPath()
        {
            return Path.Combine(_executionPath, _exeName);
        }

        public string GetExecutionExePath()
        {
            return Path.Combine(GetExecutionDirPath(), string.Format("{0}.exe", _exeName));
        }

        public bool Exists()
        {
            return File.Exists(GetOriginalExePath());
        }

        public void Copy(string executionPath)
        {
            if (IsRunning())
                Kill();

            _executionPath = executionPath;

            if (!Directory.Exists(GetExecutionDirPath()))
                Directory.CreateDirectory(GetExecutionDirPath());

            File.Copy(GetOriginalExePath(), GetExecutionExePath(), true);
        }

        public void Kill()
        {
            _process.Kill();
            _process.Dispose();
            _process = null;
        }

        public void Wait()
        {
            _process.WaitForExit();
        }

        public void Execute()
        {
            if (IsRunning())
                throw new InvalidOperationException("The executable is already running!");

            if (string.IsNullOrEmpty(_executionPath))
                throw new InvalidOperationException("You must call .Copy() before .Execute()");

            var startInfo = new ProcessStartInfo(GetExecutionExePath())
            {
                WorkingDirectory = GetExecutionDirPath(),
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            _process = Process.Start(startInfo);
        }

        public bool IsRunning()
        {
            return _process != null && !_process.HasExited;
        }

        public void Ping()
        {
            if (IsRunning())
                return;

            // todo: log stuff
            if (_process.ExitCode != 0)
                _process.Start();
        }

        public void Dispose()
        {
            if (_process != null)
                _process.Dispose();
        }
    }
}
