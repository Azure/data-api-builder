// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Source-checkout tests of the actual NuGet pack target. Build the solution first;
    /// these tests pack existing outputs without rebuilding or contacting a package feed.
    /// </summary>
    [TestClass]
    [TestCategory("TelemetryPackaging")]
    [DoNotParallelize]
    public class TelemetryPackageContractTests
    {
        [DataTestMethod]
        [DataRow("Version", "2.1", "2.1.0")]
        [DataRow("Version", "2.1.123-review", "2.1.123-review")]
        [DataRow("PackageVersion", "2.1.456-review.7", "2.1.456-review.7")]
        public async Task McpPackageRequiresExactlyTheMatchingCoreRelease(string property, string version, string normalizedVersion)
        {
            string root = RepositoryRoot();
            string project = Path.Combine(root, "src", "Azure.DataApiBuilder.Mcp", "Azure.DataApiBuilder.Mcp.csproj");
            Assert.IsTrue(File.Exists(project), "Packaging tests require the source checkout used to build this test assembly.");
            string output = Path.Combine(Path.GetTempPath(), "dab-telemetry-pack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            try
            {
                ProcessStartInfo start = CreatePackStartInfo(root, project, output, property, version);
                using Process process = Process.Start(start)!;
                try
                {
                    await WaitForPackExitAsync(process.WaitForExitAsync(), process.StandardOutput.BaseStream,
                        process.StandardError.BaseStream, TimeSpan.FromMinutes(2));
                    // Never include inherited environment/feed diagnostics in test output.
                    Assert.AreEqual(0, process.ExitCode, "The existing MCP build must pack successfully (child output withheld).");
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    }
                }

                string package = Directory.GetFiles(output, "Microsoft.DataApiBuilder.Mcp.*.nupkg").Single();
                using ZipArchive archive = ZipFile.OpenRead(package);
                using Stream manifestStream = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
                XDocument manifest = XDocument.Load(manifestStream);
                XNamespace ns = manifest.Root!.Name.Namespace;
                Assert.AreEqual(normalizedVersion, manifest.Descendants(ns + "metadata").Single().Element(ns + "version")!.Value);
                XElement dependency = manifest.Descendants(ns + "dependency")
                    .Single(element => (string?)element.Attribute("id") == "Microsoft.DataApiBuilder.Core");
                Assert.AreEqual("[" + normalizedVersion + "]", (string?)dependency.Attribute("version"),
                    "A minimum version silently permits mismatched internal Core/MCP contracts.");
                Assert.IsTrue(archive.Entries.Any(entry => entry.FullName.EndsWith("/Microsoft.DataApiBuilder.Mcp.dll", StringComparison.Ordinal)));
                Assert.IsFalse(archive.Entries.Any(entry => entry.FullName.EndsWith("/Microsoft.DataApiBuilder.Core.dll", StringComparison.Ordinal)),
                    "Core remains a versioned package dependency, not a duplicate bundled assembly.");
            }
            finally
            {
                Directory.Delete(output, recursive: true);
            }
        }

        [TestMethod]
        public void PackInvocationIsIsolatedFromBuildServersAndWorkloadDownloads()
        {
            ProcessStartInfo start = CreatePackStartInfo("root", "project", "output", "Version", "2.1");
            Assert.IsTrue(start.ArgumentList.Contains("--no-build"));
            Assert.IsTrue(start.ArgumentList.Contains("--no-restore"));
            Assert.IsTrue(start.ArgumentList.Contains("--disable-build-servers"));
            Assert.IsTrue(start.ArgumentList.Contains("-maxcpucount:1"));
            Assert.AreEqual("true", start.Environment["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"]);
            Assert.AreEqual("1", start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"]);
            Assert.IsTrue(start.RedirectStandardOutput && start.RedirectStandardError);
            Assert.IsFalse(start.UseShellExecute);
        }

        [TestMethod]
        public async Task PackExitDoesNotWaitForInheritedOutputHandles()
        {
            TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationGatedStream stdout = new();
            using CancellationGatedStream stderr = new();
            Task completion = WaitForPackExitAsync(exited.Task, stdout, stderr, TimeSpan.FromSeconds(10));
            Assert.IsFalse(completion.IsCompleted);
            Assert.IsTrue(stdout.DrainStarted && stderr.DrainStarted);

            exited.SetResult();
            await completion.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.IsTrue(stdout.DrainCanceled && stderr.DrainCanceled,
                "Pack completion must cancel reads whose EOF depends on a surviving descendant.");
        }

        [TestMethod]
        public async Task ClosedOutputDoesNotCompleteARunningPackProcess()
        {
            TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using MemoryStream stdout = new();
            using MemoryStream stderr = new();
            Task completion = WaitForPackExitAsync(exited.Task, stdout, stderr, TimeSpan.FromSeconds(10));
            Assert.IsFalse(completion.IsCompleted, "Output EOF is not proof of process completion.");

            exited.SetResult();
            await completion.WaitAsync(TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        public async Task PackTimeoutCancelsReadsAndReportsOnlyTaskState()
        {
            TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationGatedStream stdout = new();
            using CancellationGatedStream stderr = new();
            TimeoutException exception = await Assert.ThrowsExceptionAsync<TimeoutException>(() =>
                WaitForPackExitAsync(exited.Task, stdout, stderr, TimeSpan.Zero));

            Assert.AreEqual("MCP pack timed out. ProcessExitCompleted=False; StdoutCompleted=False; StderrCompleted=False. Child output withheld.", exception.Message);
            Assert.IsTrue(stdout.DrainCanceled && stderr.DrainCanceled);
            Assert.IsFalse(exited.Task.IsCompleted, "The caller still owns termination of a timed-out process.");
        }

        private static ProcessStartInfo CreatePackStartInfo(string root, string project, string output, string property, string version)
        {
            ProcessStartInfo start = new("dotnet")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("pack");
            start.ArgumentList.Add(project);
            start.ArgumentList.Add("--configuration");
#if DEBUG
            start.ArgumentList.Add("Debug");
#else
            start.ArgumentList.Add("Release");
#endif
            start.ArgumentList.Add("--no-build");
            start.ArgumentList.Add("--no-restore");
            // This metadata-only pack must not depend on the build/test runner's reusable workers.
            start.ArgumentList.Add("--disable-build-servers");
            start.ArgumentList.Add("-maxcpucount:1");
            start.ArgumentList.Add("--output");
            start.ArgumentList.Add(output);
            start.ArgumentList.Add("-p:" + property + "=" + version);
            start.ArgumentList.Add("--verbosity");
            start.ArgumentList.Add("quiet");
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            // --no-restore does not suppress the CLI's background workload manifest downloads.
            start.Environment["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true";
            return start;
        }

        private static async Task WaitForPackExitAsync(Task exited, Stream stdoutStream, Stream stderrStream, TimeSpan timeout)
        {
            using CancellationTokenSource drainLifetime = new();
            // Discard output without buffering or disclosing inherited feed/environment diagnostics.
            Task stdout = stdoutStream.CopyToAsync(Stream.Null, drainLifetime.Token);
            Task stderr = stderrStream.CopyToAsync(Stream.Null, drainLifetime.Token);
            try
            {
                await exited.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"MCP pack timed out. ProcessExitCompleted={exited.IsCompleted}; StdoutCompleted={stdout.IsCompleted}; StderrCompleted={stderr.IsCompleted}. Child output withheld.");
            }
            finally
            {
                // A descendant can inherit the pipes after dotnet exits. Their EOF is not part of
                // the pack result; only the child exit code and the package assertions decide that.
                drainLifetime.Cancel();
                try
                {
                    await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (OperationCanceledException) when (drainLifetime.IsCancellationRequested)
                {
                }
            }
        }

        private sealed class CancellationGatedStream : MemoryStream
        {
            public bool DrainStarted { get; private set; }
            public bool DrainCanceled { get; private set; }

            public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            {
                DrainStarted = true;
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    DrainCanceled = cancellationToken.IsCancellationRequested;
                }
            }
        }

        private static string RepositoryRoot([CallerFilePath] string sourceFile = "")
            => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", ".."));
    }
}
