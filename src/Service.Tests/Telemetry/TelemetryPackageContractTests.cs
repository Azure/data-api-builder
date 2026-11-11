// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Opt-in source-checkout tests of the actual NuGet pack target. Build the solution first;
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
                start.ArgumentList.Add("--output");
                start.ArgumentList.Add(output);
                start.ArgumentList.Add("-p:" + property + "=" + version);
                start.ArgumentList.Add("--verbosity");
                start.ArgumentList.Add("quiet");
                start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

                using Process process = Process.Start(start)!;
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                try
                {
                    await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr).WaitAsync(TimeSpan.FromMinutes(2));
                    // Never include inherited environment/feed diagnostics in test output.
                    Assert.AreEqual(0, process.ExitCode, "The existing MCP build must pack successfully.");
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
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

        private static string RepositoryRoot([CallerFilePath] string sourceFile = "")
            => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", ".."));
    }
}
