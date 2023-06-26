// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Reflection;

namespace Microsoft.DataApiBuilder.Service
{
    public class Utils
    {
        public const string DEFAULT_VERSION = "1.0.0";

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
    }
}
