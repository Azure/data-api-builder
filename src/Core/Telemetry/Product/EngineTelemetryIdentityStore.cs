// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using Azure.DataApiBuilder.Config.Telemetry;
using Microsoft.Win32.SafeHandles;
using Path = System.IO.Path;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>A random deployment identity, never a configuration or installation fingerprint.</summary>
    internal sealed record EngineTelemetryIdentity(Guid ApiId, string Stability);

    /// <summary>
    /// Best-effort engine-only identity storage beside an existing root/merged configuration.
    /// The session must enforce enablement before calling and retain the result for its entire run,
    /// including reloads: an ephemeral identity is deliberately not cached or recovered here.
    /// </summary>
    /// <remarks>
    /// Reset locally by deleting the configuration's .dab-telemetry.json sidecar while all affected
    /// processes are stopped. The next enabled run generates an unrelated ID; no old/new linkage is
    /// retained. Reset does not erase previously collected data, and opting out is not a reset.
    ///
    /// Persistence requires an existing, deployment-controlled directory. Windows inherits its ACL;
    /// Linux requires an owner-controlled directory and a private, owner-readable identity file.
    /// Static directory links and final-component links are rejected; final reads also use native
    /// no-follow handles. This is not a sandbox against a directory owner replacing ancestors or
    /// mounts during a call. Persistence supports Windows and Linux x64/arm64 with statx; other
    /// platforms/native APIs fail closed to ephemeral identity. No permission changes are attempted.
    /// No directories, installation IDs, network clients, locks or retry loops are created. Local
    /// filesystem calls themselves have no hard time limit; filesystem crash durability is best effort.
    /// </remarks>
    internal static class EngineTelemetryIdentityStore
    {
        private const string SIDECAR_SUFFIX = ".dab-telemetry.json";
        private const int SCHEMA_VERSION = 1;
        private const int MAX_STATE_BYTES = 1024;
        private const UnixFileMode PRIVATE_FILE_MODE = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        private const UnixFileMode UNSAFE_DIRECTORY_MODE = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
        private const FileAttributes UNSAFE_FILE_ATTRIBUTES = FileAttributes.Directory
            | FileAttributes.ReparsePoint | FileAttributes.Device | FileAttributes.Offline;

        /// <summary>
        /// Resolves once for the supplied root/merged configuration, not individual constituent files.
        /// Returns only newly_saved, reused or ephemeral stability. Opt-out performs no filesystem I/O;
        /// its returned ephemeral value is not permission to collect or transmit telemetry.
        /// </summary>
        public static EngineTelemetryIdentity Resolve(string? configPath)
        {
            return Resolve(configPath, Environment.GetEnvironmentVariable);
        }

        /// <summary>Per-call test seam; does not mutate or depend on the process environment.</summary>
        internal static EngineTelemetryIdentity Resolve(string? configPath, Func<string, string?> readEnvironmentVariable)
        {
            string? temporaryPath = null;
            bool ownsTemporaryFile = false;

            try
            {
                ArgumentNullException.ThrowIfNull(readEnvironmentVariable);
                if (ProductTelemetryPolicy.IsOptedOut(readEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR))
                    || !TryGetStoragePaths(configPath, out string directory, out string sidecar))
                {
                    return CreateEphemeral();
                }

                ReadResult result = ReadIdentity(sidecar, out Guid savedId);
                if (result == ReadResult.Valid)
                {
                    return IsSafeDirectory(directory) ? new(savedId, "reused") : CreateEphemeral();
                }

                // An existing but invalid/unreadable file belongs to its owner. Never repair,
                // truncate, replace or remove it, including future versions of our own schema.
                if (result != ReadResult.Missing || !CanCreateInDirectory(directory))
                {
                    return CreateEphemeral();
                }

                Guid candidate = Guid.NewGuid();
                temporaryPath = Path.Combine(directory, $".dab-telemetry-{Guid.NewGuid():N}.tmp");
                FileStreamOptions options = new()
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    BufferSize = MAX_STATE_BYTES
                };

                if (OperatingSystem.IsLinux())
                {
                    // Set mode at creation, never chmod a briefly world-readable temporary file.
                    options.UnixCreateMode = PRIVATE_FILE_MODE;
                }

                using (FileStream stream = new(temporaryPath, options))
                {
                    // A failed CreateNew must not trigger cleanup of an existing file.
                    ownsTemporaryFile = true;
                    using Utf8JsonWriter writer = new(stream);
                    writer.WriteStartObject();
                    writer.WriteNumber("version", SCHEMA_VERSION);
                    writer.WriteString("apiId", candidate);
                    writer.WriteEndObject();
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                // Recheck the directory before publishing; no overwrite also protects a file
                // or link created by another process after the initial missing-file observation.
                if (!IsSafeDirectory(directory)
                    || ReadIdentity(temporaryPath, out Guid stagedId) != ReadResult.Valid || stagedId != candidate)
                {
                    return CreateEphemeral();
                }

                bool published = false;
                try
                {
                    File.Move(temporaryPath, sidecar, overwrite: false);
                    ownsTemporaryFile = false;
                    published = true;
                }
                catch (IOException)
                {
                    // Possibly lost a creation race. Read the winner once, never retry publication.
                }

                if (!IsSafeDirectory(directory) || ReadIdentity(sidecar, out savedId) != ReadResult.Valid
                    || !IsSafeDirectory(directory) || (published && savedId != candidate))
                {
                    return CreateEphemeral();
                }

                return new(savedId, published ? "newly_saved" : "reused");
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                // Telemetry must not fail the engine or log paths, file contents or exception text.
                return CreateEphemeral();
            }
            finally
            {
                if (ownsTemporaryFile && temporaryPath is not null)
                {
                    try
                    {
                        if (IsSafeDirectory(Path.GetDirectoryName(temporaryPath)!))
                        {
                            File.Delete(temporaryPath);
                        }
                    }
                    catch (Exception exception) when (IsStorageFailure(exception))
                    {
                        // Only this invocation's successfully created temporary file is eligible
                        // for cleanup. A crash or cleanup failure may leave this tiny file behind.
                    }
                }
            }
        }

        /// <summary>
        /// Returns false because persistent notice state is unavailable in this implementation.
        /// Does not read or write anything, even when opted out. The session must show the notice
        /// once per enabled run before its first transmission; false must not suppress that notice.
        /// </summary>
        public static bool TryMarkNoticeShown(string? configPath)
        {
            _ = configPath;
            return false;
        }

        private static EngineTelemetryIdentity CreateEphemeral() => new(Guid.NewGuid(), "ephemeral");

        private static bool TryGetStoragePaths(string? configPath, out string directory, out string sidecar)
        {
            directory = string.Empty;
            sidecar = string.Empty;
            if (string.IsNullOrWhiteSpace(configPath)
                || (!OperatingSystem.IsWindows()
                    && (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))))
            {
                return false;
            }

            // Reject remote/URI/device and ambiguous drive-relative inputs before path I/O.
            if (configPath.StartsWith(@"\\", StringComparison.Ordinal)
                || configPath.StartsWith("//", StringComparison.Ordinal)
                || configPath.Contains("://", StringComparison.Ordinal)
                || (Path.IsPathRooted(configPath) && !Path.IsPathFullyQualified(configPath)))
            {
                return false;
            }

            string fullPath = Path.GetFullPath(configPath);
            if (OperatingSystem.IsWindows())
            {
                // No UNC/device namespace, alternate data streams or mapped network drives.
                if (fullPath.StartsWith(@"\\", StringComparison.Ordinal) || fullPath.AsSpan(2).Contains(':'))
                {
                    return false;
                }

                DriveType driveType = new DriveInfo(Path.GetPathRoot(fullPath)!).DriveType;
                if (driveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Ram))
                {
                    return false;
                }
            }

            directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            if (directory.Length == 0
                || (OperatingSystem.IsLinux() && new DriveInfo(directory).DriveType is not (DriveType.Fixed or DriveType.Ram))
                || !IsSafeDirectory(directory))
            {
                return false;
            }

            // Configuration contents are never opened. An absent/late/in-memory configuration
            // has no safe persistent target, including when a relative filename was supplied.
            if (OperatingSystem.IsLinux())
            {
                if (!TryGetLinuxStatus(fullPath, out LinuxFileStatus status)
                    || (status.Mode & NativeMethods.FILE_TYPE_MASK) != NativeMethods.REGULAR_FILE)
                {
                    return false;
                }
            }
            else if ((File.GetAttributes(fullPath) & UNSAFE_FILE_ATTRIBUTES) != 0)
            {
                return false;
            }

            sidecar = fullPath + SIDECAR_SUFFIX;
            return true;
        }

        private static bool IsSafeDirectory(string path)
        {
            uint userId = OperatingSystem.IsLinux() ? NativeMethods.GetEffectiveUserId() : 0;
            bool immediateParent = true;
            for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            {
                if (OperatingSystem.IsLinux())
                {
                    if (!TryGetLinuxStatus(directory.FullName, out LinuxFileStatus status)
                        || (status.Mode & NativeMethods.FILE_TYPE_MASK) != NativeMethods.DIRECTORY
                        || (immediateParent ? status.UserId != userId : status.UserId != userId && status.UserId != 0))
                    {
                        return false;
                    }

                    UnixFileMode mode = (UnixFileMode)status.Mode;
                    if ((mode & UNSAFE_DIRECTORY_MODE) != 0
                        && (immediateParent || (mode & UnixFileMode.StickyBit) == 0))
                    {
                        return false;
                    }
                }
                else
                {
                    FileAttributes attributes = File.GetAttributes(directory.FullName);
                    if ((attributes & UNSAFE_FILE_ATTRIBUTES) != FileAttributes.Directory)
                    {
                        return false;
                    }
                }

                // A sticky ancestor such as /tmp is allowed, but never a shared-writable
                // immediate parent. All checks are metadata-only and do not repair permissions.
                immediateParent = false;
            }

            return true;
        }

        private static bool CanCreateInDirectory(string path)
        {
            // On Windows this bit is advisory rather than an ACL. Be conservative even when
            // the caller could technically write; actual ACL/volume failures are caught too.
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
            {
                return false;
            }

            if (!OperatingSystem.IsLinux())
            {
                // Windows ACLs and read-only volumes are enforced by CreateNew, not changed here.
                return true;
            }

            const UnixFileMode required = UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            return (File.GetUnixFileMode(path) & required) == required;
        }

        private static ReadResult ReadIdentity(string path, out Guid apiId)
        {
            apiId = default;
            if (OperatingSystem.IsLinux())
            {
                // Reject static special files before opening; the handle check below also
                // rejects replacements during the race between inspection and open.
                if (!TryGetLinuxStatus(path, out LinuxFileStatus status))
                {
                    return Marshal.GetLastPInvokeError() == 2 ? ReadResult.Missing : ReadResult.Unavailable;
                }

                if (!IsSafeLinuxIdentity(status))
                {
                    return ReadResult.Unavailable;
                }
            }

            using SafeFileHandle handle = OpenIdentityNoFollow(path, out bool missing);
            if (handle.IsInvalid)
            {
                return missing ? ReadResult.Missing : ReadResult.Unavailable;
            }

            if (OperatingSystem.IsLinux())
            {
                if (NativeMethods.StatHandle(handle, string.Empty, NativeMethods.AT_EMPTY_PATH,
                    NativeMethods.REQUIRED_STATUS, out LinuxFileStatus status) != 0
                    || (status.Mask & NativeMethods.REQUIRED_STATUS) != NativeMethods.REQUIRED_STATUS
                    || !IsSafeLinuxIdentity(status))
                {
                    return ReadResult.Unavailable;
                }
            }
            else if (NativeMethods.GetFileType(handle) != NativeMethods.DISK_FILE
                || (File.GetAttributes(handle) & UNSAFE_FILE_ATTRIBUTES) != 0)
            {
                return ReadResult.Unavailable;
            }

            // Disable stream read-ahead and cap actual bytes too, not only the initial length.
            // NONBLOCK + a regular-file check prevents a substituted Unix FIFO from blocking reads.
            using FileStream stream = new(handle, FileAccess.Read, bufferSize: 1, isAsync: false);
            long expectedLength = stream.Length;
            if (expectedLength is <= 0 or > MAX_STATE_BYTES)
            {
                return ReadResult.Unavailable;
            }

            byte[] bytes = new byte[MAX_STATE_BYTES + 1];
            int count = 0;
            while (count < bytes.Length)
            {
                int read = stream.Read(bytes, count, bytes.Length - count);
                if (read == 0)
                {
                    break;
                }

                count += read;
            }

            if (count != expectedLength || count > MAX_STATE_BYTES || stream.Length != expectedLength)
            {
                return ReadResult.Unavailable;
            }

            using JsonDocument document = JsonDocument.Parse(bytes.AsMemory(0, count), new() { MaxDepth = 2 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ReadResult.Unavailable;
            }

            bool hasVersion = false;
            bool hasId = false;
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("version") && !hasVersion
                    && property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetInt32(out int version) && version == SCHEMA_VERSION)
                {
                    hasVersion = true;
                }
                else if (property.NameEquals("apiId") && !hasId
                    && property.Value.ValueKind == JsonValueKind.String
                    && TryParseRandomId(property.Value.GetString(), out apiId))
                {
                    hasId = true;
                }
                else
                {
                    // Reject duplicate/unknown fields, unexpected kinds and future schemas.
                    return ReadResult.Unavailable;
                }
            }

            return hasVersion && hasId ? ReadResult.Valid : ReadResult.Unavailable;
        }

        private static bool TryParseRandomId(string? value, out Guid apiId)
        {
            // UUID v4/RFC variant only; a file cannot prove how the saved bytes were generated.
            apiId = default;
            return value is { Length: 36 } && value[14] == '4'
                && value[19] is '8' or '9' or 'a' or 'b' or 'A' or 'B'
                && Guid.TryParseExact(value, "D", out apiId);
        }

        private static bool IsSafeLinuxIdentity(LinuxFileStatus status)
        {
            return (status.Mode & NativeMethods.FILE_TYPE_MASK) == NativeMethods.REGULAR_FILE
                && status.UserId == NativeMethods.GetEffectiveUserId()
                && ((UnixFileMode)(status.Mode & 0xFFF) & ~PRIVATE_FILE_MODE) == 0
                && ((UnixFileMode)status.Mode & UnixFileMode.UserRead) != 0;
        }

        private static SafeFileHandle OpenIdentityNoFollow(string path, out bool missing)
        {
            SafeFileHandle handle;
            int error;
            if (OperatingSystem.IsWindows())
            {
                handle = NativeMethods.OpenWindowsFile(path, NativeMethods.GENERIC_READ, FileShare.Read,
                    IntPtr.Zero, FileMode.Open, NativeMethods.OPEN_REPARSE_POINT, IntPtr.Zero);
                error = Marshal.GetLastPInvokeError();
            }
            else
            {
                int descriptor = NativeMethods.OpenLinuxFile(path,
                    NativeMethods.O_NOFOLLOW | NativeMethods.O_NONBLOCK | NativeMethods.O_CLOEXEC);
                error = Marshal.GetLastPInvokeError();
                handle = new((IntPtr)descriptor, ownsHandle: true);
            }

            // ENOENT and ERROR_FILE_NOT_FOUND are both 2. Other failures never permit creation.
            missing = handle.IsInvalid && error == 2;
            return handle;
        }

        private static bool TryGetLinuxStatus(string path, out LinuxFileStatus status)
        {
            return NativeMethods.StatPath(NativeMethods.AT_FDCWD, path, NativeMethods.AT_SYMLINK_NOFOLLOW,
                NativeMethods.REQUIRED_STATUS, out status) == 0
                && (status.Mask & NativeMethods.REQUIRED_STATUS) == NativeMethods.REQUIRED_STATUS;
        }

        private static bool IsStorageFailure(Exception exception) => exception is IOException
            or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException
            or JsonException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException
            or BadImageFormatException or MarshalDirectiveException;

        private enum ReadResult
        {
            Missing,
            Valid,
            Unavailable
        }

        // Linux statx has a fixed, architecture-independent layout (unlike struct stat).
        [StructLayout(LayoutKind.Explicit, Size = 256)]
        private struct LinuxFileStatus
        {
            [FieldOffset(0)]
            public uint Mask;

            [FieldOffset(20)]
            public uint UserId;

            [FieldOffset(28)]
            public ushort Mode;
        }

        private static class NativeMethods
        {
            internal const uint GENERIC_READ = 0x80000000;
            internal const uint OPEN_REPARSE_POINT = 0x00200000;
            internal const uint DISK_FILE = 1;
            internal const int O_NOFOLLOW = 0x20000;
            internal const int O_NONBLOCK = 0x800;
            internal const int O_CLOEXEC = 0x80000;
            internal const int AT_FDCWD = -100;
            internal const int AT_SYMLINK_NOFOLLOW = 0x100;
            internal const int AT_EMPTY_PATH = 0x1000;
            internal const uint REQUIRED_STATUS = 0xB; // STATX_TYPE | STATX_MODE | STATX_UID
            internal const int FILE_TYPE_MASK = 0xF000;
            internal const int REGULAR_FILE = 0x8000;
            internal const int DIRECTORY = 0x4000;

            [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static extern SafeFileHandle OpenWindowsFile(string path, uint access, FileShare share,
                IntPtr securityAttributes, FileMode creation, uint flags, IntPtr template);

            [DllImport("kernel32.dll", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static extern uint GetFileType(SafeFileHandle handle);

            [DllImport("libc", EntryPoint = "open", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static extern int OpenLinuxFile([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

            [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static extern int StatPath(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
                int flags, uint mask, out LinuxFileStatus status);

            [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static extern int StatHandle(SafeFileHandle handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
                int flags, uint mask, out LinuxFileStatus status);

            [DllImport("libc", EntryPoint = "geteuid")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static extern uint GetEffectiveUserId();
        }
    }
}
