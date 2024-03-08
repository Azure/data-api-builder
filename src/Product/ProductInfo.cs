// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Reflection;

namespace Azure.DataApiBuilder.Product;

public static class ProductInfo
{
    public const string DAB_APP_NAME_ENV = "DAB_APP_NAME_ENV";
    public static readonly string DAB_USER_AGENT = $"dab_oss_{GetMajorMinorPatchVersion()}";
    public static readonly string DEFAULT_APP_NAME = $"dab_oss_{GetProductVersion()}";
    public static readonly string CLOUD_ROLE_NAME = "DataApiBuilder";

    /// <summary>
    /// Reads the product version from the executing assembly's file version information.
    /// Includes commit hash.
    /// </summary>
    /// <param name="includeCommitHash">True by default: the user agent string will include the commit hash.</param>
    /// <returns>Product version if not null, default version 0.0.0 otherwise.</returns>
    public static string GetProductVersion(bool includeCommitHash = true)
    {
        if (!includeCommitHash)
        {
            return GetMajorMinorPatchVersion();
        }
 
        Assembly assembly = Assembly.GetExecutingAssembly();
        FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
        string? version = fileVersionInfo.ProductVersion;

        return version ?? "DAB UNVERSIONED OSS";
    }

    /// <summary>
    /// Returns the Product version in Major.Minor.Patch format without a commit hash.
    /// FileVersionInfo.ProductBuildPart is used to represent the Patch version.
    /// FileVersionInfo is used to retrieve the version information from the executing assembly
    /// set by the Version property in Directory.Build.props
    /// </summary>
    /// <returns>Version string "Major.Minor.Patch"</returns>
    public static string GetMajorMinorPatchVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
        string version = fileVersionInfo.ProductMajorPart + "." + fileVersionInfo.ProductMinorPart + "." + fileVersionInfo.ProductBuildPart;
        return version;
    }

    /// <summary>
    /// It retrieves the user agent for the DataApiBuilder by checking the value of
    /// DAB_APP_NAME_ENV environment variable. If the environment variable is not set,
    /// it returns a default value indicating connections from open source.
    /// </summary>
    /// <param name="includeCommitHash">True by default: the user agent string will include the commit hash.</param>
    /// <returns></returns>
    public static string GetDataApiBuilderUserAgent(bool includeCommitHash = true)
    {
        if (includeCommitHash)
        {
            return Environment.GetEnvironmentVariable(DAB_APP_NAME_ENV) ?? DEFAULT_APP_NAME;
        }

        return Environment.GetEnvironmentVariable(DAB_APP_NAME_ENV) ?? DAB_USER_AGENT;
    }
}

