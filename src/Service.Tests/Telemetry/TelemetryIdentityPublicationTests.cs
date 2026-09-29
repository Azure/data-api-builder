// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Real, synthetic disk fixtures only. Common cases exercise Windows publication or Linux
    /// renameat2, according to the actual test host; Windows results are not native Linux evidence.
    /// Reflection exposes private operations, not mutable process-wide hooks. The existing per-call
    /// directory check lets a test stop after inspection and staging, before actual publication.
    /// No user profile, configuration contents, environment, sender or database is involved.
    /// </summary>
    [TestClass]
    [TestCategory("CliTelemetry")]
    [TestCategory("EngineTelemetry")]
    public class TelemetryIdentityPublicationTests
    {
        private const int CREATOR_COUNT = 12;
        private const int RACE_REPETITIONS = 20;
        private const UnixFileMode PRIVATE_DIRECTORY_MODE = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        private const UnixFileMode PRIVATE_FILE_MODE = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        private delegate (Guid Id, bool Created)? ResolveStateDelegate(string directory, string statePath,
            string idPropertyName, bool allowCreate, Func<bool> isSafeDirectory, bool requireWindowsOwner);

        [TestInitialize]
        public void RequireSupportedStoragePlatform()
        {
            if (!OperatingSystem.IsWindows()
                && (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64)))
            {
                Assert.Inconclusive("Publication fixtures require Windows or Linux x64/arm64.");
            }
        }

        [DataTestMethod]
        [DataRow("apiId")]
        [DataRow("installationId")]
        public void PublicationPrimitivePublishesAnAbsentDestinationWithoutChangingItsSchema(string idPropertyName)
        {
            using TemporaryState files = new(idPropertyName);
            Guid candidate = Guid.NewGuid();
            byte[] bytes = StateBytes(idPropertyName, candidate);
            string source = files.WriteTemporary(bytes);
            Func<string, string, bool> publish = BindPersistenceMethod<Func<string, string, bool>>("TryPublishState");

            Assert.IsTrue(publish(source, files.StatePath), "The actual platform primitive must successfully publish a missing destination.");

            Assert.IsFalse(File.Exists(source), "Only successful publication consumes the source.");
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(files.StatePath));
            Assert.AreEqual(candidate, ReadStateId(files.StatePath, idPropertyName));
            AssertReused(files, candidate);
            files.AssertTemporaryFiles();
        }

        [DataTestMethod]
        [DataRow("apiId")]
        [DataRow("installationId")]
        public void PublicationPrimitiveNeverReplacesAnExistingValidWinner(string idPropertyName)
        {
            using TemporaryState files = new(idPropertyName);
            Guid winner = Guid.NewGuid();
            byte[] winnerBytes = StateBytes(idPropertyName, winner);
            byte[] candidateBytes = StateBytes(idPropertyName, Guid.NewGuid());
            TemporaryState.WritePrivateFile(files.StatePath, winnerBytes);
            DateTime timestamp = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(files.StatePath, timestamp);
            string source = files.WriteTemporary(candidateBytes);
            Func<string, string, bool> publish = BindPersistenceMethod<Func<string, string, bool>>("TryPublishState");

            Assert.IsFalse(publish(source, files.StatePath));

            CollectionAssert.AreEqual(winnerBytes, File.ReadAllBytes(files.StatePath));
            Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(files.StatePath));
            CollectionAssert.AreEqual(candidateBytes, File.ReadAllBytes(source), "The losing source remains owned by its caller.");
            Assert.AreEqual(winner, ReadStateId(files.StatePath, idPropertyName));
            AssertReused(files, winner);
            files.AssertTemporaryFiles(source);
        }

        [DataTestMethod]
        [DataRow("apiId", "valid")]
        [DataRow("installationId", "valid")]
        [DataRow("apiId", "corrupt")]
        [DataRow("installationId", "corrupt")]
        [DataRow("apiId", "future")]
        [DataRow("installationId", "future")]
        public void StateCreatedAfterInspectionIsNeitherReplacedNorRepaired(string idPropertyName, string stateKind)
        {
            using TemporaryState files = new(idPropertyName);
            Guid winner = Guid.NewGuid();
            byte[] winnerBytes = stateKind switch
            {
                "valid" => StateBytes(idPropertyName, winner),
                "future" => StateBytes(idPropertyName, winner, version: 2),
                "corrupt" => Encoding.UTF8.GetBytes($"{{\"version\":1,\"{idPropertyName}\":\"not-a-guid\"}}"),
                _ => throw new ArgumentException("Unknown synthetic state kind.", nameof(stateKind))
            };
            byte[] abandonedBytes = StateBytes(idPropertyName, Guid.NewGuid());
            string abandoned = files.WriteTemporary(abandonedBytes);
            DateTime timestamp = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            (Guid Id, bool Created)? result = ResolveAfterInspection(files, () =>
            {
                string staged = Directory.GetFiles(files.DirectoryPath, ".dab-telemetry-*.tmp")
                    .Single(path => path != abandoned);
                Assert.AreNotEqual(winner, ReadStateId(staged, idPropertyName));
                TemporaryState.WritePrivateFile(files.StatePath, winnerBytes);
                File.SetLastWriteTimeUtc(files.StatePath, timestamp);
            });

            if (stateKind == "valid")
            {
                Assert.IsTrue(result.HasValue);
                Assert.AreEqual(winner, result.GetValueOrDefault().Id);
                Assert.IsFalse(result.GetValueOrDefault().Created, "A loser must not report newly_saved or first-run ownership.");
                AssertReused(files, winner);
            }
            else
            {
                Assert.IsNull(result);
                AssertUnavailable(files);
            }

            CollectionAssert.AreEqual(winnerBytes, File.ReadAllBytes(files.StatePath));
            Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(files.StatePath));
            if (OperatingSystem.IsLinux())
            {
                Assert.AreEqual(PRIVATE_FILE_MODE, File.GetUnixFileMode(files.StatePath));
            }

            CollectionAssert.AreEqual(abandonedBytes, File.ReadAllBytes(abandoned));
            files.AssertTemporaryFiles(abandoned);
        }

        [DataTestMethod]
        [TestCategory("LinuxNativePublication")]
        [DataRow("apiId", true)]
        [DataRow("apiId", false)]
        [DataRow("installationId", true)]
        [DataRow("installationId", false)]
        public void LinuxSymlinkCreatedAfterInspectionIsNeitherFollowedNorReplaced(string idPropertyName, bool targetExists)
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Inconclusive("This case requires actual Linux no-replace and no-follow behavior.");
                return;
            }

            using TemporaryState files = new(idPropertyName);
            string target = Path.Combine(files.DirectoryPath, "synthetic-target.json");
            byte[] targetBytes = StateBytes(idPropertyName, Guid.NewGuid());
            if (targetExists)
            {
                TemporaryState.WritePrivateFile(target, targetBytes);
            }

            (Guid Id, bool Created)? result = ResolveAfterInspection(files, () =>
            {
                Assert.AreEqual(1, Directory.GetFiles(files.DirectoryPath, ".dab-telemetry-*.tmp").Length);
                // No capability skip on Linux: failure to exercise a real symlink is a failed test.
                File.CreateSymbolicLink(files.StatePath, target);
            });

            Assert.IsNull(result);
            AssertUnavailable(files);
            Assert.AreEqual(target, new FileInfo(files.StatePath).LinkTarget);
            if (targetExists)
            {
                CollectionAssert.AreEqual(targetBytes, File.ReadAllBytes(target));
                Assert.AreEqual(PRIVATE_FILE_MODE, File.GetUnixFileMode(target));
            }
            else
            {
                Assert.IsFalse(File.Exists(target), "A dangling destination link must not create or replace its target.");
            }

            files.AssertTemporaryFiles();
        }

        [DataTestMethod]
        [DataRow("apiId")]
        [DataRow("installationId")]
        public async Task ConcurrentStoreCreatorsConvergeOnExactlyOneSavedIdentity(string idPropertyName)
        {
            for (int repetition = 0; repetition < RACE_REPETITIONS; repetition++)
            {
                // Installation callers also race the real missing-directory creation path.
                using TemporaryState files = new(idPropertyName, createStateDirectories: false);
                if (files.IsInstallation)
                {
                    Assert.IsFalse(Directory.Exists(files.DabPath));
                }

                using Barrier start = new(CREATOR_COUNT);
                (Guid? Id, string Stability)[] identities = await RunCreatorsAsync(_ =>
                {
                    WaitForCreators(start);
                    return files.Resolve();
                });

                Assert.AreEqual(1, identities.Count(identity => identity.Stability == "newly_saved"));
                Assert.AreEqual(CREATOR_COUNT - 1, identities.Count(identity => identity.Stability == "reused"));
                Assert.IsTrue(identities.All(identity => identity.Id.HasValue));
                Assert.AreEqual(1, identities.Select(identity => identity.Id).Distinct().Count());
                Guid winner = identities[0].Id.GetValueOrDefault();
                Assert.AreEqual(winner, ReadStateId(files.StatePath, idPropertyName));
                AssertReused(files, winner);
                files.AssertTemporaryFiles();
            }
        }

        [DataTestMethod]
        [DataRow("apiId")]
        [DataRow("installationId")]
        public async Task ConcurrentInspectedCreatorsReadTheWinnerAndCleanOnlyTheirOwnTemporaryFiles(string idPropertyName)
        {
            for (int repetition = 0; repetition < RACE_REPETITIONS; repetition++)
            {
                using TemporaryState files = new(idPropertyName);
                byte[] abandonedBytes = StateBytes(idPropertyName, Guid.NewGuid());
                string abandoned = files.WriteTemporary(abandonedBytes);
                using Barrier staged = new(CREATOR_COUNT, _ =>
                {
                    // All missing-state inspections and all real flushed temporary writes have
                    // completed, and not one publisher has yet been allowed to rename its file.
                    Assert.IsFalse(File.Exists(files.StatePath));
                    string[] candidates = Directory.GetFiles(files.DirectoryPath, ".dab-telemetry-*.tmp")
                        .Where(path => path != abandoned).ToArray();
                    Assert.AreEqual(CREATOR_COUNT, candidates.Length);
                    Assert.AreEqual(CREATOR_COUNT, candidates.Select(path => ReadStateId(path, idPropertyName)).Distinct().Count());
                });

                (Guid Id, bool Created)?[] identities = await RunCreatorsAsync(_ =>
                    ResolveAfterInspection(files, () => WaitForCreators(staged)));

                Guid winner = ReadStateId(files.StatePath, idPropertyName);
                if (OperatingSystem.IsLinux())
                {
                    Assert.IsTrue(identities.All(identity => identity.HasValue), "Every Linux loser must read the atomically published winner.");
                    Assert.AreEqual(1, identities.Count(identity => identity.GetValueOrDefault().Created));
                    Assert.AreEqual(CREATOR_COUNT - 1, identities.Count(identity => !identity.GetValueOrDefault().Created));
                }
                else
                {
                    // Windows no-replace moves can overlap no-follow reads with incompatible
                    // sharing. Best-effort storage may be unavailable; it must never return a
                    // competing saved ID or more than one first-run winner. Do not add retries
                    // to product I/O merely to make an availability assertion under contention.
                    Assert.IsTrue(identities.Count(identity => identity.GetValueOrDefault().Created) <= 1);
                }

                Assert.IsTrue(identities.Where(identity => identity.HasValue).All(identity => identity.GetValueOrDefault().Id == winner));
                AssertReused(files, winner);
                CollectionAssert.AreEqual(abandonedBytes, File.ReadAllBytes(abandoned));
                files.AssertTemporaryFiles(abandoned);
            }
        }

        [DataTestMethod]
        [DataRow("apiId")]
        [DataRow("installationId")]
        public async Task ConcurrentPrimitivePublishersRetainOneWinnerAndAllLosingSources(string idPropertyName)
        {
            Func<string, string, bool> publish = BindPersistenceMethod<Func<string, string, bool>>("TryPublishState");
            for (int repetition = 0; repetition < RACE_REPETITIONS; repetition++)
            {
                using TemporaryState files = new(idPropertyName);
                Guid[] candidates = Enumerable.Range(0, CREATOR_COUNT).Select(_ => Guid.NewGuid()).ToArray();
                byte[][] bytes = candidates.Select(candidate => StateBytes(idPropertyName, candidate)).ToArray();
                string[] sources = bytes.Select(files.WriteTemporary).ToArray();
                using Barrier start = new(CREATOR_COUNT, _ =>
                {
                    Assert.IsFalse(File.Exists(files.StatePath));
                    Assert.AreEqual(CREATOR_COUNT, Directory.GetFiles(files.DirectoryPath, ".dab-telemetry-*.tmp").Length);
                });

                bool[] published = await RunCreatorsAsync(index =>
                {
                    WaitForCreators(start);
                    return publish(sources[index], files.StatePath);
                });

                Assert.AreEqual(1, published.Count(result => result));
                int winnerIndex = Array.FindIndex(published, result => result);
                Assert.AreEqual(candidates[winnerIndex], ReadStateId(files.StatePath, idPropertyName));
                CollectionAssert.AreEqual(bytes[winnerIndex], File.ReadAllBytes(files.StatePath));
                for (int index = 0; index < CREATOR_COUNT; index++)
                {
                    if (published[index])
                    {
                        Assert.IsFalse(File.Exists(sources[index]));
                    }
                    else
                    {
                        CollectionAssert.AreEqual(bytes[index], File.ReadAllBytes(sources[index]));
                    }
                }

                AssertReused(files, candidates[winnerIndex]);
                files.AssertTemporaryFiles(sources.Where((_, index) => !published[index]).ToArray());
            }
        }

        [DataTestMethod]
        [TestCategory("LinuxNativePublication")]
        [DataRow("missing-parent")]
        [DataRow("directory-collision")]
        public void LinuxPublicationErrorsLeaveTheSourceOwnedAndDoNotMutateTheDestination(string failure)
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Inconclusive("This case verifies errors from the actual Linux renameat2 primitive.");
                return;
            }

            using TemporaryState files = new("apiId");
            byte[] candidateBytes = StateBytes("apiId", Guid.NewGuid());
            string source = files.WriteTemporary(candidateBytes);
            string destination = failure == "missing-parent"
                ? Path.Combine(files.DirectoryPath, "missing-parent", "state.json") : files.StatePath;
            string sentinel = Path.Combine(destination, "untouched.json");
            if (failure == "directory-collision")
            {
                TemporaryState.CreatePrivateDirectory(destination);
                TemporaryState.WritePrivateFile(sentinel, candidateBytes);
            }

            Func<string, string, bool> publish = BindPersistenceMethod<Func<string, string, bool>>("TryPublishState");
            bool published = publish(source, destination);
            int error = Marshal.GetLastPInvokeError();

            Assert.IsFalse(published);
            Assert.AreEqual(failure == "missing-parent" ? 2 : 17, error, "Expected ENOENT or EEXIST from renameat2.");
            CollectionAssert.AreEqual(candidateBytes, File.ReadAllBytes(source));
            Assert.AreEqual(PRIVATE_FILE_MODE, File.GetUnixFileMode(source));
            if (failure == "directory-collision")
            {
                CollectionAssert.AreEqual(candidateBytes, File.ReadAllBytes(sentinel));
                Assert.AreEqual(PRIVATE_DIRECTORY_MODE, File.GetUnixFileMode(destination));
            }
            else
            {
                Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(destination)));
            }

            files.AssertTemporaryFiles(source);
        }

        private static TDelegate BindPersistenceMethod<TDelegate>(string name) where TDelegate : Delegate
        {
            MethodInfo? method = typeof(TelemetryIdentityPersistence).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            return method.CreateDelegate<TDelegate>();
        }

        private static (Guid Id, bool Created)? ResolveAfterInspection(TemporaryState files, Action beforePublish)
        {
            ResolveStateDelegate resolve = BindPersistenceMethod<ResolveStateDelegate>("ResolveState");
            bool inspected = false;
            (Guid Id, bool Created)? result = resolve(files.DirectoryPath, files.StatePath, files.IdPropertyName,
                allowCreate: true, () =>
                {
                    if (!inspected)
                    {
                        inspected = true;
                        Assert.IsFalse(File.Exists(files.StatePath), "The control must observe a missing destination before introducing a competitor.");
                        Assert.IsTrue(files.IsSafeDirectory());
                        beforePublish();
                    }

                    // The seam schedules the race; it never bypasses the actual directory policy.
                    return files.IsSafeDirectory();
                }, requireWindowsOwner: files.IsInstallation);
            Assert.IsTrue(inspected, "The control must reach the real inspect/stage/publish boundary.");
            return result;
        }

        private static async Task<T[]> RunCreatorsAsync<T>(Func<int, T> create)
        {
            Task<T>[] creators = Enumerable.Range(0, CREATOR_COUNT).Select(index =>
                Task.Factory.StartNew(() => create(index), CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            return await Task.WhenAll(creators).WaitAsync(TimeSpan.FromSeconds(30));
        }

        private static void WaitForCreators(Barrier barrier)
        {
            Assert.IsTrue(barrier.SignalAndWait(TimeSpan.FromSeconds(10)), "Synthetic publication barrier did not complete.");
        }

        private static byte[] StateBytes(string idPropertyName, Guid id, int version = 1)
        {
            return Encoding.UTF8.GetBytes($"{{\"version\":{version},\"{idPropertyName}\":\"{id:D}\"}}");
        }

        private static Guid ReadStateId(string path, string idPropertyName)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Assert.IsTrue(bytes.Length is > 0 and <= 1024);
            using JsonDocument document = JsonDocument.Parse(bytes);
            CollectionAssert.AreEquivalent(new[] { "version", idPropertyName },
                document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.AreEqual(1, document.RootElement.GetProperty("version").GetInt32());
            string? text = document.RootElement.GetProperty(idPropertyName).GetString();
            Assert.IsNotNull(text);
            Assert.AreEqual(36, text.Length);
            Assert.IsTrue(Guid.TryParseExact(text, "D", out Guid id));
            AssertRandomGuid(id);
            if (OperatingSystem.IsLinux())
            {
                Assert.AreEqual(PRIVATE_FILE_MODE, File.GetUnixFileMode(path));
            }

            return id;
        }

        private static void AssertRandomGuid(Guid id)
        {
            Assert.AreNotEqual(Guid.Empty, id);
            byte[] bytes = id.ToByteArray();
            Assert.AreEqual(0x40, bytes[7] & 0xf0);
            Assert.AreEqual(0x80, bytes[8] & 0xc0);
        }

        private static void AssertReused(TemporaryState files, Guid expected)
        {
            byte[] before = File.ReadAllBytes(files.StatePath);
            (Guid? Id, string Stability) identity = files.Resolve();
            Assert.AreEqual("reused", identity.Stability);
            Assert.IsTrue(identity.Id.HasValue);
            Assert.AreEqual(expected, identity.Id.GetValueOrDefault());
            CollectionAssert.AreEqual(before, File.ReadAllBytes(files.StatePath));
        }

        private static void AssertUnavailable(TemporaryState files)
        {
            (Guid? Id, string Stability) identity = files.Resolve();
            if (files.IsInstallation)
            {
                Assert.AreEqual("unavailable", identity.Stability);
                Assert.IsNull(identity.Id, "There must be no ephemeral installation identity.");
            }
            else
            {
                Assert.AreEqual("ephemeral", identity.Stability);
                Assert.IsTrue(identity.Id.HasValue);
                AssertRandomGuid(identity.Id.GetValueOrDefault());
                Assert.IsNull(EngineTelemetryIdentityStore.Lookup(files.ConfigPath, static _ => null));
            }
        }

        private sealed class TemporaryState : IDisposable
        {
            public TemporaryState(string idPropertyName, bool createStateDirectories = true)
            {
                IdPropertyName = idPropertyName;
                Assert.IsTrue(idPropertyName is "apiId" or "installationId");
                RootPath = Directory.CreateTempSubdirectory("dab-identity-publication-tests-").FullName;
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        new DirectoryInfo(RootPath).SetAccessControl(NewWindowsDirectorySecurity());
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        // Only synthetic objects are configured. Ownership stays with the test
                        // process, whether root or non-root, and no real profile is ever inspected.
                        File.SetUnixFileMode(RootPath, PRIVATE_DIRECTORY_MODE);
                    }

                    WritePrivateFile(ConfigPath, Encoding.UTF8.GetBytes("synthetic configuration; not parsed or hashed"));
                    if (IsInstallation)
                    {
                        CreatePrivateDirectory(ProfilePath);
                        if (createStateDirectories)
                        {
                            CreatePrivateDirectory(DabPath);
                            CreatePrivateDirectory(DirectoryPath);
                        }
                    }
                }
                catch
                {
                    Directory.Delete(RootPath, recursive: true);
                    throw;
                }
            }

            public string IdPropertyName { get; }
            public bool IsInstallation => IdPropertyName == "installationId";
            public string RootPath { get; }
            public string ConfigPath => Path.Combine(RootPath, "root.json");
            public string ProfilePath => Path.Combine(RootPath, "profile");
            public string DabPath => Path.Combine(ProfilePath, ".dab");
            public string DirectoryPath => IsInstallation ? Path.Combine(DabPath, "telemetry") : RootPath;
            public string StatePath => IsInstallation ? Path.Combine(DirectoryPath, "installation.json") : ConfigPath + ".dab-telemetry.json";

            public bool IsSafeDirectory()
            {
                return IsInstallation
                    ? BindPersistenceMethod<Func<string, string, bool>>("IsSafeInstallationDirectory")(DirectoryPath, ProfilePath)
                    : BindPersistenceMethod<Func<string, bool>>("IsSafeDirectory")(DirectoryPath);
            }

            public (Guid? Id, string Stability) Resolve()
            {
                if (IsInstallation)
                {
                    CliTelemetryInstallation installation = CliTelemetryInstallationStore.Resolve(static _ => null, ProfilePath);
                    return (installation.InstallationId, installation.Stability);
                }

                EngineTelemetryIdentity api = EngineTelemetryIdentityStore.Resolve(ConfigPath, static _ => null);
                return (api.ApiId, api.Stability);
            }

            public string WriteTemporary(byte[] bytes)
            {
                string path = Path.Combine(DirectoryPath, $".dab-telemetry-{Guid.NewGuid():N}.tmp");
                WritePrivateFile(path, bytes);
                return path;
            }

            public void AssertTemporaryFiles(params string[] expected)
            {
                CollectionAssert.AreEquivalent(expected, Directory.GetFiles(DirectoryPath, ".dab-telemetry-*.tmp"));
            }

            public static void CreatePrivateDirectory(string path)
            {
                if (OperatingSystem.IsWindows())
                {
                    new DirectoryInfo(path).Create(NewWindowsDirectorySecurity());
                }
                else if (OperatingSystem.IsLinux())
                {
                    Directory.CreateDirectory(path, PRIVATE_DIRECTORY_MODE);
                }
            }

            public static void WritePrivateFile(string path, byte[] bytes)
            {
                if (OperatingSystem.IsWindows())
                {
                    using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                    SecurityIdentifier user = identity.User ?? throw new InvalidOperationException("Synthetic fixture requires a Windows SID.");
                    FileSecurity security = new();
                    security.SetSecurityDescriptorSddlForm($"O:{user.Value}D:P(A;;FA;;;{user.Value})(A;;FA;;;SY)(A;;FA;;;BA)",
                        AccessControlSections.Owner | AccessControlSections.Access);
                    using FileStream stream = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl,
                        FileShare.None, 1024, FileOptions.None, security);
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                else if (OperatingSystem.IsLinux())
                {
                    using FileStream stream = new(path, new FileStreamOptions
                    {
                        Mode = FileMode.CreateNew,
                        Access = FileAccess.Write,
                        Share = FileShare.None,
                        UnixCreateMode = PRIVATE_FILE_MODE
                    });
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
            }

            [SupportedOSPlatform("windows")]
            private static DirectorySecurity NewWindowsDirectorySecurity()
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                SecurityIdentifier user = identity.User ?? throw new InvalidOperationException("Synthetic fixture requires a Windows SID.");
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
    }
}
