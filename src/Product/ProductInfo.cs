// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Reflection;

namespace Azure.DataApiBuilder.Product;

public static class ProductInfo
{
    public const string DAB_APP_NAME_ENV = "DAB_APP_NAME_ENV";
    public static readonly string DAB_USER_AGENT = $"dab_oss_{GetProductVersion()}";
    public static readonly string CLOUD_ROLE_NAME = "DataApiBuilder";

    /// <summary>
    /// Returns the Product version in Major.Minor.Patch format without a commit hash.
    /// FileVersionInfo.ProductBuildPart is used to represent the Patch version.
    /// FileVersionInfo is used to retrieve the version information from the executing assembly
    /// set by the Version property in Directory.Build.props.
    /// FileVersionInfo.ProductVersion includes the commit hash.
    /// </summary>
    /// <param name="includeCommitHash">If true, returns the version string with the commit hash</param>
    /// <returns>Version string without commit hash: Major.Minor.Patch
    /// Version string with commit hash: Major.Minor.Patch+COMMIT_ID"</returns>
    public static string GetProductVersion(bool includeCommitHash = false)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(fileName: assembly.Location);

        string versionString;

        // fileVersionInfo's ProductVersion is nullable, while PoductMajorPart, ProductMinorPart, and ProductBuildPart are not.
        // if ProductVersion is null, the other properties will be 0 since they do not return null. 
        if (includeCommitHash && fileVersionInfo.ProductVersion is not null)
        {
            versionString = fileVersionInfo.ProductVersion;
        }
        else
        {
            versionString = fileVersionInfo.ProductMajorPart + "." + fileVersionInfo.ProductMinorPart + "." + fileVersionInfo.ProductBuildPart;
        }

        return versionString;
    }

    /// <summary>
    /// It retrieves the user agent for the DataApiBuilder by checking the value of
    /// DAB_APP_NAME_ENV environment variable. If the environment variable is not set,
    /// it returns a default value indicating connections from open source.
    /// </summary>
    /// <returns>Returns the value in the environment variable DAB_APP_NAME_ENV, when set.
    /// Otherwise, returns user agent string: dab_oss_Major.Minor.Patch</returns>
    public static string GetDataApiBuilderUserAgent()
    {
        return Environment.GetEnvironmentVariable(DAB_APP_NAME_ENV) ?? DAB_USER_AGENT;
    }
}

