using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Tooling
{
    [TestClass]
    public class MissionPackScriptTests
    {
        [TestMethod]
        public void PreviewValidatesAndDiffsTheExplicitDatabaseWithoutPublishing()
        {
            using var fixture = new ScriptFixture();

            var result = fixture.Run("-WorldDatabasePath", Path.GetFileName(fixture.Database));

            Assert.AreEqual(0, result.ExitCode, result.Output);
            CollectionAssert.AreEqual(new[] { "build", "validate", "diff" }, fixture.Operations());
            foreach (var call in fixture.Calls().Skip(1))
            {
                Assert.AreEqual(fixture.Database[..^3], ValueAfter(call, "--database"));
                Assert.AreEqual(fixture.DefaultPacks, ValueAfter(call, "--directory"));
                CollectionAssert.DoesNotContain(call, "--initialize-empty");
            }
            StringAssert.Contains(result.Output, fixture.Database);
            StringAssert.Contains(result.Output, "script-test-release");
            StringAssert.Contains(result.Output, "Preview");
        }

        [TestMethod]
        [DynamicData(nameof(AvailablePowerShellHosts))]
        public void ExplicitPublicationWorksWithEachAvailablePowerShellHost(string host)
        {
            using var fixture = new ScriptFixture(host);

            var result = fixture.Run("-WorldDatabasePath", fixture.Database, "-Publish");

            Assert.AreEqual(0, result.ExitCode, result.Output);
            CollectionAssert.AreEqual(new[] { "build", "validate", "diff", "publish" }, fixture.Operations());
            StringAssert.Contains(result.Output, "Publication completed");
            Assert.AreEqual("Test target; the shim never opens this file.", File.ReadAllText(fixture.Database));
        }

        [TestMethod]
        [DynamicData(nameof(AvailablePowerShellHosts))]
        public void ValidationFailureStopsPublicationOnEachAvailablePowerShellHost(string host)
        {
            using var fixture = new ScriptFixture(host) { FailAtOperation = "validate" };

            var result = fixture.Run("-WorldDatabasePath", fixture.Database, "-Publish");

            Assert.AreEqual(42, result.ExitCode, result.Output);
            CollectionAssert.AreEqual(new[] { "build", "validate" }, fixture.Operations());
        }

        [TestMethod]
        public void ExplicitPackDirectoryIsResolvedFromTheCallerNotTheRepository()
        {
            using var fixture = new ScriptFixture();
            var directory = Path.Combine(Path.GetDirectoryName(fixture.Database)!, "release [candidate]");
            ScriptFixture.WritePacks(directory);

            var result = fixture.Run("-WorldDatabasePath", fixture.Database,
                "-PackDirectory", Path.GetFileName(directory));

            Assert.AreEqual(0, result.ExitCode, result.Output);
            foreach (var call in fixture.Calls().Skip(1))
                Assert.AreEqual(directory, ValueAfter(call, "--directory"));
        }

        [TestMethod]
        [DataRow("build", 1)]
        [DataRow("validate", 2)]
        [DataRow("diff", 3)]
        [DataRow("publish", 4)]
        public void NativeFailurePreservesExitCodeAndStopsLaterStages(string stage, int calls)
        {
            using var fixture = new ScriptFixture { FailAtOperation = stage };

            var result = fixture.Run("-WorldDatabasePath", fixture.Database, "-Publish");

            Assert.AreEqual(42, result.ExitCode, result.Output);
            CollectionAssert.AreEqual(new[] { "build", "validate", "diff", "publish" }.Take(calls).ToArray(),
                fixture.Operations());
            StringAssert.Contains(result.Output, $"Injected {stage} failure");
        }

        [TestMethod]
        public void MissingExplicitDatabaseFailsWithoutPromptingOrInvokingDotnet()
        {
            using var fixture = new ScriptFixture();

            var result = fixture.Run();

            Assert.AreEqual(1, result.ExitCode, result.Output);
            Assert.AreEqual(0, fixture.Calls().Length);
            StringAssert.Contains(result.Output, "Specify -WorldDatabasePath");
        }

        [TestMethod]
        [DataRow("missing")]
        [DataRow("directory")]
        [DataRow("extension")]
        public void InvalidDatabaseIsRejectedBeforeBuildWithoutCreatingFiles(string invalid)
        {
            using var fixture = new ScriptFixture();
            var path = invalid switch
            {
                "directory" => fixture.DefaultPacks,
                "extension" => Path.ChangeExtension(fixture.Database, ".sqlite"),
                _ => Path.Combine(Path.GetDirectoryName(fixture.Database)!, "does-not-exist.db")
            };
            if (invalid == "extension")
                File.WriteAllText(path, "Not an accepted CLI target.");

            var result = fixture.Run("-WorldDatabasePath", path, "-Publish");

            Assert.AreEqual(1, result.ExitCode, result.Output);
            Assert.AreEqual(0, fixture.Calls().Length);
            if (invalid == "missing")
                Assert.IsFalse(File.Exists(path));
        }

        [TestMethod]
        [DataRow("manifest")]
        [DataRow("packs")]
        public void IncompletePackDirectoryIsRejectedBeforeBuild(string missing)
        {
            using var fixture = new ScriptFixture();
            File.Delete(Path.Combine(fixture.DefaultPacks,
                missing == "manifest" ? "client-bindings.json" : "mission.json"));

            var result = fixture.Run("-WorldDatabasePath", fixture.Database, "-Publish");

            Assert.AreEqual(1, result.ExitCode, result.Output);
            Assert.AreEqual(0, fixture.Calls().Length);
        }

        public static IEnumerable<object[]> AvailablePowerShellHosts()
        {
            var hosts = ScriptFixture.Hosts().ToArray();
            return hosts.Length == 0 ? new[] { new object[] { "" } } :
                hosts.Select(host => new object[] { host });
        }

        private static string ValueAfter(string[] arguments, string name)
        {
            var index = Array.IndexOf(arguments, name);
            Assert.IsTrue(index >= 0 && index + 1 < arguments.Length, $"Missing argument {name}.");
            return arguments[index + 1];
        }

        private sealed record ScriptResult(int ExitCode, string Output);

        private sealed class ScriptFixture : IDisposable
        {
            private readonly string _directory;
            private readonly string _caller;
            private readonly string _bin;
            private readonly string _script;
            private readonly string _log;
            private readonly string _host;

            internal string Database { get; }
            internal string DefaultPacks { get; }
            internal string FailAtOperation { get; set; }

            internal ScriptFixture(string host = null)
            {
                var root = new DirectoryInfo(AppContext.BaseDirectory);
                while (root != null && !File.Exists(Path.Combine(root.FullName, "Rasa.NET.sln")))
                    root = root.Parent;
                Assert.IsNotNull(root, "Repository root not found.");
                var source = Path.Combine(root.FullName, "scripts", "Update-MissionPacks.ps1");
                Assert.IsTrue(File.Exists(source), "The approved mission publication wrapper is missing.");
                _host = host ?? Hosts().FirstOrDefault();
                if (string.IsNullOrEmpty(_host))
                    Assert.Inconclusive("PowerShell is required for the wrapper integration tests.");
                _directory = Path.Combine(Path.GetTempPath(), "rasa-mission-script-" + Guid.NewGuid().ToString("N"));
                var repository = Path.Combine(_directory, "repository with spaces");
                _caller = Path.Combine(_directory, "unrelated caller");
                _bin = Path.Combine(_directory, "shim");
                _script = Path.Combine(repository, "scripts", "Update-MissionPacks.ps1");
                _log = Path.Combine(_directory, "calls.jsonl");
                Directory.CreateDirectory(Path.GetDirectoryName(_script)!);
                Directory.CreateDirectory(_caller);
                Directory.CreateDirectory(_bin);
                File.Copy(source, _script);
                var project = Path.Combine(repository, "src", "Rasa.MissionTool", "Rasa.MissionTool.csproj");
                Directory.CreateDirectory(Path.GetDirectoryName(project)!);
                File.WriteAllText(project, "<Project />");
                DefaultPacks = Path.Combine(repository, "content", "missions", "bootcamp");
                WritePacks(DefaultPacks);
                Database = Path.Combine(_caller, "world [test].db");
                File.WriteAllText(Database, "Test target; the shim never opens this file.");

                var shim = Path.Combine(_bin, "dotnet-shim.ps1");
                File.WriteAllText(shim, """
                    $values = @($args | ForEach-Object { [string]$_ })
                    [IO.File]::AppendAllText($env:RASA_SCRIPT_TEST_LOG,
                        (ConvertTo-Json -Compress -InputObject $values) + [Environment]::NewLine)
                    $operation = $values[0]
                    if ($operation -eq 'run') {
                        $separator = [Array]::IndexOf($values, '--')
                        if ($separator -lt 0) { exit 98 }
                        $operation = $values[$separator + 1]
                    }
                    if ($env:RASA_SCRIPT_TEST_FAIL -eq $operation) {
                        [Console]::Error.WriteLine("Injected $operation failure")
                        exit 42
                    }
                    Write-Output "Stub $operation succeeded"
                    exit 0
                    """);
                if (OperatingSystem.IsWindows())
                    File.WriteAllText(Path.Combine(_bin, "dotnet.cmd"),
                        $"@echo off\r\n\"{_host}\" -NoProfile -NonInteractive -File \"{shim}\" %*\r\nexit /b %errorlevel%\r\n");
                else
                {
                    var executable = Path.Combine(_bin, "dotnet");
                    File.WriteAllText(executable,
                        $"#!/bin/sh\nexec {Quote(_host)} -NoProfile -NonInteractive -File {Quote(shim)} \"$@\"\n");
                    File.SetUnixFileMode(executable,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }

            internal static void WritePacks(string directory)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "client-bindings.json"), "{}");
                File.WriteAllText(Path.Combine(directory, "mission.json"), "{\"release\":\"script-test-release\"}");
            }

            internal ScriptResult Run(params string[] arguments)
            {
                var start = new ProcessStartInfo(_host)
                {
                    WorkingDirectory = _caller,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", _script }.Concat(arguments))
                    start.ArgumentList.Add(argument);
                start.Environment["PATH"] = _bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
                start.Environment["RASA_SCRIPT_TEST_LOG"] = _log;
                start.Environment["RASA_SCRIPT_TEST_FAIL"] = FailAtOperation ?? "";
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60_000))
                {
                    process.Kill(entireProcessTree: true);
                    Assert.Fail("The noninteractive wrapper did not finish.");
                }
                Task.WaitAll(output, error);
                return new ScriptResult(process.ExitCode, output.Result + error.Result);
            }

            internal string[][] Calls() => File.Exists(_log)
                ? File.ReadAllLines(_log).Select(line => JsonSerializer.Deserialize<string[]>(line)!).ToArray()
                : Array.Empty<string[]>();

            internal string[] Operations() => Calls().Select(call =>
                call[0] == "build" ? "build" : call[Array.IndexOf(call, "--") + 1]).ToArray();

            internal static IEnumerable<string> Hosts()
            {
                foreach (var name in OperatingSystem.IsWindows() ? new[] { "pwsh.exe", "powershell.exe" } : new[] { "pwsh" })
                {
                    foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                        if (!string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory.Trim('"'), name)))
                        {
                            yield return Path.Combine(directory.Trim('"'), name);
                            break;
                        }
                }
            }

            private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

            public void Dispose() => Directory.Delete(_directory, recursive: true);
        }
    }
}
