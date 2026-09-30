// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Reflection;

namespace Azure.DataApiBuilder.Product;

public static class ProductInfo
{
    public const string DAB_APP_NAME_ENV = "DAB_APP_NAME_ENV";
    public const string COSMOSDB_DATABASE_NAME = "COSMOSDB_DATABASE_NAME";

    /// <summary>
    /// Prefix that identifies a DAB open-source user agent / telemetry block. Kept as a separate
    /// constant so consumers (e.g. Application Name telemetry decoding) can locate the block.
    /// </summary>
    public const string DAB_USER_AGENT_MARKER = "dab_oss_";
    public static readonly string DAB_USER_AGENT = $"{DAB_USER_AGENT_MARKER}{GetProductVersion()}";

    /// <summary>
    /// Marker shared by the open-source (<c>dab_oss_</c>) and hosted (<c>dab_hosted_</c>) telemetry
    /// Application Name blocks. Decoding keys off this common prefix so both scenarios are recognized.
    /// </summary>
    public const string DAB_MARKER_PREFIX = "dab_";

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

    /// <summary>
    /// Returns the marker + version that the telemetry payload is appended to, based on the hosting
    /// scenario: <c>dab_oss_&lt;version&gt;</c> for open source, or <c>&lt;DAB_APP_NAME_ENV&gt;_&lt;version&gt;</c>
    /// (e.g. <c>dab_hosted_&lt;version&gt;</c>) when <c>DAB_APP_NAME_ENV</c> is set. In the hosted case the
    /// <c>dab_oss_</c> marker is not present; <see cref="DAB_MARKER_PREFIX"/> is the shared marker.
    /// </summary>
    public static string GetTelemetryApplicationNameBase()
    {
        string? label = Environment.GetEnvironmentVariable(DAB_APP_NAME_ENV);
        if (string.IsNullOrWhiteSpace(label))
        {
            return DAB_USER_AGENT;
        }

        string marker = label.EndsWith("_", StringComparison.Ordinal) ? label : label + "_";
        return $"{marker}{GetProductVersion()}";
    }
}

