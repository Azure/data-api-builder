// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// DataSourceFiles is a record that contains a list of files defining the runtimeConfigs for multi-db scenario.
    /// </summary>
    /// <param name="SourceFiles"></param>
    public record DataSourceFiles(IEnumerable<string>? SourceFiles = null);
}
