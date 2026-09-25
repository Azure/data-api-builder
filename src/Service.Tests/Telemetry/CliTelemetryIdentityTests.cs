// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Synthetic local fixtures only: no actual user profile, environment mutation, telemetry
    /// sender, first-run emission or database. The two identity types must remain independent.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    [TestCategory("CliTelemetry")]
    public class CliTelemetryIdentityTests
    {
        private const string SYNTHETIC_ID = "e4bc3f2c-5782-4adf-8a11-72f2418c7917";
        private const string INSTALLATION_STATE = "{\"version\":1,\"installationId\":\"" + SYNTHETIC_ID + "\"}";
        private const string API_STATE = "{\"version\":1,\"apiId\":\"" + SYNTHETIC_ID + "\"}";
        private const UnixFileMode PRIVATE_DIRECTORY_MODE = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        private const UnixFileMode PRIVATE_FILE_MODE = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        [TestMethod]
        public void FirstResolutionCreatesOnlyPrivateVersionAndRandomInstallationIdUnderProfileRoot()
        {
            using TemporaryProfile files = new();
            byte[] configBefore = File.ReadAllBytes(files.ConfigPath);

            CliTelemetryInstallation installation = ResolveEnabled(files.ProfilePath);

            Guid id = AssertAvailable(installation, "newly_saved");
            byte[] bytes = File.ReadAllBytes(files.StatePath);
            Assert.IsTrue(bytes.Length <= 1024);
            using JsonDocument document = JsonDocument.Parse(bytes);
            CollectionAssert.AreEquivalent(new[] { "version", "installationId" },
                document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.AreEqual(1, document.RootElement.GetProperty("version").GetInt32());
            Assert.AreEqual(id, document.RootElement.GetProperty("installationId").GetGuid());
            CollectionAssert.AreEqual(configBefore, File.ReadAllBytes(files.ConfigPath));
            Assert.IsFalse(File.Exists(files.SidecarPath), "An installation is not an API identity.");
            CollectionAssert.AreEquivalent(new[] { files.DabPath, files.TelemetryPath, files.StatePath },
                Directory.GetFileSystemEntries(files.ProfilePath, "*", SearchOption.AllDirectories));

            if (OperatingSystem.IsWindows())
            {
                AssertPrivateWindowsObject(files.DabPath, isDirectory: true);
                AssertPrivateWindowsObject(files.TelemetryPath, isDirectory: true);
                AssertPrivateWindowsObject(files.StatePath, isDirectory: false);
            }
            else
            {
                Assert.AreEqual(PRIVATE_DIRECTORY_MODE, File.GetUnixFileMode(files.DabPath));
                Assert.AreEqual(PRIVATE_DIRECTORY_MODE, File.GetUnixFileMode(files.TelemetryPath));
                Assert.AreEqual(PRIVATE_FILE_MODE, File.GetUnixFileMode(files.StatePath));
            }
        }

        [TestMethod]
        public void SubsequentResolutionReusesImmutableStateWithoutDependingOnConfiguration()
        {
            using TemporaryProfile files = new();
            Guid first = AssertAvailable(ResolveEnabled(files.ProfilePath), "newly_saved");
            byte[] before = File.ReadAllBytes(files.StatePath);
            DateTime timestamp = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(files.StatePath, timestamp);
            File.WriteAllText(files.ConfigPath, "changed synthetic config, deliberately not JSON");

            Assert.AreEqual(first, AssertAvailable(ResolveEnabled(files.ProfilePath), "reused"));
            File.Delete(files.ConfigPath);
            Assert.AreEqual(first, AssertAvailable(ResolveEnabled(files.ProfilePath), "reused"));

            CollectionAssert.AreEqual(before, File.ReadAllBytes(files.StatePath));
            Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(files.StatePath));
            CollectionAssert.AreEquivalent(new[] { files.StatePath }, Directory.GetFiles(files.TelemetryPath));
        }

        [TestMethod]
        public void IndependentProfilesAndApiIdentitiesRemainIndependentAcrossBothResets()
        {
            using TemporaryProfile first = new();
            using TemporaryProfile second = new();
            Guid installation = AssertAvailable(ResolveEnabled(first.ProfilePath), "newly_saved");
            Guid otherInstallation = AssertAvailable(ResolveEnabled(second.ProfilePath), "newly_saved");
            EngineTelemetryIdentity api = EngineTelemetryIdentityStore.Resolve(first.ConfigPath, static _ => null);
            EngineTelemetryIdentity otherApi = EngineTelemetryIdentityStore.Resolve(second.ConfigPath, static _ => null);
            Assert.AreEqual("newly_saved", api.Stability);
            Assert.AreEqual("newly_saved", otherApi.Stability);
            Assert.AreEqual(4, new[] { installation, otherInstallation, api.ApiId, otherApi.ApiId }.Distinct().Count());
            byte[] apiBefore = File.ReadAllBytes(first.SidecarPath);

            File.Delete(first.StatePath);
            Guid resetInstallation = AssertAvailable(ResolveEnabled(first.ProfilePath), "newly_saved");

            Assert.AreNotEqual(installation, resetInstallation);
            Assert.AreNotEqual(api.ApiId, resetInstallation);
            CollectionAssert.AreEqual(apiBefore, File.ReadAllBytes(first.SidecarPath));
            Assert.AreEqual(api.ApiId, LookupEnabled(first.ConfigPath)?.ApiId);
            using (JsonDocument state = JsonDocument.Parse(File.ReadAllBytes(first.StatePath)))
            {
                Assert.AreEqual(2, state.RootElement.EnumerateObject().Count(), "Reset must not save old/new linkage.");
                Assert.AreEqual(resetInstallation, state.RootElement.GetProperty("installationId").GetGuid());
            }

            byte[] installationBefore = File.ReadAllBytes(first.StatePath);
            File.Delete(first.SidecarPath);
            Assert.IsNull(LookupEnabled(first.ConfigPath));
            EngineTelemetryIdentity resetApi = EngineTelemetryIdentityStore.Resolve(first.ConfigPath, static _ => null);
            Assert.AreEqual("newly_saved", resetApi.Stability);
            Assert.AreNotEqual(api.ApiId, resetApi.ApiId);
            CollectionAssert.AreEqual(installationBefore, File.ReadAllBytes(first.StatePath));
            Assert.AreEqual(resetInstallation, AssertAvailable(ResolveEnabled(first.ProfilePath), "reused"));
            Assert.AreEqual(otherInstallation, AssertAvailable(ResolveEnabled(second.ProfilePath), "reused"));
            Assert.AreEqual(otherApi.ApiId, LookupEnabled(second.ConfigPath)?.ApiId);
        }

        [TestMethod]
        public void LookupWithoutSidecarNeverCreatesAnIdentityOrDirectories()
        {
            using TemporaryProfile files = new();
            string missingConfig = Path.Combine(files.RootPath, "absent.json");
            string missingDirectory = Path.Combine(files.RootPath, "absent-directory");

            Assert.IsNull(LookupEnabled(files.ConfigPath));
            Assert.IsNull(LookupEnabled(files.ConfigPath));
            Assert.IsNull(LookupEnabled(missingConfig));
            Assert.IsNull(LookupEnabled(Path.GetRelativePath(Environment.CurrentDirectory, missingConfig)));
            Assert.IsNull(LookupEnabled(Path.Combine(missingDirectory, "root.json")));

            Assert.IsFalse(Directory.Exists(missingDirectory));
            Assert.IsFalse(Directory.Exists(files.DabPath));
            CollectionAssert.AreEquivalent(new[] { files.ConfigPath }, Directory.GetFiles(files.RootPath));
        }

        [TestMethod]
        public void LookupReadsOnlyTheExactExistingSafeSidecarWithoutOpeningConfigOrRewritingState()
        {
            using TemporaryProfile files = new();
            EngineTelemetryIdentity saved = EngineTelemetryIdentityStore.Resolve(files.ConfigPath, static _ => null);
            Assert.AreEqual("newly_saved", saved.Stability);
            byte[] before = File.ReadAllBytes(files.SidecarPath);
            DateTime timestamp = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(files.SidecarPath, timestamp);
            string otherRoot = Path.Combine(files.RootPath, "other.json");
            File.Copy(files.ConfigPath, otherRoot);

            using (FileStream held = new(files.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                EngineTelemetryIdentity? found = LookupEnabled(files.ConfigPath);
                Assert.IsNotNull(found);
                Assert.AreEqual("reused", found.Stability);
                Assert.AreEqual(saved.ApiId, found.ApiId);
                Assert.AreEqual(saved.ApiId, LookupEnabled(Path.GetRelativePath(Environment.CurrentDirectory, files.ConfigPath))?.ApiId);
            }

            Assert.IsNull(LookupEnabled(otherRoot), "Lookup must not search other roots or adopt a nearby sidecar.");
            Assert.IsFalse(File.Exists(otherRoot + ".dab-telemetry.json"));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(files.SidecarPath));
            Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(files.SidecarPath));
            Assert.IsFalse(Directory.Exists(files.DabPath));
        }

        [TestMethod]
        public void LookupDoesNotAdoptStateWithoutItsRootConfiguration()
        {
            using TemporaryProfile files = new();
            TemporaryProfile.WritePrivateFile(files.SidecarPath, Encoding.UTF8.GetBytes(API_STATE));
            File.Delete(files.ConfigPath);

            Assert.IsNull(LookupEnabled(files.ConfigPath));

            Assert.AreEqual(API_STATE, File.ReadAllText(files.SidecarPath));
            CollectionAssert.AreEquivalent(new[] { files.SidecarPath }, Directory.GetFiles(files.RootPath));
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("{")]
        [DataRow("[]")]
        [DataRow("null")]
        [DataRow("{}")]
        [DataRow("{\"$property\":\"$id\"}")]
        [DataRow("{\"version\":1}")]
        [DataRow("{\"version\":0,\"$property\":\"$id\"}")]
        [DataRow("{\"version\":2,\"$property\":\"$id\"}")]
        [DataRow("{\"version\":1.5,\"$property\":\"$id\"}")]
        [DataRow("{\"version\":\"1\",\"$property\":\"$id\"}")]
        [DataRow("{\"version\":true,\"$property\":\"$id\"}")]
        [DataRow("{\"version\":null,\"$property\":\"$id\"}")]
        [DataRow("{\"version\":1,\"$property\":null}")]
        [DataRow("{\"version\":1,\"$property\":1}")]
        [DataRow("{\"version\":1,\"$property\":[]}")]
        [DataRow("{\"version\":1,\"$property\":{}}")]
        [DataRow("{\"version\":1,\"$property\":\"not-a-guid\"}")]
        [DataRow("{\"version\":1,\"$property\":\"00000000-0000-0000-0000-000000000000\"}")]
        [DataRow("{\"version\":1,\"$property\":\"e4bc3f2c-5782-1adf-8a11-72f2418c7917\"}")]
        [DataRow("{\"version\":1,\"$property\":\"e4bc3f2c-5782-5adf-8a11-72f2418c7917\"}")]
        [DataRow("{\"version\":1,\"$property\":\"e4bc3f2c-5782-4adf-0a11-72f2418c7917\"}")]
        [DataRow("{\"version\":1,\"$property\":\" $id\"}")]
        [DataRow("{\"version\":1,\"$property\":\"{$id}\"}")]
        [DataRow("{\"version\":1,\"version\":1,\"$property\":\"$id\"}")]
        [DataRow("{\"version\":1,\"$property\":\"$id\",\"$property\":\"$id\"}")]
        [DataRow("{\"Version\":1,\"$property\":\"$id\"}")]
        [DataRow("{\"version\":1,\"$property\":\"$id\",\"noticeShown\":true}")]
        [DataRow("{\"version\":1,\"$property\":\"$id\",\"previousId\":\"$id\"}")]
        [DataRow("{\"version\":1,\"$property\":\"$id\",}")]
        [DataRow("/*comment*/{\"version\":1,\"$property\":\"$id\"}")]
        public void CorruptOrFutureInstallationAndLookupStateIsNeverRepairedOrOverwritten(string template)
        {
            using TemporaryProfile files = new();
            byte[] installation = Encoding.UTF8.GetBytes(template.Replace("$property", "installationId", StringComparison.Ordinal)
                .Replace("$id", SYNTHETIC_ID, StringComparison.Ordinal));
            byte[] api = Encoding.UTF8.GetBytes(template.Replace("$property", "apiId", StringComparison.Ordinal)
                .Replace("$id", SYNTHETIC_ID, StringComparison.Ordinal));
            files.WriteState(installation);
            TemporaryProfile.WritePrivateFile(files.SidecarPath, api);

            AssertUnavailable(ResolveEnabled(files.ProfilePath));
            AssertUnavailable(ResolveEnabled(files.ProfilePath));
            Assert.IsNull(LookupEnabled(files.ConfigPath));

            CollectionAssert.AreEqual(installation, File.ReadAllBytes(files.StatePath));
            CollectionAssert.AreEqual(api, File.ReadAllBytes(files.SidecarPath));
            CollectionAssert.AreEquivalent(new[] { files.StatePath }, Directory.GetFiles(files.TelemetryPath));
            CollectionAssert.AreEquivalent(new[] { files.ConfigPath, files.SidecarPath }, Directory.GetFiles(files.RootPath));
        }

        [TestMethod]
        public void TheTwoStateSchemasCannotBeSubstitutedForOneAnother()
        {
            using TemporaryProfile files = new();
            files.WriteState(Encoding.UTF8.GetBytes(API_STATE));
            TemporaryProfile.WritePrivateFile(files.SidecarPath, Encoding.UTF8.GetBytes(INSTALLATION_STATE));

            AssertUnavailable(ResolveEnabled(files.ProfilePath));
            Assert.IsNull(LookupEnabled(files.ConfigPath));

            Assert.AreEqual(API_STATE, File.ReadAllText(files.StatePath));
            Assert.AreEqual(INSTALLATION_STATE, File.ReadAllText(files.SidecarPath));
        }

        [DataTestMethod]
        [DataRow(1024, true)]
        [DataRow(1025, false)]
        [DataRow(4096, false)]
        public void BothReadsBoundActualStateBytesWithoutTruncation(int length, bool valid)
        {
            using TemporaryProfile files = new();
            byte[] installation = Encoding.UTF8.GetBytes(INSTALLATION_STATE.PadRight(length));
            byte[] api = Encoding.UTF8.GetBytes(API_STATE.PadRight(length));
            files.WriteState(installation);
            TemporaryProfile.WritePrivateFile(files.SidecarPath, api);

            CliTelemetryInstallation saved = ResolveEnabled(files.ProfilePath);
            EngineTelemetryIdentity? found = LookupEnabled(files.ConfigPath);
            if (valid)
            {
                Assert.AreEqual(Guid.Parse(SYNTHETIC_ID), AssertAvailable(saved, "reused"));
                Assert.IsNotNull(found);
                Assert.AreEqual("reused", found.Stability);
                Assert.AreEqual(Guid.Parse(SYNTHETIC_ID), found.ApiId);
            }
            else
            {
                AssertUnavailable(saved);
                Assert.IsNull(found);
            }

            CollectionAssert.AreEqual(installation, File.ReadAllBytes(files.StatePath));
            CollectionAssert.AreEqual(api, File.ReadAllBytes(files.SidecarPath));
        }

        [TestMethod]
        public void MalformedUtf8StateIsLeftUntouchedByBothReads()
        {
            using TemporaryProfile files = new();
            byte[] malformed = { 0xff, 0xfe, 0x7b, 0x7d };
            files.WriteState(malformed);
            TemporaryProfile.WritePrivateFile(files.SidecarPath, malformed);

            AssertUnavailable(ResolveEnabled(files.ProfilePath));
            Assert.IsNull(LookupEnabled(files.ConfigPath));

            CollectionAssert.AreEqual(malformed, File.ReadAllBytes(files.StatePath));
            CollectionAssert.AreEqual(malformed, File.ReadAllBytes(files.SidecarPath));
        }

        [DataTestMethod]
        [DataRow("1")]
        [DataRow(" true ")]
        [DataRow("TRUE")]
        public void OptOutPreservesSavedStateAndReenablementIsNotAReset(string optOut)
        {
            using TemporaryProfile files = new();
            Guid installation = AssertAvailable(ResolveEnabled(files.ProfilePath), "newly_saved");
            EngineTelemetryIdentity api = EngineTelemetryIdentityStore.Resolve(files.ConfigPath, static _ => null);
            Assert.AreEqual("newly_saved", api.Stability);
            byte[] installationBefore = File.ReadAllBytes(files.StatePath);
            byte[] apiBefore = File.ReadAllBytes(files.SidecarPath);
            DateTime timestamp = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(files.StatePath, timestamp);
            File.SetLastWriteTimeUtc(files.SidecarPath, timestamp);
            int policyReads = 0;
            string? ReadPolicy(string variable)
            {
                Assert.AreEqual(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, variable);
                policyReads++;
                return optOut;
            }

            AssertUnavailable(CliTelemetryInstallationStore.Resolve(ReadPolicy, files.ProfilePath));
            Assert.IsNull(EngineTelemetryIdentityStore.Lookup(files.ConfigPath, ReadPolicy));

            Assert.AreEqual(2, policyReads);
            CollectionAssert.AreEqual(installationBefore, File.ReadAllBytes(files.StatePath));
            CollectionAssert.AreEqual(apiBefore, File.ReadAllBytes(files.SidecarPath));
            Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(files.StatePath));
            Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(files.SidecarPath));
            Assert.AreEqual(installation, AssertAvailable(ResolveEnabled(files.ProfilePath), "reused"));
            Assert.AreEqual(api.ApiId, LookupEnabled(files.ConfigPath)?.ApiId);
        }

        [TestMethod]
        public void OptOutAndPolicyFailurePrecedeProfileDiscoveryAndCannotCreateState()
        {
            using TemporaryProfile files = new();

            AssertUnavailable(CliTelemetryInstallationStore.Resolve(static _ => "1", files.ProfilePath));
            AssertUnavailable(CliTelemetryInstallationStore.Resolve(static _ => "true", "\0"));
            // The only null profile seam in these tests is opted out BEFORE real-profile discovery.
            AssertUnavailable(CliTelemetryInstallationStore.Resolve(static _ => "1", profileDirectory: null));
            AssertUnavailable(CliTelemetryInstallationStore.Resolve(
                static _ => throw new InvalidOperationException("Synthetic policy failure."), files.ProfilePath));
            Assert.IsNull(EngineTelemetryIdentityStore.Lookup(files.ConfigPath, static _ => "1"));
            Assert.IsNull(EngineTelemetryIdentityStore.Lookup("\0", static _ => "true"));
            Assert.IsNull(EngineTelemetryIdentityStore.Lookup(files.ConfigPath,
                static _ => throw new InvalidOperationException("Synthetic policy failure.")));

            Assert.IsFalse(Directory.Exists(files.DabPath));
            CollectionAssert.AreEquivalent(new[] { files.ConfigPath }, Directory.GetFiles(files.RootPath));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("0")]
        [DataRow("false")]
        [DataRow("yes")]
        public void NonOptOutValuesPermitStorageForAnAlreadyEnabledCaller(string? value)
        {
            using TemporaryProfile files = new();

            AssertAvailable(CliTelemetryInstallationStore.Resolve(_ => value, files.ProfilePath), "newly_saved");
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow(" \t ")]
        [DataRow("\0")]
        [DataRow("relative-profile")]
        [DataRow(".")]
        [DataRow("..")]
        [DataRow(@"C:relative-profile")]
        [DataRow("https://synthetic.invalid/profile")]
        [DataRow(@"\\synthetic.invalid\share\profile")]
        [DataRow("//synthetic.invalid/share/profile")]
        [DataRow(@"\\?\C:\synthetic\profile")]
        public void EmptyAmbiguousOrNonLocalProfilesAreUnavailableWithoutFallback(string path)
        {
            AssertUnavailable(ResolveEnabled(path));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("\0")]
        [DataRow("https://synthetic.invalid/config.json")]
        [DataRow(@"\\synthetic.invalid\share\config.json")]
        [DataRow("//synthetic.invalid/share/config.json")]
        public void LookupRejectsMissingOrNonLocalInputWithoutGeneratingAnId(string? path)
        {
            Assert.IsNull(LookupEnabled(path));
        }

        [TestMethod]
        public void MissingFileOrTraversalProfileCannotCreateDirectories()
        {
            using TemporaryProfile files = new();
            string missing = Path.Combine(files.RootPath, "missing", "profile");

            AssertUnavailable(ResolveEnabled(missing));
            AssertUnavailable(ResolveEnabled(files.ConfigPath));
            AssertUnavailable(ResolveEnabled(Path.Combine(files.ProfilePath, ".")));
            AssertUnavailable(ResolveEnabled(Path.Combine(files.ProfilePath, "unused", "..")));

            Assert.IsFalse(Directory.Exists(Path.Combine(files.RootPath, "missing")));
            Assert.IsFalse(Directory.Exists(files.DabPath));
            CollectionAssert.AreEquivalent(new[] { files.ConfigPath }, Directory.GetFiles(files.RootPath));
        }

        [DataTestMethod]
        [DataRow("dab")]
        [DataRow("telemetry")]
        public void FileAtAnInstallationDirectoryPathIsNeverReplaced(string target)
        {
            using TemporaryProfile files = new();
            if (target == "telemetry")
            {
                TemporaryProfile.CreatePrivateDirectory(files.DabPath);
            }

            string path = target == "dab" ? files.DabPath : files.TelemetryPath;
            byte[] before = Encoding.UTF8.GetBytes("synthetic directory blocker");
            TemporaryProfile.WritePrivateFile(path, before);

            AssertUnavailable(ResolveEnabled(files.ProfilePath));

            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            Assert.IsFalse(File.Exists(files.StatePath));
        }

        [TestMethod]
        public void DirectoryAtEitherStatePathIsNeverReplaced()
        {
            using TemporaryProfile files = new();
            files.CreateStateDirectories();
            TemporaryProfile.CreatePrivateDirectory(files.StatePath);
            TemporaryProfile.CreatePrivateDirectory(files.SidecarPath);
            string installationSentinel = Path.Combine(files.StatePath, "untouched.txt");
            string apiSentinel = Path.Combine(files.SidecarPath, "untouched.txt");
            File.WriteAllText(installationSentinel, "synthetic installation sentinel");
            File.WriteAllText(apiSentinel, "synthetic API sentinel");

            AssertUnavailable(ResolveEnabled(files.ProfilePath));
            Assert.IsNull(LookupEnabled(files.ConfigPath));

            Assert.AreEqual("synthetic installation sentinel", File.ReadAllText(installationSentinel));
            Assert.AreEqual("synthetic API sentinel", File.ReadAllText(apiSentinel));
        }

        [TestMethod]
        public void ReadOnlyProfileWithoutStateIsNotRepairedOrWritten()
        {
            using TemporaryProfile files = new();
            FileAttributes originalAttributes = File.GetAttributes(files.ProfilePath);
            UnixFileMode? originalMode = OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(files.ProfilePath);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Advisory ReadOnly is not an ACL test; real ACL tests are separate below.
                    File.SetAttributes(files.ProfilePath, originalAttributes | FileAttributes.ReadOnly);
                }
                else
                {
                    File.SetUnixFileMode(files.ProfilePath, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                }

                AssertUnavailable(ResolveEnabled(files.ProfilePath));
                Assert.IsFalse(Directory.Exists(files.DabPath));
                if (!OperatingSystem.IsWindows())
                {
                    Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserExecute, File.GetUnixFileMode(files.ProfilePath));
                }
            }
            finally
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(files.ProfilePath, originalMode!.Value);
                }

                File.SetAttributes(files.ProfilePath, originalAttributes);
            }
        }

        [TestMethod]
        public void ValidReadOnlyInstallationAndDirectoriesAreReusedWithoutRequiringWrites()
        {
            using TemporaryProfile files = new();
            Guid saved = AssertAvailable(ResolveEnabled(files.ProfilePath), "newly_saved");
            string[] paths = { files.ProfilePath, files.DabPath, files.TelemetryPath, files.StatePath };
            FileAttributes[] attributes = paths.Select(File.GetAttributes).ToArray();
            UnixFileMode[]? modes = OperatingSystem.IsWindows() ? null : paths.Select(File.GetUnixFileMode).ToArray();
            byte[] before = File.ReadAllBytes(files.StatePath);
            try
            {
                for (int index = 0; index < paths.Length; index++)
                {
                    if (OperatingSystem.IsWindows())
                    {
                        File.SetAttributes(paths[index], attributes[index] | FileAttributes.ReadOnly);
                    }
                    else
                    {
                        File.SetUnixFileMode(paths[index], index == paths.Length - 1
                            ? UnixFileMode.UserRead : UnixFileMode.UserRead | UnixFileMode.UserExecute);
                    }
                }

                Assert.AreEqual(saved, AssertAvailable(ResolveEnabled(files.ProfilePath), "reused"));
                CollectionAssert.AreEqual(before, File.ReadAllBytes(files.StatePath));
                if (!OperatingSystem.IsWindows())
                {
                    Assert.AreEqual(UnixFileMode.UserRead, File.GetUnixFileMode(files.StatePath));
                    Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserExecute, File.GetUnixFileMode(files.ProfilePath));
                }
            }
            finally
            {
                for (int index = 0; index < paths.Length; index++)
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(paths[index], modes![index]);
                    }

                    File.SetAttributes(paths[index], attributes[index]);
                }
            }
        }

        [DataTestMethod]
        [DataRow("profile", UnixFileMode.GroupRead)]
        [DataRow("profile", UnixFileMode.OtherWrite)]
        [DataRow("dab", UnixFileMode.GroupExecute)]
        [DataRow("dab", UnixFileMode.GroupWrite)]
        [DataRow("telemetry", UnixFileMode.OtherRead)]
        [DataRow("telemetry", UnixFileMode.OtherExecute)]
        [DataRow("telemetry", UnixFileMode.StickyBit)]
        public void NonPrivateLinuxDirectoriesAreUnavailableWithoutChangingModes(string target, UnixFileMode extra)
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Inconclusive("Linux mode validation test.");
                return;
            }

            using TemporaryProfile files = new();
            AssertAvailable(ResolveEnabled(files.ProfilePath), "newly_saved");
            string path = files.GetPath(target);
            UnixFileMode original = File.GetUnixFileMode(path);
            byte[] before = File.ReadAllBytes(files.StatePath);
            try
            {
                File.SetUnixFileMode(path, original | extra);

                AssertUnavailable(ResolveEnabled(files.ProfilePath));

                Assert.AreEqual(original | extra, File.GetUnixFileMode(path));
                CollectionAssert.AreEqual(before, File.ReadAllBytes(files.StatePath));
            }
            finally
            {
                File.SetUnixFileMode(path, original);
            }
        }

        [DataTestMethod]
        [DataRow(UnixFileMode.GroupRead)]
        [DataRow(UnixFileMode.OtherWrite)]
        public void UnsafeLinuxProfileCannotCreateInstallationDirectories(UnixFileMode extra)
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Inconclusive("Linux private-profile creation test.");
                return;
            }

            using TemporaryProfile files = new();
            UnixFileMode original = File.GetUnixFileMode(files.ProfilePath);
            try
            {
                File.SetUnixFileMode(files.ProfilePath, original | extra);

                AssertUnavailable(ResolveEnabled(files.ProfilePath));

                Assert.IsFalse(Directory.Exists(files.DabPath));
                Assert.AreEqual(original | extra, File.GetUnixFileMode(files.ProfilePath));
            }
            finally
            {
                File.SetUnixFileMode(files.ProfilePath, original);
            }
        }

        [DataTestMethod]
        [DataRow(UnixFileMode.GroupRead)]
        [DataRow(UnixFileMode.OtherRead)]
        [DataRow(UnixFileMode.GroupWrite)]
        [DataRow(UnixFileMode.OtherWrite)]
        [DataRow(UnixFileMode.UserExecute)]
        public void NonPrivateLinuxFilesAreRejectedByInstallationAndLookupWithoutRepair(UnixFileMode extra)
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Inconclusive("Linux mode validation test.");
                return;
            }

            using TemporaryProfile files = new();
            files.WriteState(Encoding.UTF8.GetBytes(INSTALLATION_STATE));
            TemporaryProfile.WritePrivateFile(files.SidecarPath, Encoding.UTF8.GetBytes(API_STATE));
            UnixFileMode mode = PRIVATE_FILE_MODE | extra;
            File.SetUnixFileMode(files.StatePath, mode);
            File.SetUnixFileMode(files.SidecarPath, mode);

            AssertUnavailable(ResolveEnabled(files.ProfilePath));
            Assert.IsNull(LookupEnabled(files.ConfigPath));

            Assert.AreEqual(mode, File.GetUnixFileMode(files.StatePath));
            Assert.AreEqual(mode, File.GetUnixFileMode(files.SidecarPath));
            Assert.AreEqual(INSTALLATION_STATE, File.ReadAllText(files.StatePath));
            Assert.AreEqual(API_STATE, File.ReadAllText(files.SidecarPath));
        }

        [DataTestMethod]
        [DataRow("profile")]
        [DataRow("state")]
        public void LinuxForeignOwnerIsRejectedEvenWhenRootCouldAccessIt(string target)
        {
            if (!OperatingSystem.IsLinux() || NativeMethods.GetEffectiveUserId() != 0)
            {
                Assert.Inconclusive("Changing a synthetic fixture's owner requires a privileged Linux test process.");
                return;
            }

            using TemporaryProfile files = new();
            AssertAvailable(ResolveEnabled(files.ProfilePath), "newly_saved");
            string path = files.GetPath(target);
            try
            {
                Assert.AreEqual(0, NativeMethods.ChangeOwner(path, 1, uint.MaxValue));

                AssertUnavailable(ResolveEnabled(files.ProfilePath));
            }
            finally
            {
                Assert.AreEqual(0, NativeMethods.ChangeOwner(path, 0, uint.MaxValue));
            }

            AssertAvailable(ResolveEnabled(files.ProfilePath), "reused");
        }

        [DataTestMethod]
        [DataRow("profile", "Write")]
        [DataRow("profile", "DeleteSubdirectoriesAndFiles")]
        [DataRow("dab", "Delete")]
        [DataRow("dab", "ChangePermissions")]
        [DataRow("telemetry", "Write")]
        [DataRow("telemetry", "TakeOwnership")]
        [DataRow("state", "WriteData")]
        [DataRow("state", "AppendData")]
        [DataRow("state", "Delete")]
        [DataRow("state", "ChangePermissions")]
        [DataRow("state", "TakeOwnership")]
        public void WindowsUntrustedMutationGrantsAreRejectedWithoutChangingAcls(string target, string rightsName)
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Windows owner/DACL validation test.");
                return;
            }

            FileSystemRights rights = Enum.Parse<FileSystemRights>(rightsName);
            using TemporaryProfile files = new();
            AssertAvailable(ResolveEnabled(files.ProfilePath), "newly_saved");
            string path = files.GetPath(target);
            bool isDirectory = target != "state";
            FileSystemSecurity original = ReadWindowsSecurity(path, isDirectory);
            FileSystemSecurity changed = ReadWindowsSecurity(path, isDirectory);
            byte[] before = File.ReadAllBytes(files.StatePath);
            try
            {
                changed.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.WorldSid, null), rights, AccessControlType.Allow));
                WriteWindowsSecurity(path, isDirectory, changed);
                byte[] unsafeAcl = ReadWindowsSecurity(path, isDirectory).GetSecurityDescriptorBinaryForm();

                AssertUnavailable(ResolveEnabled(files.ProfilePath));

                CollectionAssert.AreEqual(unsafeAcl, ReadWindowsSecurity(path, isDirectory).GetSecurityDescriptorBinaryForm());
                CollectionAssert.AreEqual(before, File.ReadAllBytes(files.StatePath));
            }
            finally
            {
                WriteWindowsSecurity(path, isDirectory, original);
            }

            AssertAvailable(ResolveEnabled(files.ProfilePath), "reused");
        }

        [TestMethod]
        public void WindowsUnsafeProfileCannotCreateStateEvenWhenCurrentUserCanWrite()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Windows owner/DACL validation test.");
                return;
            }

            using TemporaryProfile files = new();
            DirectorySecurity original = new DirectoryInfo(files.ProfilePath).GetAccessControl();
            DirectorySecurity changed = new DirectoryInfo(files.ProfilePath).GetAccessControl();
            try
            {
                changed.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
                new DirectoryInfo(files.ProfilePath).SetAccessControl(changed);

                AssertUnavailable(ResolveEnabled(files.ProfilePath));

                Assert.IsFalse(Directory.Exists(files.DabPath));
            }
            finally
            {
                WriteWindowsSecurity(files.ProfilePath, isDirectory: true, original);
            }
        }

        [DataTestMethod]
        [DataRow("profile")]
        [DataRow("dab")]
        [DataRow("telemetry")]
        [DataRow("state")]
        public void WindowsOwnerMustBeCurrentUserEvenIfAnotherTrustedOwnerGrantsFullAccess(string target)
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Windows owner validation test.");
                return;
            }

            using TemporaryProfile files = new();
            AssertAvailable(ResolveEnabled(files.ProfilePath), "newly_saved");
            string path = files.GetPath(target);
            bool isDirectory = target != "state";
            FileSystemSecurity original = ReadWindowsSecurity(path, isDirectory);
            FileSystemSecurity changed = ReadWindowsSecurity(path, isDirectory);
            try
            {
                SecurityIdentifier otherOwner = new(WellKnownSidType.BuiltinAdministratorsSid, null);
                changed.SetOwner(otherOwner);
                try
                {
                    WriteWindowsSecurity(path, isDirectory, changed);
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or PrivilegeNotHeldException
                    or InvalidOperationException)
                {
                    // Win32 ERROR_INVALID_OWNER is mapped by NativeObjectSecurity to
                    // InvalidOperationException. A restricted token cannot assign this SID.
                    // Confirm ownership did not change before treating setup as unavailable.
                    Assert.AreEqual(original.GetOwner(typeof(SecurityIdentifier)),
                        ReadWindowsSecurity(path, isDirectory).GetOwner(typeof(SecurityIdentifier)));
                    Assert.Inconclusive("Assigning another owner requires an appropriately privileged Windows test process.");
                    return;
                }

                Assert.AreEqual(otherOwner, ReadWindowsSecurity(path, isDirectory).GetOwner(typeof(SecurityIdentifier)));
                AssertUnavailable(ResolveEnabled(files.ProfilePath));
                Assert.AreEqual(otherOwner, ReadWindowsSecurity(path, isDirectory).GetOwner(typeof(SecurityIdentifier)));
            }
            finally
            {
                WriteWindowsSecurity(path, isDirectory, original);
            }

            AssertAvailable(ResolveEnabled(files.ProfilePath), "reused");
        }

        [TestMethod]
        public void WindowsNullDaclIsNotTreatedAsPrivateState()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Windows null DACL validation test.");
                return;
            }

            using TemporaryProfile files = new();
            AssertAvailable(ResolveEnabled(files.ProfilePath), "newly_saved");
            FileSecurity original = new FileInfo(files.StatePath).GetAccessControl();
            FileSecurity changed = new();
            RawSecurityDescriptor descriptor = new(ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected,
                (SecurityIdentifier?)original.GetOwner(typeof(SecurityIdentifier)),
                (SecurityIdentifier?)original.GetGroup(typeof(SecurityIdentifier)), null, null);
            byte[] bytes = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(bytes, 0);
            changed.SetSecurityDescriptorBinaryForm(bytes, AccessControlSections.Access);
            try
            {
                new FileInfo(files.StatePath).SetAccessControl(changed);
                RawSecurityDescriptor actual = new(new FileInfo(files.StatePath).GetAccessControl().GetSecurityDescriptorBinaryForm(), 0);
                Assert.IsNull(actual.DiscretionaryAcl, "Control must actually install a null DACL.");

                AssertUnavailable(ResolveEnabled(files.ProfilePath));
            }
            finally
            {
                WriteWindowsSecurity(files.StatePath, isDirectory: false, original);
            }

            AssertAvailable(ResolveEnabled(files.ProfilePath), "reused");
        }

        [TestMethod]
        public void WindowsReadOnlyGrantsToOthersAreNotMistakenForMutationGrants()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Windows DACL validation test.");
                return;
            }

            using TemporaryProfile files = new();
            Guid saved = AssertAvailable(ResolveEnabled(files.ProfilePath), "newly_saved");
            FileSecurity security = new FileInfo(files.StatePath).GetAccessControl();
            security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
            new FileInfo(files.StatePath).SetAccessControl(security);
            byte[] before = new FileInfo(files.StatePath).GetAccessControl().GetSecurityDescriptorBinaryForm();

            Assert.AreEqual(saved, AssertAvailable(ResolveEnabled(files.ProfilePath), "reused"));

            CollectionAssert.AreEqual(before, new FileInfo(files.StatePath).GetAccessControl().GetSecurityDescriptorBinaryForm());
        }

        [TestMethod]
        public void WindowsSharingFailuresDoNotReplaceInstallationOrLookupState()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Windows mandatory file-sharing test.");
                return;
            }

            using TemporaryProfile files = new();
            files.WriteState(Encoding.UTF8.GetBytes(INSTALLATION_STATE));
            TemporaryProfile.WritePrivateFile(files.SidecarPath, Encoding.UTF8.GetBytes(API_STATE));
            using (FileStream heldInstallation = new(files.StatePath, FileMode.Open, FileAccess.Read, FileShare.None))
            using (FileStream heldApi = new(files.SidecarPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                AssertUnavailable(ResolveEnabled(files.ProfilePath));
                Assert.IsNull(LookupEnabled(files.ConfigPath));
            }

            Assert.AreEqual(INSTALLATION_STATE, File.ReadAllText(files.StatePath));
            Assert.AreEqual(API_STATE, File.ReadAllText(files.SidecarPath));
        }

        [TestMethod]
        public void WindowsAlternateStreamsAndTrimmedProfileAliasesAreRejected()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Windows path namespace test.");
                return;
            }

            using TemporaryProfile files = new();

            AssertUnavailable(ResolveEnabled(files.ProfilePath + ":synthetic"));
            AssertUnavailable(ResolveEnabled(files.ProfilePath + "."));
            AssertUnavailable(ResolveEnabled(files.ProfilePath + " "));
            Assert.IsNull(LookupEnabled(files.ConfigPath + ":synthetic"));

            Assert.IsFalse(Directory.Exists(files.DabPath));
            Assert.IsFalse(File.Exists(files.SidecarPath));
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void StateSymlinksIncludingDanglingTargetsAreNeverFollowedOrReplaced(bool targetExists)
        {
            using TemporaryProfile files = new();
            files.CreateStateDirectories();
            string installationTarget = Path.Combine(files.RootPath, "installation-target.json");
            string apiTarget = Path.Combine(files.RootPath, "api-target.json");
            if (targetExists)
            {
                TemporaryProfile.WritePrivateFile(installationTarget, Encoding.UTF8.GetBytes(INSTALLATION_STATE));
                TemporaryProfile.WritePrivateFile(apiTarget, Encoding.UTF8.GetBytes(API_STATE));
            }

            CreateFileLinkOrSkip(files.StatePath, installationTarget);
            CreateFileLinkOrSkip(files.SidecarPath, apiTarget);

            AssertUnavailable(ResolveEnabled(files.ProfilePath));
            Assert.IsNull(LookupEnabled(files.ConfigPath));

            Assert.IsNotNull(new FileInfo(files.StatePath).LinkTarget);
            Assert.IsNotNull(new FileInfo(files.SidecarPath).LinkTarget);
            if (targetExists)
            {
                Assert.AreEqual(INSTALLATION_STATE, File.ReadAllText(installationTarget));
                Assert.AreEqual(API_STATE, File.ReadAllText(apiTarget));
            }
            else
            {
                Assert.IsFalse(File.Exists(installationTarget));
                Assert.IsFalse(File.Exists(apiTarget));
            }

            Assert.AreEqual(0, Directory.GetFiles(files.TelemetryPath, ".dab-telemetry-*.tmp").Length);
        }

        [DataTestMethod]
        [DataRow("profile", true)]
        [DataRow("profile", false)]
        [DataRow("dab", true)]
        [DataRow("dab", false)]
        [DataRow("telemetry", true)]
        [DataRow("telemetry", false)]
        public void SymlinkedProfileOrInstallationDirectoryIsRejected(string target, bool targetExists)
        {
            using TemporaryProfile files = new();
            string realDirectory = Path.Combine(files.RootPath, "real-directory");
            if (targetExists)
            {
                TemporaryProfile.CreatePrivateDirectory(realDirectory);
            }

            string link = files.GetPath(target);
            if (target == "profile")
            {
                Directory.Delete(files.ProfilePath);
            }
            else if (target == "telemetry")
            {
                TemporaryProfile.CreatePrivateDirectory(files.DabPath);
            }

            CreateDirectoryLinkOrSkip(link, realDirectory);
            try
            {
                AssertUnavailable(ResolveEnabled(files.ProfilePath));

                Assert.IsNotNull(new DirectoryInfo(link).LinkTarget);
                if (targetExists)
                {
                    Assert.AreEqual(0, Directory.GetFileSystemEntries(realDirectory).Length);
                }
                else
                {
                    Assert.IsFalse(Directory.Exists(realDirectory));
                }
            }
            finally
            {
                Directory.Delete(link);
            }
        }

        [TestMethod]
        public void SymlinkedAncestorAndConfigAreRejectedByLookupAndInstallation()
        {
            using TemporaryProfile files = new();
            string directoryLink = Path.Combine(files.RootPath, "linked-ancestor");
            string configLink = Path.Combine(files.RootPath, "linked-config.json");
            Guid installation = AssertAvailable(ResolveEnabled(files.ProfilePath), "newly_saved");
            TemporaryProfile.WritePrivateFile(files.SidecarPath, Encoding.UTF8.GetBytes(API_STATE));
            TemporaryProfile.WritePrivateFile(configLink + ".dab-telemetry.json", Encoding.UTF8.GetBytes(API_STATE));
            CreateDirectoryLinkOrSkip(directoryLink, files.RootPath);
            try
            {
                CreateFileLinkOrSkip(configLink, files.ConfigPath);

                AssertUnavailable(ResolveEnabled(Path.Combine(directoryLink, "profile")));
                Assert.IsNull(LookupEnabled(Path.Combine(directoryLink, "root.json")));
                Assert.IsNull(LookupEnabled(configLink));

                Assert.AreEqual(installation, AssertAvailable(ResolveEnabled(files.ProfilePath), "reused"));
                Assert.AreEqual(API_STATE, File.ReadAllText(files.SidecarPath));
                Assert.AreEqual(API_STATE, File.ReadAllText(configLink + ".dab-telemetry.json"));
            }
            finally
            {
                Directory.Delete(directoryLink);
            }
        }

        [TestMethod]
        [Timeout(5000)]
        public void LinuxFifoStateIsRejectedWithoutBlockingOrReplacingIt()
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Inconclusive("Linux special-file/no-follow test.");
                return;
            }

            using TemporaryProfile files = new();
            files.CreateStateDirectories();
            Assert.AreEqual(0, NativeMethods.MakeFifo(files.StatePath, (uint)PRIVATE_FILE_MODE));
            Assert.AreEqual(0, NativeMethods.MakeFifo(files.SidecarPath, (uint)PRIVATE_FILE_MODE));

            AssertUnavailable(ResolveEnabled(files.ProfilePath));
            Assert.IsNull(LookupEnabled(files.ConfigPath));

            Assert.IsTrue(File.Exists(files.StatePath));
            Assert.IsTrue(File.Exists(files.SidecarPath));
        }

        [TestMethod]
        public void AbandonedTemporaryStateIsNeitherAdoptedNorDeleted()
        {
            using TemporaryProfile files = new();
            files.CreateStateDirectories();
            string abandonedInstallation = Path.Combine(files.TelemetryPath, $".dab-telemetry-{Guid.NewGuid():N}.tmp");
            string abandonedApi = Path.Combine(files.RootPath, $".dab-telemetry-{Guid.NewGuid():N}.tmp");
            TemporaryProfile.WritePrivateFile(abandonedInstallation, Encoding.UTF8.GetBytes(INSTALLATION_STATE));
            TemporaryProfile.WritePrivateFile(abandonedApi, Encoding.UTF8.GetBytes(API_STATE));

            Assert.AreNotEqual(Guid.Parse(SYNTHETIC_ID), AssertAvailable(ResolveEnabled(files.ProfilePath), "newly_saved"));
            Assert.IsNull(LookupEnabled(files.ConfigPath));

            Assert.AreEqual(INSTALLATION_STATE, File.ReadAllText(abandonedInstallation));
            Assert.AreEqual(API_STATE, File.ReadAllText(abandonedApi));
            CollectionAssert.AreEquivalent(new[] { files.StatePath, abandonedInstallation }, Directory.GetFiles(files.TelemetryPath));
            Assert.IsFalse(File.Exists(files.SidecarPath));
        }

        [TestMethod]
        public async Task ConcurrentCreatorsConvergeWithExactlyOneFirstRunWinnerIncludingDirectoryCreation()
        {
            using TemporaryProfile files = new();
            Assert.IsFalse(Directory.Exists(files.DabPath));
            using Barrier start = new(12);
            Task<CliTelemetryInstallation>[] creators = Enumerable.Range(0, 12).Select(_ =>
                Task.Factory.StartNew(() =>
                {
                    if (!start.SignalAndWait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Synthetic concurrent start did not complete.");
                    }

                    return ResolveEnabled(files.ProfilePath);
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

            CliTelemetryInstallation[] identities = await Task.WhenAll(creators).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.AreEqual(1, identities.Count(identity => identity.Stability == "newly_saved"));
            Assert.AreEqual(11, identities.Count(identity => identity.Stability == "reused"));
            Assert.AreEqual(1, identities.Select(identity => identity.InstallationId).Distinct().Count());
            foreach (CliTelemetryInstallation identity in identities)
            {
                AssertAvailable(identity, identity.Stability);
            }

            byte[] before = File.ReadAllBytes(files.StatePath);
            Assert.AreEqual(identities[0].InstallationId, AssertAvailable(ResolveEnabled(files.ProfilePath), "reused"));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(files.StatePath));
            CollectionAssert.AreEquivalent(new[] { files.StatePath }, Directory.GetFiles(files.TelemetryPath));
            Assert.IsFalse(File.Exists(files.SidecarPath));
        }

        [TestMethod]
        public void UnsupportedPlatformsReturnUnavailableWithoutCreatingProfileState()
        {
            if (OperatingSystem.IsWindows()
                || (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64))
            {
                Assert.Inconclusive("This control exercises conservative unsupported-platform behavior, including macOS.");
                return;
            }

            string profile = Directory.CreateTempSubdirectory("dab-cli-unsupported-tests-").FullName;
            try
            {
                AssertUnavailable(ResolveEnabled(profile));
                Assert.AreEqual(0, Directory.GetFileSystemEntries(profile).Length);
            }
            finally
            {
                Directory.Delete(profile, recursive: true);
            }
        }

        private static CliTelemetryInstallation ResolveEnabled(string profile)
        {
            return CliTelemetryInstallationStore.Resolve(static _ => null, profile);
        }

        private static EngineTelemetryIdentity? LookupEnabled(string? configPath)
        {
            return EngineTelemetryIdentityStore.Lookup(configPath, static _ => null);
        }

        private static Guid AssertAvailable(CliTelemetryInstallation installation, string stability)
        {
            Assert.AreEqual(stability, installation.Stability);
            Assert.IsTrue(stability is "newly_saved" or "reused");
            Assert.IsTrue(installation.InstallationId.HasValue);
            Guid id = installation.InstallationId.GetValueOrDefault();
            Assert.AreNotEqual(Guid.Empty, id);
            byte[] bytes = id.ToByteArray();
            Assert.AreEqual(0x40, bytes[7] & 0xf0);
            Assert.AreEqual(0x80, bytes[8] & 0xc0);
            return id;
        }

        private static void AssertUnavailable(CliTelemetryInstallation installation)
        {
            Assert.AreEqual("unavailable", installation.Stability);
            Assert.IsNull(installation.InstallationId, "No ephemeral installation ID may enter a CLI event.");
        }

        private static void CreateFileLinkOrSkip(string path, string target)
        {
            try
            {
                File.CreateSymbolicLink(path, target);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Assert.Inconclusive("Symbolic links are unavailable for this test process/filesystem.");
            }
        }

        private static void CreateDirectoryLinkOrSkip(string path, string target)
        {
            try
            {
                Directory.CreateSymbolicLink(path, target);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Assert.Inconclusive("Symbolic links are unavailable for this test process/filesystem.");
            }
        }

        [SupportedOSPlatform("windows")]
        private static FileSystemSecurity ReadWindowsSecurity(string path, bool isDirectory)
        {
            const AccessControlSections sections = AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access;
            return isDirectory ? new DirectoryInfo(path).GetAccessControl(sections) : new FileInfo(path).GetAccessControl(sections);
        }

        [SupportedOSPlatform("windows")]
        private static void WriteWindowsSecurity(string path, bool isDirectory, FileSystemSecurity security)
        {
            // GetAccessControl returns an unmodified descriptor. SetAccessControl persists only
            // dirty sections, so restoring that object directly can silently do nothing. Import
            // the saved binary descriptor into a fresh object to mark every restored section.
            byte[] descriptor = security.GetSecurityDescriptorBinaryForm();
            if (isDirectory)
            {
                DirectorySecurity copy = new();
                copy.SetSecurityDescriptorBinaryForm(descriptor, AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);
                new DirectoryInfo(path).SetAccessControl(copy);
            }
            else
            {
                FileSecurity copy = new();
                copy.SetSecurityDescriptorBinaryForm(descriptor, AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);
                new FileInfo(path).SetAccessControl(copy);
            }
        }

        [SupportedOSPlatform("windows")]
        private static void AssertPrivateWindowsObject(string path, bool isDirectory)
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier user = identity.User ?? throw new InvalidOperationException("Synthetic fixture requires a Windows user SID.");
            FileSystemSecurity security = ReadWindowsSecurity(path, isDirectory);
            Assert.AreEqual(user, security.GetOwner(typeof(SecurityIdentifier)));
            Assert.IsTrue(security.AreAccessRulesProtected, "New state must have protected create-time ACLs.");
            FileSystemAccessRule[] grants = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>().Where(rule => rule.AccessControlType == AccessControlType.Allow).ToArray();
            Assert.IsTrue(grants.Any(rule => rule.IdentityReference.Equals(user)
                && (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl));
            foreach (FileSystemAccessRule grant in grants)
            {
                SecurityIdentifier principal = (SecurityIdentifier)grant.IdentityReference;
                Assert.IsTrue(principal.Equals(user) || principal.IsWellKnown(WellKnownSidType.LocalSystemSid)
                    || principal.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
            }
        }

        private sealed class TemporaryProfile : IDisposable
        {
            public TemporaryProfile()
            {
                if (!OperatingSystem.IsWindows()
                    && (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64)))
                {
                    Assert.Inconclusive("Persistent fixtures require Windows or Linux x64/arm64 with statx.");
                }

                RootPath = Directory.CreateTempSubdirectory("dab-cli-identity-tests-").FullName;
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        // Fixtures are exclusively owned; setting their ACL does not touch a real profile.
                        new DirectoryInfo(RootPath).SetAccessControl(NewWindowsDirectorySecurity());
                    }
                    else
                    {
                        File.SetUnixFileMode(RootPath, PRIVATE_DIRECTORY_MODE);
                    }

                    CreatePrivateDirectory(ProfilePath);
                    WritePrivateFile(ConfigPath, Encoding.UTF8.GetBytes("{\"synthetic\":true}"));
                }
                catch
                {
                    Directory.Delete(RootPath, recursive: true);
                    throw;
                }
            }

            public string RootPath { get; }
            public string ProfilePath => Path.Combine(RootPath, "profile");
            public string DabPath => Path.Combine(ProfilePath, ".dab");
            public string TelemetryPath => Path.Combine(DabPath, "telemetry");
            public string StatePath => Path.Combine(TelemetryPath, "installation.json");
            public string ConfigPath => Path.Combine(RootPath, "root.json");
            public string SidecarPath => ConfigPath + ".dab-telemetry.json";

            public string GetPath(string target) => target switch
            {
                "profile" => ProfilePath,
                "dab" => DabPath,
                "telemetry" => TelemetryPath,
                "state" => StatePath,
                _ => throw new ArgumentException("Unknown synthetic fixture target.", nameof(target))
            };

            public void CreateStateDirectories()
            {
                CreatePrivateDirectory(DabPath);
                CreatePrivateDirectory(TelemetryPath);
            }

            public void WriteState(byte[] bytes)
            {
                CreateStateDirectories();
                WritePrivateFile(StatePath, bytes);
            }

            public static void CreatePrivateDirectory(string path)
            {
                if (OperatingSystem.IsWindows())
                {
                    new DirectoryInfo(path).Create(NewWindowsDirectorySecurity());
                }
                else
                {
                    Directory.CreateDirectory(path, PRIVATE_DIRECTORY_MODE);
                }
            }

            public static void WritePrivateFile(string path, byte[] bytes)
            {
                if (OperatingSystem.IsWindows())
                {
                    using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                    SecurityIdentifier user = identity.User ?? throw new InvalidOperationException("Synthetic fixture requires a Windows user SID.");
                    FileSecurity security = new();
                    security.SetSecurityDescriptorSddlForm($"O:{user.Value}D:P(A;;FA;;;{user.Value})(A;;FA;;;SY)(A;;FA;;;BA)",
                        AccessControlSections.Owner | AccessControlSections.Access);
                    using FileStream stream = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl,
                        FileShare.None, 1024, FileOptions.None, security);
                    stream.Write(bytes);
                }
                else
                {
                    using FileStream stream = new(path, new FileStreamOptions
                    {
                        Mode = FileMode.CreateNew,
                        Access = FileAccess.Write,
                        Share = FileShare.None,
                        UnixCreateMode = PRIVATE_FILE_MODE
                    });
                    stream.Write(bytes);
                }
            }

            [SupportedOSPlatform("windows")]
            private static DirectorySecurity NewWindowsDirectorySecurity()
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                SecurityIdentifier user = identity.User ?? throw new InvalidOperationException("Synthetic fixture requires a Windows user SID.");
                DirectorySecurity security = new();
                security.SetSecurityDescriptorSddlForm($"O:{user.Value}D:P(A;OICI;FA;;;{user.Value})(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)",
                    AccessControlSections.Owner | AccessControlSections.Access);
                return security;
            }

            public void Dispose()
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private static class NativeMethods
        {
            [DllImport("libc", EntryPoint = "geteuid")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static extern uint GetEffectiveUserId();

            [DllImport("libc", EntryPoint = "chown", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static extern int ChangeOwner([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint owner, uint group);

            [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static extern int MakeFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
        }
    }
}
