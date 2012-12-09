using System;
using System.IO;
using System.Reflection;
using AzureWebFarm.Services;
using NUnit.Framework;

namespace AzureWebFarm.Tests.Services
{
    [TestFixture]
    class ExecutableShould
    {
        private static readonly string TestPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase.Replace("file:///", ""));
        private static readonly string OriginalPath = Path.Combine(TestPath, "original");
        private static readonly string ExecutePath = Path.Combine(TestPath, "execute");
        private Executable _e;

        private const string ExeContents = "asdf";
        private const string ExeName = "someexe";

        private const string RandomFileContents = "random";
        private const string RandomFilePath = "random.txt";

        private const string WebConfigContents = "web.config contents";

        [SetUp]
        public void Setup()
        {
            if (Directory.Exists(OriginalPath))
                Directory.Delete(OriginalPath, true);

            if (Directory.Exists(ExecutePath))
                Directory.Delete(ExecutePath, true);

            Directory.CreateDirectory(Path.Combine(OriginalPath, ExeName));

            File.WriteAllText(Path.Combine(OriginalPath, ExeName, string.Format("{0}.exe", ExeName)), ExeContents);
            File.WriteAllText(Path.Combine(OriginalPath, ExeName, RandomFilePath), RandomFileContents);
            File.WriteAllText(Path.Combine(TestPath, "web.config"), WebConfigContents);

            _e = new Executable(OriginalPath, ExeName);
        }

        [TearDown]
        public void Teardown()
        {
            _e.Dispose();
        }

        private void ArrangeTestExecutable(int i)
        {
            File.Copy(Path.Combine(TestPath, string.Format("TestApp{0}.exe", i)), Path.Combine(OriginalPath, ExeName, string.Format("{0}.exe", ExeName)), true);
        }
        
        [Test]
        public void Return_correct_original_file_paths()
        {
            Assert.That(_e.GetOriginalDirPath(), Is.EqualTo(Path.Combine(OriginalPath, ExeName)), "Original directory");
            Assert.That(_e.GetOriginalExePath(), Is.EqualTo(Path.Combine(OriginalPath, ExeName, string.Format("{0}.exe", ExeName))), "Original .exe path");
        }

        [Test]
        public void Return_whether_or_not_the_executable_exists([Values(true, false)] bool exists)
        {
            if (!exists)
                File.Delete(Path.Combine(OriginalPath, ExeName, string.Format("{0}.exe", ExeName)));

            Assert.That(_e.Exists(), Is.EqualTo(exists));
        }

        [Test]
        public void Copy_executable_file()
        {
            _e.Copy(ExecutePath);

            var exe = Path.Combine(ExecutePath, ExeName, string.Format("{0}.exe", ExeName));
            Assert.That(File.Exists(exe), ".exe exists");
            Assert.That(File.ReadAllText(exe), Is.EqualTo(ExeContents), ".exe was copied");
        }

        [Test]
        public void Copy_other_files()
        {
            _e.Copy(ExecutePath);

            var random = Path.Combine(ExecutePath, ExeName, RandomFilePath);
            Assert.That(File.Exists(random), "Non .exe file exists");
            Assert.That(File.ReadAllText(random), Is.EqualTo(RandomFileContents), "Non .exe file was copied");
        }

        [Test]
        public void Copy_website_web_config()
        {
            _e.Copy(ExecutePath);

            var config = Path.Combine(ExecutePath, ExeName, "web.config");
            Assert.That(File.Exists(config), "Web.config file exists");
            Assert.That(File.ReadAllText(config), Is.EqualTo(WebConfigContents), "Web.config file was copied");
        }

        [Test]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Execute_file(int testExecutable)
        {
            ArrangeTestExecutable(testExecutable);
            _e.Copy(ExecutePath);
            
            _e.Execute();
            
            _e.Wait();
            Assert.That(File.Exists(Path.Combine(ExecutePath, ExeName, "file.txt")), "Executable was actually run");
        }

        [Test]
        public void Throw_exception_when_copying_over_running_executable()
        {
            ArrangeTestExecutable(4);
            _e.Copy(ExecutePath);
            _e.Execute();

            var ex = Assert.Throws<InvalidOperationException>(() => _e.Copy(TestPath));
            Assert.That(ex.Message, Is.EqualTo("The executable is already running!"));
        }

        [Test]
        public void Throw_exception_when_trying_to_execute_twice()
        {
            ArrangeTestExecutable(4);
            _e.Copy(ExecutePath);
            _e.Execute();

            var ex = Assert.Throws<InvalidOperationException>(() => _e.Execute());
            Assert.That(ex.Message, Is.EqualTo("The executable is already running!"));
        }

        [Test]
        public void Throw_exception_when_trying_to_execute_without_copying_to_execution_folder()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => _e.Execute());
            Assert.That(ex.Message, Is.EqualTo("You must call .Copy() before .Execute()"));
        }

        [Test]
        public void Return_whether_the_exe_is_still_running()
        {
            ArrangeTestExecutable(4);
            _e.Copy(ExecutePath);

            _e.Execute();

            Assert.That(_e.IsRunning());
        }

        [Test]
        public void Return_whether_the_exe_is_not_still_running([Values(true, false)] bool wasRunningInFirstPlace)
        {
            ArrangeTestExecutable(1);
            _e.Copy(ExecutePath);

            if (wasRunningInFirstPlace)
                _e.Execute();
            _e.Wait();

            Assert.That(_e.IsRunning(), Is.False);
        }

        [Test]
        public void Do_nothing_if_process_already_successfully_finished_executing_when_pinged()
        {
            ArrangeTestExecutable(1);
            _e.Copy(ExecutePath);
            _e.Execute();
            _e.Wait();
            var stream = File.Open(Path.Combine(ExecutePath, ExeName, string.Format("{0}.exe", ExeName)), FileMode.Open);
            stream.Lock(0, 1);

            _e.Ping();

            stream.Unlock(0, 1);
            stream.Dispose();
        }

        [Test]
        public void Do_nothing_if_process_still_running()
        {
            ArrangeTestExecutable(4);
            _e.Copy(ExecutePath);
            _e.Execute();

            _e.Ping();
        }

        [Test]
        public void Restart_if_process_exited_with_non_zero_code()
        {
            ArrangeTestExecutable(3);
            _e.Copy(ExecutePath);
            _e.Execute();
            _e.Wait();

            _e.Ping();
            
            _e.Wait();
            Assert.That(File.ReadAllText(Path.Combine(ExecutePath, ExeName, "file.txt")), Is.EqualTo("2"));
        }

        [Test]
        public void Restart_multiple_times_if_process_exits_repeatedly_with_non_zero_code_but_not_after_it_returns_zero_exit_code()
        {
            ArrangeTestExecutable(3);
            _e.Copy(ExecutePath);
            _e.Execute();
            _e.Wait();

            _e.Ping(); // "2"
            _e.Wait();
            _e.Ping(); // "3"
            _e.Wait();
            _e.Ping(); // not run

            Assert.That(File.ReadAllText(Path.Combine(ExecutePath, ExeName, "file.txt")), Is.EqualTo("3"));
        }
    }
}
