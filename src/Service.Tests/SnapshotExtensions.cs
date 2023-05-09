// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config;
using Snapshooter.MSTest;

namespace Azure.DataApiBuilder.Service.Tests;

internal static class SnapshotExtensions
{
    /// <summary>
    /// Performs a snapshot match on the given RuntimeConfig, while ignoring fields that we wouldn't want included
    /// in the output, such as the connection string.
    /// </summary>
    /// <param name="config"></param>
    public static void MatchSnapshot(this RuntimeConfig config) =>
        Snapshot.Match(
            config,
            options => options.ExcludeField("DataSource.ConnectionString").IgnoreField("DataSource.ConnectionString")
        );
}
