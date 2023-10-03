// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Reflection;

namespace Azure.DataApiBuilder.Product;

public static class ProductInfo
{
    public const string DEFAULT_VERSION = "1.0.0";
    public const string DAB_APP_NAME_ENV = "DAB_APP_NAME_ENV";
    public static readonly string DEFAULT_APP_NAME = $"dab_oss_{ProductInfo.GetProductVersion()}";
    public static readonly string ROLE_NAME = "DataApiBuilder";

    /// <summary>
    /// Reads the product version from the executing assembly's file version information.
    /// </summary>
    /// <returns>Product version if not null, default version 1.0.0 otherwise.</returns>
    public static string GetProductVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
        string? version = fileVersionInfo.ProductVersion;

        return version ?? DEFAULT_VERSION;
    }

    /// <summary>
    /// It retrieves the user agent for the DataApiBuilder by checking the value of
    /// DAB_APP_NAME_ENV  environment variable. If the environment variable is not set,
    /// it returns a default value indicating connections from open source.
    /// The method serves as a means of identifying the source of connections made through the DataApiBuilder.
    /// </summary>
    public static string GetDataApiBuilderUserAgent()
    {
        return Environment.GetEnvironmentVariable(DAB_APP_NAME_ENV) ?? DEFAULT_APP_NAME;
    }
}

