// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Azure.DataApiBuilder.Config.Telemetry;
using Microsoft.Win32.SafeHandles;
using Path = System.IO.Path;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>A saved random installation identity, or unavailable; never an ephemeral identity.</summary>
    internal sealed record CliTelemetryInstallation(Guid? InstallationId, string Stability);

    /// <summary>
    /// Best-effort CLI installation identity in the OS user profile, independent of API identities.
    /// The caller must enforce enablement first. This store does not enable telemetry, emit first-run
    /// events, persist notices or log storage failures. Only the atomic publication winner is newly_saved.
    /// </summary>
    /// <remarks>
    /// The profileDirectory seam denotes the user profile ROOT, not the telemetry directory. The
    /// default is Environment.SpecialFolder.UserProfile, never the working directory. The profile
    /// must already exist, be local, link-free and owned by the current user. On Linux, the profile,
    /// .dab and telemetry directories must be owner-only (0700, or 0500 for read-only reuse), and
    /// state must be owner-readable and at most 0600. On Windows, profile/directories/state require
    /// current-user ownership and no applicable write/delete/ACL-change grants to identities other
    /// than that user, SYSTEM or Administrators. Null/unsupported ACLs fail closed. Native no-follow
    /// handles validate reads; no ACL or mode on an existing object is ever changed.
    ///
    /// Only missing .dab/telemetry directories and a missing installation.json can be created. State
    /// is bounded to 1 KiB and exactly v1 {version,installationId}. Unsafe, corrupt and future state
    /// is left untouched. Reset by deleting installation.json while affected processes are stopped;
    /// the next enabled run saves an unrelated ID, without changing any API sidecar. Opt-out is not
    /// reset. Unsupported platforms (including macOS and Linux without supported statx) return
    /// unavailable. As with engine state, this is not a sandbox against an owner replacing ancestors
    /// or mounts during a call; filesystem calls have no hard time limit or crash-durability guarantee.
    /// </remarks>
    internal static class CliTelemetryInstallationStore
    {
        internal static CliTelemetryInstallation Resolve(Func<string, string?>? readEnvironmentVariable = null,
            string? profileDirectory = null)
        {
            return TelemetryIdentityPersistence.ResolveInstallationId(
                readEnvironmentVariable ?? Environment.GetEnvironmentVariable, profileDirectory) is { } saved
                ? new(saved.Id, saved.Created ? "newly_saved" : "reused")
                : new(null, "unavailable");
        }
    }

    internal static partial class TelemetryIdentityPersistence
    {
        private const UnixFileMode PRIVATE_DIRECTORY_MODE = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        private const UnixFileMode READABLE_DIRECTORY_MODE = UnixFileMode.UserRead | UnixFileMode.UserExecute;

        internal static (Guid Id, bool Created)? ResolveInstallationId(Func<string, string?> readEnvironmentVariable,
            string? profileDirectory)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(readEnvironmentVariable);
                if (ProductTelemetryPolicy.IsOptedOut(readEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR)))
                {
                    return null;
                }

                // No profile discovery, path normalization, principal lookup or filesystem calls
                // precede opt-out. An explicitly empty test seam must NOT fall back to the real user.
                profileDirectory ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!TryGetProfileDirectory(profileDirectory, out string profile))
                {
                    return null;
                }

                string dabDirectory = Path.Combine(profile, ".dab");
                string directory = Path.Combine(dabDirectory, "telemetry");
                if (!EnsureInstallationDirectory(dabDirectory, profile, profile)
                    || !EnsureInstallationDirectory(directory, dabDirectory, profile))
                {
                    return null;
                }

                return ResolveState(directory, Path.Combine(directory, "installation.json"), "installationId",
                    allowCreate: true, () => IsSafeInstallationDirectory(directory, profile), requireWindowsOwner: true);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                return null;
            }
        }

        private static bool TryGetProfileDirectory(string profileDirectory, out string profile)
        {
            profile = string.Empty;
            if (string.IsNullOrWhiteSpace(profileDirectory)
                || (!OperatingSystem.IsWindows()
                    && (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64)))
                || !Path.IsPathFullyQualified(profileDirectory)
                || profileDirectory.StartsWith(@"\\", StringComparison.Ordinal)
                || profileDirectory.StartsWith("//", StringComparison.Ordinal)
                || profileDirectory.Contains("://", StringComparison.Ordinal))
            {
                return false;
            }

            // Do not normalize away a link-containing component or accept Win32 trimmed aliases.
            char[] separators = OperatingSystem.IsWindows() ? ['/', '\\'] : ['/'];
            foreach (string component in profileDirectory.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (component is "." or ".."
                    || (OperatingSystem.IsWindows() && (component.EndsWith(' ') || component.EndsWith('.'))))
                {
                    return false;
                }
            }

            profile = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profileDirectory));
            if (string.Equals(profile, Path.GetPathRoot(profile), OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return false;
            }

            if (OperatingSystem.IsWindows()
                && (profile.StartsWith(@"\\", StringComparison.Ordinal) || profile.AsSpan(2).Contains(':')
                    || new DriveInfo(Path.GetPathRoot(profile)!).DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Ram)))
            {
                return false;
            }

            return IsSafeInstallationDirectory(profile, profile);
        }

        private static bool EnsureInstallationDirectory(string path, string parent, string profile)
        {
            bool missing;
            if (OperatingSystem.IsLinux())
            {
                missing = !TryGetLinuxStatus(path, out _) && Marshal.GetLastPInvokeError() == 2;
            }
            else if (OperatingSystem.IsWindows())
            {
                using SafeFileHandle handle = OpenWindowsDirectory(path);
                int error = Marshal.GetLastPInvokeError();
                missing = handle.IsInvalid && error is 2 or 3;
            }
            else
            {
                return false;
            }

            if (!missing)
            {
                // Includes dangling links, special files, unreadable directories and foreign owners.
                return IsSafeInstallationDirectory(path, profile);
            }

            if (!IsSafeInstallationDirectory(parent, profile) || !CanCreateInDirectory(parent))
            {
                return false;
            }

            if (OperatingSystem.IsWindows())
            {
                if (!CreatePrivateWindowsDirectory(path))
                {
                    return false;
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                // A single mkdir, not recursive CreateDirectory: a disappearing profile/parent
                // must never be recreated. EEXIST permits only a subsequent safety check.
                if (NativeMethods.MakeDirectory(path, (uint)PRIVATE_DIRECTORY_MODE) != 0
                    && Marshal.GetLastPInvokeError() != 17)
                {
                    return false;
                }
            }

            // Another creator may have won. Validate the actual object rather than chmod/repair it.
            return IsSafeInstallationDirectory(path, profile);
        }

        private static bool IsSafeInstallationDirectory(string path, string profile)
        {
            if ((OperatingSystem.IsLinux() && new DriveInfo(path).DriveType is not (DriveType.Fixed or DriveType.Ram))
                || !IsSafeDirectory(path))
            {
                return false;
            }

            for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            {
                if (OperatingSystem.IsLinux())
                {
                    if (!TryGetLinuxStatus(directory.FullName, out LinuxFileStatus status)
                        || (status.Mode & NativeMethods.FILE_TYPE_MASK) != NativeMethods.DIRECTORY
                        || status.UserId != NativeMethods.GetEffectiveUserId()
                        || ((UnixFileMode)(status.Mode & 0xFFF) & ~PRIVATE_DIRECTORY_MODE) != 0
                        || ((UnixFileMode)status.Mode & READABLE_DIRECTORY_MODE) != READABLE_DIRECTORY_MODE)
                    {
                        return false;
                    }
                }
                else if (OperatingSystem.IsWindows())
                {
                    using SafeFileHandle handle = OpenWindowsDirectory(directory.FullName);
                    if (handle.IsInvalid || NativeMethods.GetFileType(handle) != NativeMethods.DISK_FILE
                        || (File.GetAttributes(handle) & UNSAFE_FILE_ATTRIBUTES) != FileAttributes.Directory
                        || !HasSafeWindowsOwnership(handle))
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

                if (string.Equals(directory.FullName, profile, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        [SupportedOSPlatform("windows")]
        private static SafeFileHandle OpenWindowsDirectory(string path)
        {
            return NativeMethods.OpenWindowsFile(path, NativeMethods.READ_CONTROL | NativeMethods.READ_ATTRIBUTES,
                FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open,
                NativeMethods.OPEN_REPARSE_POINT | NativeMethods.BACKUP_SEMANTICS, IntPtr.Zero);
        }

        [SupportedOSPlatform("windows")]
        private static bool HasSafeWindowsOwnership(SafeFileHandle handle)
        {
            const int MUTATION_RIGHTS = (int)(FileSystemRights.Write | FileSystemRights.Delete
                | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership)
                | 0x10000000 | 0x40000000; // GENERIC_ALL | GENERIC_WRITE, if not already mapped by the filesystem.
            // Query the opened object, not a FileInfo/path which could inspect a different target.
            uint result = NativeMethods.GetSecurityInfo(handle, 1, 0x5, // SE_FILE_OBJECT; OWNER | DACL
                out _, out _, out _, out _, out IntPtr descriptor);
            try
            {
                if (result != 0 || descriptor == IntPtr.Zero)
                {
                    return false;
                }

                uint length = NativeMethods.GetSecurityDescriptorLength(descriptor);
                if (length is < 20 or > 131072)
                {
                    return false;
                }

                byte[] bytes = new byte[(int)length];
                Marshal.Copy(descriptor, bytes, 0, bytes.Length);
                RawSecurityDescriptor security = new(bytes, 0);
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                SecurityIdentifier? user = identity.User;
                if (user is null || security.Owner is null || !user.Equals(security.Owner)
                    || (security.ControlFlags & ControlFlags.DiscretionaryAclPresent) == 0
                    || security.DiscretionaryAcl is null)
                {
                    return false;
                }

                foreach (GenericAce ace in security.DiscretionaryAcl)
                {
                    if (ace is not CommonAce rule || rule.IsCallback)
                    {
                        // Do not guess about conditional, object-specific or unknown ACEs.
                        return false;
                    }

                    if ((rule.AceFlags & AceFlags.InheritOnly) != 0)
                    {
                        // Not applicable to this object. Every existing child is checked itself,
                        // and newly created children have explicit protected, owner-only ACLs.
                        continue;
                    }

                    if (rule.AceQualifier is not (AceQualifier.AccessAllowed or AceQualifier.AccessDenied))
                    {
                        return false;
                    }

                    if (rule.AceQualifier == AceQualifier.AccessAllowed
                        && (rule.AccessMask & MUTATION_RIGHTS) != 0
                        && !user.Equals(rule.SecurityIdentifier)
                        && !rule.SecurityIdentifier.IsWellKnown(WellKnownSidType.LocalSystemSid)
                        && !rule.SecurityIdentifier.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid))
                    {
                        // Conservative even if a separate deny ACE might neutralize this allow.
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                if (descriptor != IntPtr.Zero)
                {
                    _ = NativeMethods.LocalFree(descriptor);
                }
            }
        }

        [SupportedOSPlatform("windows")]
        private static bool CreatePrivateWindowsDirectory(string path)
        {
            DirectorySecurity security = new();
            SetNewWindowsObjectSecurity(security, isDirectory: true);
            byte[] bytes = security.GetSecurityDescriptorBinaryForm();
            IntPtr descriptor = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, descriptor, bytes.Length);
                WindowsSecurityAttributes attributes = new()
                {
                    Length = Marshal.SizeOf<WindowsSecurityAttributes>(),
                    SecurityDescriptor = descriptor,
                    InheritHandle = 0
                };

                // Native CreateDirectory creates exactly one leaf and applies its ACL atomically.
                // ERROR_ALREADY_EXISTS is not success until the existing object is validated.
                return NativeMethods.CreateWindowsDirectory(path, in attributes)
                    || Marshal.GetLastPInvokeError() == 183;
            }
            finally
            {
                Marshal.FreeHGlobal(descriptor);
            }
        }

        [SupportedOSPlatform("windows")]
        private static FileStream CreatePrivateWindowsFile(string path)
        {
            FileSecurity security = new();
            SetNewWindowsObjectSecurity(security, isDirectory: false);
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None,
                MAX_STATE_BYTES, FileOptions.None, security);
        }

        [SupportedOSPlatform("windows")]
        private static void SetNewWindowsObjectSecurity(FileSystemSecurity security, bool isDirectory)
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier user = identity.User ?? throw new InvalidOperationException();
            security.SetOwner(user);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            InheritanceFlags inheritance = isDirectory ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
            foreach (SecurityIdentifier principal in new[]
            {
                user,
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
            })
            {
                security.AddAccessRule(new(principal, FileSystemRights.FullControl, inheritance,
                    PropagationFlags.None, AccessControlType.Allow));
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowsSecurityAttributes
        {
            public int Length;
            public IntPtr SecurityDescriptor;
            public int InheritHandle;
        }

        private static partial class NativeMethods
        {
            internal const uint READ_CONTROL = 0x00020000;
            internal const uint READ_ATTRIBUTES = 0x00000080;
            internal const uint BACKUP_SEMANTICS = 0x02000000;

            [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool CreateWindowsDirectory(string path, in WindowsSecurityAttributes attributes);

            [DllImport("libc", EntryPoint = "mkdir", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static extern int MakeDirectory([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

            [DllImport("advapi32.dll", ExactSpelling = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static extern uint GetSecurityInfo(SafeFileHandle handle, int objectType, uint securityInformation,
                out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);

            [DllImport("advapi32.dll", ExactSpelling = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static extern uint GetSecurityDescriptorLength(IntPtr descriptor);

            [DllImport("kernel32.dll", ExactSpelling = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static extern IntPtr LocalFree(IntPtr memory);
        }
    }
}
