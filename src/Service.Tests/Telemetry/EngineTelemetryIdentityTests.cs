// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    /// Synthetic, local-only storage tests. No engine, sender, database, process environment
    /// mutation or user configuration is involved. Every on-disk fixture is freshly temporary.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    public class EngineTelemetryIdentityTests
    {
        private const string SYNTHETIC_API_ID = "e4bc3f2c-5782-4adf-8a11-72f2418c7917";
        private const string VALID_STATE = "{\"version\":1,\"apiId\":\"" + SYNTHETIC_API_ID + "\"}";

        [TestInitialize]
        public void RequireSupportedStoragePlatform()
        {
            if (!OperatingSystem.IsWindows()
                && (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64)))
            {
                Assert.Inconclusive("Persistent storage is supported on Windows and Linux x64/arm64; other platforms use ephemeral IDs.");
            }
        }

        [TestMethod]
        public void FirstResolutionPublishesOnlyVersionAndRandomApiIdBesideRootConfig()
        {
            using TemporaryConfig files = new();
            byte[] configBefore = File.ReadAllBytes(files.ConfigPath);

            EngineTelemetryIdentity identity = ResolveEnabled(files.ConfigPath);

            Assert.AreEqual("newly_saved", identity.Stability);
            AssertRandomGuid(identity.ApiId);
            byte[] bytes = File.ReadAllBytes(files.SidecarPath);
            Assert.IsTrue(bytes.Length <= 1024);
            using JsonDocument document = JsonDocument.Parse(bytes);
            CollectionAssert.AreEquivalent(new[] { "version", "apiId" },
                document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.AreEqual(1, document.RootElement.GetProperty("version").GetInt32());
            Assert.AreEqual(identity.ApiId, document.RootElement.GetProperty("apiId").GetGuid());
            CollectionAssert.AreEqual(configBefore, File.ReadAllBytes(files.ConfigPath));
            CollectionAssert.AreEquivalent(new[] { files.ConfigPath, files.SidecarPath }, Directory.GetFiles(files.DirectoryPath));

            if (!OperatingSystem.IsWindows())
            {
                Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(files.SidecarPath));
            }
        }

        [TestMethod]
        public void RestartAndConfigurationEditsReuseImmutableSavedState()
        {
            using TemporaryConfig files = new();
            EngineTelemetryIdentity first = ResolveEnabled(files.ConfigPath);
            byte[] before = File.ReadAllBytes(files.SidecarPath);
            DateTime timestamp = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(files.SidecarPath, timestamp);

            EngineTelemetryIdentity restarted = ResolveEnabled(files.ConfigPath);
            // The store must not parse, hash or derive identity from configuration contents.
            File.WriteAllText(files.ConfigPath, "changed synthetic configuration; deliberately not JSON");
            EngineTelemetryIdentity afterEdit = ResolveEnabled(files.ConfigPath);

            Assert.AreEqual(first.ApiId, restarted.ApiId);
            Assert.AreEqual(first.ApiId, afterEdit.ApiId);
            Assert.AreEqual("reused", restarted.Stability);
            Assert.AreEqual("reused", afterEdit.Stability);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(files.SidecarPath));
            Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(files.SidecarPath));
        }

        [TestMethod]
        public void IndependentIdenticalConfigurationsReceiveIndependentIds()
        {
            using TemporaryConfig first = new();
            using TemporaryConfig second = new();
            CollectionAssert.AreEqual(File.ReadAllBytes(first.ConfigPath), File.ReadAllBytes(second.ConfigPath));

            EngineTelemetryIdentity firstIdentity = ResolveEnabled(first.ConfigPath);
            EngineTelemetryIdentity secondIdentity = ResolveEnabled(second.ConfigPath);

            Assert.AreEqual("newly_saved", firstIdentity.Stability);
            Assert.AreEqual("newly_saved", secondIdentity.Stability);
            Assert.AreNotEqual(firstIdentity.ApiId, secondIdentity.ApiId);
        }

        [TestMethod]
        public void DifferentRootFilesInSameDirectoryDoNotShareIdentity()
        {
            using TemporaryConfig files = new();
            string otherConfig = Path.Combine(files.DirectoryPath, "other-root.json");
            File.Copy(files.ConfigPath, otherConfig);

            EngineTelemetryIdentity first = ResolveEnabled(files.ConfigPath);
            EngineTelemetryIdentity second = ResolveEnabled(otherConfig);

            Assert.AreEqual("newly_saved", first.Stability);
            Assert.AreEqual("newly_saved", second.Stability);
            Assert.AreNotEqual(first.ApiId, second.ApiId);
            Assert.IsTrue(File.Exists(otherConfig + ".dab-telemetry.json"));
        }

        [TestMethod]
        public void ExistingRelativeConfigResolvesToSameRootSidecar()
        {
            using TemporaryConfig files = new();
            string relativePath = Path.GetRelativePath(Environment.CurrentDirectory, files.ConfigPath);

            EngineTelemetryIdentity first = ResolveEnabled(relativePath);
            EngineTelemetryIdentity absolute = ResolveEnabled(files.ConfigPath);

            Assert.AreEqual("newly_saved", first.Stability);
            Assert.AreEqual("reused", absolute.Stability);
            Assert.AreEqual(first.ApiId, absolute.ApiId);
        }

        [TestMethod]
        public void LocalResetCreatesUnrelatedStateWithoutPreviousIdOrLinkage()
        {
            using TemporaryConfig files = new();
            EngineTelemetryIdentity first = ResolveEnabled(files.ConfigPath);
            // No engine is running: local reset removes only identity state, not configuration.
            File.Delete(files.SidecarPath);

            EngineTelemetryIdentity afterReset = ResolveEnabled(files.ConfigPath);

            Assert.AreEqual("newly_saved", afterReset.Stability);
            Assert.AreNotEqual(first.ApiId, afterReset.ApiId);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(files.SidecarPath));
            Assert.AreEqual(2, document.RootElement.EnumerateObject().Count());
            Assert.AreEqual(afterReset.ApiId, document.RootElement.GetProperty("apiId").GetGuid());
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" \t ")]
        [DataRow("\0")]
        [DataRow("https://synthetic.invalid/config.json")]
        [DataRow(@"\\synthetic.invalid\share\config.json")]
        [DataRow("//synthetic.invalid/share/config.json")]
        [DataRow(@"\\?\C:\synthetic\config.json")]
        public void MissingOrNonLocalInputReturnsFreshEphemeralIds(string? path)
        {
            EngineTelemetryIdentity first = ResolveEnabled(path);
            EngineTelemetryIdentity second = ResolveEnabled(path);

            AssertEphemeral(first);
            AssertEphemeral(second);
            Assert.AreNotEqual(first.ApiId, second.ApiId, "The parent session, not the store, retains ephemeral IDs.");
        }

        [TestMethod]
        public void MissingAbsoluteAndRelativeConfigsAndDirectoriesAreNeverCreated()
        {
            using TemporaryConfig files = new();
            string absentConfig = Path.Combine(files.DirectoryPath, "absent.json");
            string absentDirectory = Path.Combine(files.DirectoryPath, "absent-directory");
            string relativePath = Path.GetRelativePath(Environment.CurrentDirectory, absentConfig);

            AssertEphemeral(ResolveEnabled(absentConfig));
            AssertEphemeral(ResolveEnabled(relativePath));
            AssertEphemeral(ResolveEnabled(Path.Combine(absentDirectory, "config.json")));

            Assert.IsFalse(Directory.Exists(absentDirectory));
            CollectionAssert.AreEquivalent(new[] { files.ConfigPath }, Directory.GetFiles(files.DirectoryPath));
        }

        [TestMethod]
        public void SavedStateDoesNotMakeAnAbsentRootConfigurationEligible()
        {
            using TemporaryConfig files = new();
            files.WriteState(VALID_STATE);
            File.Delete(files.ConfigPath);

            AssertEphemeral(ResolveEnabled(files.ConfigPath));
            Assert.AreEqual(VALID_STATE, File.ReadAllText(files.SidecarPath));
        }

        [DataTestMethod]
        [DataRow("1")]
        [DataRow(" true ")]
        [DataRow("TRUE")]
        public void OptOutPrecedesStorageAndLeavesSavedIdentityUntouched(string value)
        {
            using TemporaryConfig files = new();
            files.WriteState(VALID_STATE);
            byte[] before = File.ReadAllBytes(files.SidecarPath);
            DateTime timestamp = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(files.SidecarPath, timestamp);
            int policyReads = 0;

            EngineTelemetryIdentity identity = EngineTelemetryIdentityStore.Resolve(files.ConfigPath, variable =>
            {
                Assert.AreEqual(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, variable);
                policyReads++;
                return value;
            });

            AssertEphemeral(identity);
            Assert.AreNotEqual(Guid.Parse(SYNTHETIC_API_ID), identity.ApiId);
            Assert.AreEqual(1, policyReads);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(files.SidecarPath));
            Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(files.SidecarPath));
            Assert.AreEqual(Guid.Parse(SYNTHETIC_API_ID), ResolveEnabled(files.ConfigPath).ApiId,
                "Re-enablement reuses state; opt-out is not reset.");
            CollectionAssert.AreEquivalent(new[] { files.ConfigPath, files.SidecarPath }, Directory.GetFiles(files.DirectoryPath));
        }

        [TestMethod]
        public void OptOutOrPolicyFailureCannotCreateIdentityOrTemporaryFiles()
        {
            using TemporaryConfig files = new();

            AssertEphemeral(EngineTelemetryIdentityStore.Resolve(files.ConfigPath, static _ => "1"));
            AssertEphemeral(EngineTelemetryIdentityStore.Resolve(files.ConfigPath,
                static _ => throw new InvalidOperationException("Synthetic policy failure.")));
            AssertEphemeral(EngineTelemetryIdentityStore.Resolve("\0", static _ => "true"));

            CollectionAssert.AreEquivalent(new[] { files.ConfigPath }, Directory.GetFiles(files.DirectoryPath));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("0")]
        [DataRow("false")]
        [DataRow("yes")]
        public void NonOptOutValuesDoNotPreventResolutionForAnEnabledCaller(string? value)
        {
            using TemporaryConfig files = new();

            EngineTelemetryIdentity identity = EngineTelemetryIdentityStore.Resolve(files.ConfigPath, _ => value);

            Assert.AreEqual("newly_saved", identity.Stability);
            AssertRandomGuid(identity.ApiId);
        }

        [TestMethod]
        public void ConfigurationContentsAreNeverOpened()
        {
            using TemporaryConfig files = new();
            // Windows enforces this sharing denial; only metadata queries remain possible.
            using FileStream held = new(files.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None);

            EngineTelemetryIdentity identity = ResolveEnabled(files.ConfigPath);

            Assert.AreEqual("newly_saved", identity.Stability);
            AssertRandomGuid(identity.ApiId);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("{")]
        [DataRow("[]")]
        [DataRow("null")]
        [DataRow("{}")]
        [DataRow("{\"apiId\":\"$id\"}")]
        [DataRow("{\"version\":1}")]
        [DataRow("{\"version\":0,\"apiId\":\"$id\"}")]
        [DataRow("{\"version\":2,\"apiId\":\"$id\"}")]
        [DataRow("{\"version\":1.5,\"apiId\":\"$id\"}")]
        [DataRow("{\"version\":\"1\",\"apiId\":\"$id\"}")]
        [DataRow("{\"version\":true,\"apiId\":\"$id\"}")]
        [DataRow("{\"version\":null,\"apiId\":\"$id\"}")]
        [DataRow("{\"version\":1,\"apiId\":null}")]
        [DataRow("{\"version\":1,\"apiId\":1}")]
        [DataRow("{\"version\":1,\"apiId\":[]}")]
        [DataRow("{\"version\":1,\"apiId\":{}}")]
        [DataRow("{\"version\":1,\"apiId\":\"not-a-guid\"}")]
        [DataRow("{\"version\":1,\"apiId\":\"00000000-0000-0000-0000-000000000000\"}")]
        [DataRow("{\"version\":1,\"apiId\":\"e4bc3f2c-5782-1adf-8a11-72f2418c7917\"}")]
        [DataRow("{\"version\":1,\"apiId\":\"e4bc3f2c-5782-5adf-8a11-72f2418c7917\"}")]
        [DataRow("{\"version\":1,\"apiId\":\"e4bc3f2c-5782-4adf-0a11-72f2418c7917\"}")]
        [DataRow("{\"version\":1,\"apiId\":\" $id\"}")]
        [DataRow("{\"version\":1,\"apiId\":\"{$id}\"}")]
        [DataRow("{\"version\":1,\"version\":1,\"apiId\":\"$id\"}")]
        [DataRow("{\"version\":1,\"apiId\":\"$id\",\"apiId\":\"$id\"}")]
        [DataRow("{\"Version\":1,\"apiId\":\"$id\"}")]
        [DataRow("{\"version\":1,\"apiId\":\"$id\",\"configuration\":\"synthetic\"}")]
        [DataRow("{\"version\":1,\"apiId\":\"$id\",\"noticeShown\":true}")]
        [DataRow("{\"version\":1,\"apiId\":\"$id\",}")]
        [DataRow("/*comment*/{\"version\":1,\"apiId\":\"$id\"}")]
        public void InvalidStateIsNeverOverwrittenRepairedOrAdopted(string state)
        {
            using TemporaryConfig files = new();
            byte[] before = Encoding.UTF8.GetBytes(state.Replace("$id", SYNTHETIC_API_ID, StringComparison.Ordinal));
            files.WriteState(before);

            EngineTelemetryIdentity first = ResolveEnabled(files.ConfigPath);
            EngineTelemetryIdentity second = ResolveEnabled(files.ConfigPath);

            AssertEphemeral(first);
            AssertEphemeral(second);
            Assert.AreNotEqual(first.ApiId, second.ApiId);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(files.SidecarPath));
            CollectionAssert.AreEquivalent(new[] { files.ConfigPath, files.SidecarPath }, Directory.GetFiles(files.DirectoryPath));
        }

        [DataTestMethod]
        [DataRow(1024, "reused")]
        [DataRow(1025, "ephemeral")]
        [DataRow(4096, "ephemeral")]
        public void StateByteLimitIsEnforcedWithoutTruncatingTheFile(int length, string stability)
        {
            using TemporaryConfig files = new();
            byte[] before = Encoding.UTF8.GetBytes(VALID_STATE.PadRight(length));
            files.WriteState(before);

            EngineTelemetryIdentity identity = ResolveEnabled(files.ConfigPath);

            Assert.AreEqual(stability, identity.Stability);
            AssertRandomGuid(identity.ApiId);
            if (stability == "reused")
            {
                Assert.AreEqual(Guid.Parse(SYNTHETIC_API_ID), identity.ApiId);
            }

            CollectionAssert.AreEqual(before, File.ReadAllBytes(files.SidecarPath));
        }

        [TestMethod]
        public void MalformedUtf8RemainsUntouched()
        {
            using TemporaryConfig files = new();
            byte[] before = { 0xff, 0xfe, 0x7b, 0x7d };
            files.WriteState(before);

            AssertEphemeral(ResolveEnabled(files.ConfigPath));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(files.SidecarPath));
        }

        [TestMethod]
        public void DirectoryAtSidecarPathIsNotReplaced()
        {
            using TemporaryConfig files = new();
            Directory.CreateDirectory(files.SidecarPath);
            string sentinel = Path.Combine(files.SidecarPath, "untouched.txt");
            File.WriteAllText(sentinel, "synthetic sentinel");

            AssertEphemeral(ResolveEnabled(files.ConfigPath));

            Assert.AreEqual("synthetic sentinel", File.ReadAllText(sentinel));
            CollectionAssert.AreEquivalent(new[] { files.ConfigPath }, Directory.GetFiles(files.DirectoryPath));
        }

        [TestMethod]
        public void ReadOnlyDirectoryWithoutStateFallsBackWithoutChangingPermissions()
        {
            using TemporaryConfig files = new();
            FileAttributes attributes = File.GetAttributes(files.DirectoryPath);
            UnixFileMode? mode = OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(files.DirectoryPath);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Windows directory ReadOnly is an advisory marker, not an ACL test.
                    // The store conservatively refuses it rather than trying to change ACLs.
                    File.SetAttributes(files.DirectoryPath, attributes | FileAttributes.ReadOnly);
                }
                else
                {
                    File.SetUnixFileMode(files.DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                }

                AssertEphemeral(ResolveEnabled(files.ConfigPath));
                Assert.IsFalse(File.Exists(files.SidecarPath));
                if (!OperatingSystem.IsWindows())
                {
                    Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserExecute, File.GetUnixFileMode(files.DirectoryPath));
                }
            }
            finally
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(files.DirectoryPath, mode!.Value);
                }

                File.SetAttributes(files.DirectoryPath, attributes);
            }
        }

        [TestMethod]
        public void ValidReadOnlyStateIsReusedWithoutRequiringWrites()
        {
            using TemporaryConfig files = new();
            files.WriteState(VALID_STATE);
            FileAttributes attributes = File.GetAttributes(files.SidecarPath);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    File.SetAttributes(files.SidecarPath, attributes | FileAttributes.ReadOnly);
                }
                else
                {
                    File.SetUnixFileMode(files.SidecarPath, UnixFileMode.UserRead);
                }

                EngineTelemetryIdentity identity = ResolveEnabled(files.ConfigPath);

                Assert.AreEqual("reused", identity.Stability);
                Assert.AreEqual(Guid.Parse(SYNTHETIC_API_ID), identity.ApiId);
                Assert.AreEqual(VALID_STATE, File.ReadAllText(files.SidecarPath));
            }
            finally
            {
                File.SetAttributes(files.SidecarPath, attributes);
            }
        }

        [DataTestMethod]
        [DataRow(UnixFileMode.GroupRead)]
        [DataRow(UnixFileMode.OtherRead)]
        [DataRow(UnixFileMode.GroupWrite)]
        [DataRow(UnixFileMode.OtherWrite)]
        [DataRow(UnixFileMode.UserExecute)]
        public void NonPrivateUnixStateFallsBackWithoutRepairingPermissions(UnixFileMode extraPermission)
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Unix permission test.");
                return;
            }

            using TemporaryConfig files = new();
            files.WriteState(VALID_STATE);
            UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | extraPermission;
            File.SetUnixFileMode(files.SidecarPath, mode);

            AssertEphemeral(ResolveEnabled(files.ConfigPath));

            Assert.AreEqual(mode, File.GetUnixFileMode(files.SidecarPath));
            Assert.AreEqual(VALID_STATE, File.ReadAllText(files.SidecarPath));
        }

        [DataTestMethod]
        [DataRow(UnixFileMode.GroupWrite)]
        [DataRow(UnixFileMode.OtherWrite)]
        public void SharedWritableUnixParentIsNotUsed(UnixFileMode extraPermission)
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Unix permission test.");
                return;
            }

            using TemporaryConfig files = new();
            UnixFileMode original = File.GetUnixFileMode(files.DirectoryPath);
            try
            {
                File.SetUnixFileMode(files.DirectoryPath, original | extraPermission);

                AssertEphemeral(ResolveEnabled(files.ConfigPath));
                Assert.AreEqual(original | extraPermission, File.GetUnixFileMode(files.DirectoryPath));
                Assert.IsFalse(File.Exists(files.SidecarPath));
            }
            finally
            {
                File.SetUnixFileMode(files.DirectoryPath, original);
            }
        }

        [TestMethod]
        public void WindowsSharingFailureReturnsEphemeralWithoutReplacingState()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Windows mandatory file-sharing test.");
                return;
            }

            using TemporaryConfig files = new();
            files.WriteState(VALID_STATE);
            using (FileStream held = new(files.SidecarPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                AssertEphemeral(ResolveEnabled(files.ConfigPath));
            }

            Assert.AreEqual(VALID_STATE, File.ReadAllText(files.SidecarPath));
            CollectionAssert.AreEquivalent(new[] { files.ConfigPath, files.SidecarPath }, Directory.GetFiles(files.DirectoryPath));
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void SidecarSymlinkIncludingDanglingTargetIsRejected(bool targetExists)
        {
            using TemporaryConfig files = new();
            string target = Path.Combine(files.DirectoryPath, "synthetic-target.json");
            if (targetExists)
            {
                TemporaryConfig.WritePrivateFile(target, Encoding.UTF8.GetBytes(VALID_STATE));
            }

            CreateFileLinkOrSkip(files.SidecarPath, target);

            AssertEphemeral(ResolveEnabled(files.ConfigPath));

            Assert.IsNotNull(new FileInfo(files.SidecarPath).LinkTarget);
            if (targetExists)
            {
                Assert.AreEqual(VALID_STATE, File.ReadAllText(target));
            }
            else
            {
                Assert.IsFalse(File.Exists(target));
            }

            Assert.AreEqual(0, Directory.GetFiles(files.DirectoryPath, ".dab-telemetry-*.tmp").Length);
        }

        [TestMethod]
        public void ConfigurationSymlinkIsRejectedWithoutFollowingItsContents()
        {
            using TemporaryConfig files = new();
            string configLink = Path.Combine(files.DirectoryPath, "linked-root.json");
            CreateFileLinkOrSkip(configLink, files.ConfigPath);

            AssertEphemeral(ResolveEnabled(configLink));

            Assert.IsFalse(File.Exists(configLink + ".dab-telemetry.json"));
            Assert.IsFalse(File.Exists(files.SidecarPath));
        }

        [TestMethod]
        public void SymlinkedDirectoryAncestorIsRejected()
        {
            using TemporaryConfig files = new();
            string realDirectory = Path.Combine(files.DirectoryPath, "real");
            string childDirectory = Path.Combine(realDirectory, "child");
            Directory.CreateDirectory(childDirectory);
            string config = Path.Combine(childDirectory, "root.json");
            File.WriteAllText(config, "synthetic config");
            string link = Path.Combine(files.DirectoryPath, "directory-link");
            try
            {
                Directory.CreateSymbolicLink(link, realDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Assert.Inconclusive("Creating symbolic links is unavailable for this test process/filesystem.");
                return;
            }

            AssertEphemeral(ResolveEnabled(Path.Combine(link, "child", "root.json")));
            Assert.IsFalse(File.Exists(config + ".dab-telemetry.json"));
        }

        [TestMethod]
        public void AbandonedTemporaryFileIsNeitherAdoptedNorDeleted()
        {
            using TemporaryConfig files = new();
            string abandoned = Path.Combine(files.DirectoryPath, $".dab-telemetry-{Guid.NewGuid():N}.tmp");
            TemporaryConfig.WritePrivateFile(abandoned, Encoding.UTF8.GetBytes(VALID_STATE));

            EngineTelemetryIdentity identity = ResolveEnabled(files.ConfigPath);

            Assert.AreEqual("newly_saved", identity.Stability);
            Assert.AreNotEqual(Guid.Parse(SYNTHETIC_API_ID), identity.ApiId);
            Assert.AreEqual(VALID_STATE, File.ReadAllText(abandoned));
            CollectionAssert.AreEquivalent(new[] { files.ConfigPath, files.SidecarPath, abandoned }, Directory.GetFiles(files.DirectoryPath));
        }

        [TestMethod]
        public async Task ConcurrentCreatorsReadOneImmutableWinnerAndCleanTheirOwnTemporaryFiles()
        {
            // Repeated independent races exercise the native publication path, not a cached
            // identity or a process-local lock. Every attempt must preserve one immutable winner.
            for (int iteration = 0; iteration < 20; iteration++)
            {
                using TemporaryConfig files = new();
                using Barrier start = new(12);
                Task<EngineTelemetryIdentity>[] creators = Enumerable.Range(0, 12).Select(_ =>
                    Task.Factory.StartNew(() =>
                    {
                        if (!start.SignalAndWait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException("Synthetic concurrent start did not complete.");
                        }

                        return ResolveEnabled(files.ConfigPath);
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

                EngineTelemetryIdentity[] identities = await Task.WhenAll(creators).WaitAsync(TimeSpan.FromSeconds(30));

                Assert.AreEqual(1, identities.Count(identity => identity.Stability == "newly_saved"), $"Iteration {iteration}.");
                Assert.AreEqual(11, identities.Count(identity => identity.Stability == "reused"), $"Iteration {iteration}.");
                Assert.AreEqual(1, identities.Select(identity => identity.ApiId).Distinct().Count(), $"Iteration {iteration}.");
                AssertRandomGuid(identities[0].ApiId);
                byte[] winnerBytes = File.ReadAllBytes(files.SidecarPath);
                EngineTelemetryIdentity subsequent = ResolveEnabled(files.ConfigPath);
                Assert.AreEqual("reused", subsequent.Stability);
                Assert.AreEqual(identities[0].ApiId, subsequent.ApiId);
                CollectionAssert.AreEqual(winnerBytes, File.ReadAllBytes(files.SidecarPath));
                CollectionAssert.AreEquivalent(new[] { files.ConfigPath, files.SidecarPath }, Directory.GetFiles(files.DirectoryPath));
            }
        }

        [TestMethod]
        public void UnavailableNoticeMarkerPerformsNoIoAndDoesNotMutateIdentity()
        {
            using TemporaryConfig files = new();

            Assert.IsFalse(EngineTelemetryIdentityStore.TryMarkNoticeShown(files.ConfigPath));
            Assert.IsFalse(File.Exists(files.SidecarPath));
            files.WriteState(VALID_STATE);
            Assert.IsFalse(EngineTelemetryIdentityStore.TryMarkNoticeShown(files.ConfigPath));
            Assert.IsFalse(EngineTelemetryIdentityStore.TryMarkNoticeShown(null));
            Assert.IsFalse(EngineTelemetryIdentityStore.TryMarkNoticeShown("\0"));

            Assert.AreEqual(VALID_STATE, File.ReadAllText(files.SidecarPath));
            CollectionAssert.AreEquivalent(new[] { files.ConfigPath, files.SidecarPath }, Directory.GetFiles(files.DirectoryPath));
        }

        private static EngineTelemetryIdentity ResolveEnabled(string? path)
        {
            return EngineTelemetryIdentityStore.Resolve(path, static _ => null);
        }

        private static void AssertEphemeral(EngineTelemetryIdentity identity)
        {
            Assert.AreEqual("ephemeral", identity.Stability);
            AssertRandomGuid(identity.ApiId);
        }

        private static void AssertRandomGuid(Guid id)
        {
            Assert.AreNotEqual(Guid.Empty, id);
            byte[] bytes = id.ToByteArray();
            Assert.AreEqual(0x40, bytes[7] & 0xf0);
            Assert.AreEqual(0x80, bytes[8] & 0xc0);
        }

        private static void CreateFileLinkOrSkip(string path, string target)
        {
            try
            {
                File.CreateSymbolicLink(path, target);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Assert.Inconclusive("Creating symbolic links is unavailable for this test process/filesystem.");
            }
        }

        private sealed class TemporaryConfig : IDisposable
        {
            public TemporaryConfig()
            {
                DirectoryPath = Directory.CreateTempSubdirectory("dab-identity-tests-").FullName;
                ConfigPath = Path.Combine(DirectoryPath, "root.json");
                File.WriteAllText(ConfigPath, "{\"synthetic\":true}");
            }

            public string DirectoryPath { get; }

            public string ConfigPath { get; }

            public string SidecarPath => ConfigPath + ".dab-telemetry.json";

            public void WriteState(string content)
            {
                WriteState(Encoding.UTF8.GetBytes(content));
            }

            public void WriteState(byte[] content)
            {
                WritePrivateFile(SidecarPath, content);
            }

            public static void WritePrivateFile(string path, byte[] content)
            {
                FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows())
                {
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                }

                using FileStream stream = new(path, options);
                stream.Write(content);
            }

            public void Dispose()
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
