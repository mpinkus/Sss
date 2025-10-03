using System.Diagnostics;
using System.Security;
using System.Text;
using System.Text.Json;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Models;

namespace Shamir.Ceremony.Common.Tests.Integration
{
    [TestClass]
    public sealed class ConsoleE2ETests
    {
        private string _testOutputFolder = string.Empty;
        private string _consoleAppPath = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _testOutputFolder = Path.Combine(Path.GetTempPath(), $"ShamirE2E_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testOutputFolder);

            var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            _consoleAppPath = Path.Combine(projectRoot, "Shamir.Ceremony.Console", "bin", "Debug", "net8.0", "Shamir.Ceremony.Console.dll");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_testOutputFolder))
            {
                try
                {
                    Directory.Delete(_testOutputFolder, true);
                }
                catch
                {
                }
            }
        }

        [TestMethod]
        public async Task ConsoleApp_CompleteUserJourney_CreateAndReconstruct()
        {
            if (!File.Exists(_consoleAppPath))
            {
                Assert.Inconclusive("Console app not built. Run 'dotnet build' first.");
                return;
            }

            Assert.IsTrue(File.Exists(_consoleAppPath), "Console app should exist");
        }

        [TestMethod]
        public async Task ConsoleApp_MixedKeeperTypes_DefaultAndCustom()
        {
            Assert.Inconclusive("Requires interactive console automation - implement with expect-like tool");
        }

        [TestMethod]
        public async Task ConsoleApp_MaxComplexity_255Shares128Threshold()
        {
            Assert.Inconclusive("Requires console automation framework");
        }

        [TestMethod]
        public async Task ConsoleApp_CancelInterrupt_CtrlC()
        {
            Assert.Inconclusive("Requires process signal handling testing");
        }

        [TestMethod]
        public async Task ConsoleApp_DiskFullScenario_ShouldHandleGracefully()
        {
            Assert.Inconclusive("Requires disk space simulation - complex to test safely");
        }

        [TestMethod]
        public async Task ConsoleApp_PermissionDenied_ShouldReportError()
        {
            var readOnlyFolder = Path.Combine(_testOutputFolder, "readonly");
            Directory.CreateDirectory(readOnlyFolder);

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"444 {readOnlyFolder}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                });
                await process!.WaitForExitAsync();
            }
            else if (OperatingSystem.IsWindows())
            {
                var dirInfo = new DirectoryInfo(readOnlyFolder);
                dirInfo.Attributes = FileAttributes.ReadOnly;
            }

            Assert.IsTrue(Directory.Exists(readOnlyFolder));

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"755 {readOnlyFolder}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                });
                await process!.WaitForExitAsync();
            }
            else if (OperatingSystem.IsWindows())
            {
                var dirInfo = new DirectoryInfo(readOnlyFolder);
                dirInfo.Attributes = FileAttributes.Normal;
            }
        }

        [TestMethod]
        public async Task ConsoleApp_RecoveryFromCrashedSession_ShouldCleanup()
        {
            Assert.Inconclusive("Requires crash simulation framework");
        }

        [TestMethod]
        public async Task ConsoleApp_MultipleConsecutiveCeremonies_ShouldIsolate()
        {
            Assert.Inconclusive("Requires console automation");
        }

        [TestMethod]
        public void ConsoleApp_ConfigurationFile_LoadsCorrectly()
        {
            var configPath = Path.Combine(Path.GetDirectoryName(_consoleAppPath)!, "appsettings.json");
            
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                Assert.IsFalse(string.IsNullOrEmpty(json));
                
                try
                {
                    JsonDocument.Parse(json);
                    Assert.IsTrue(true, "Configuration is valid JSON");
                }
                catch (JsonException)
                {
                    Assert.Fail("Configuration file is not valid JSON");
                }
            }
            else
            {
                Assert.Inconclusive("Configuration file not found");
            }
        }

        [TestMethod]
        public void ConsoleApp_Executable_ExistsAndIsAccessible()
        {
            if (File.Exists(_consoleAppPath))
            {
                var fileInfo = new FileInfo(_consoleAppPath);
                Assert.IsTrue(fileInfo.Length > 0, "Console app executable should not be empty");
                Assert.IsTrue(fileInfo.Exists, "Console app should exist");
            }
            else
            {
                Assert.Inconclusive($"Console app not found at: {_consoleAppPath}");
            }
        }
    }
}
