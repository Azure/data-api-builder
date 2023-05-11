// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Snapshooter;
using Snapshooter.Core;
using Snapshooter.Core.Serialization;
using Snapshooter.MSTest;

namespace Azure.DataApiBuilder.Service.Tests;

internal static class SnapshotExtensions
{
    /// <summary>
    /// Performs a snapshot match on the given RuntimeConfig, while ignoring fields that we wouldn't want included
    /// in the output, such as the connection string.
    /// </summary>
    /// <param name="config"></param>
    public static void MatchSnapshot(this RuntimeConfig config)
    {
        try
        {
            Snapshot.Match(
                config,
                options => options.ExcludeField("DataSource.ConnectionString").IgnoreField("DataSource.ConnectionString")
            );
        }
        catch (AssertFailedException ex)
        {
            SnapshotFullName fullName = Snapshot.FullName();

            SnapshotFileHandler fileHandler = new();

            string expected = fileHandler.ReadSnapshot(fullName);

            SnapshotSerializer snapshotSerializer = new(new GlobalSnapshotSettingsResolver());
            string actual = snapshotSerializer.SerializeObject(config);

            string diff = BasicDiffDisplay(expected, actual);

            throw new AssertFailedException($"Snapshot {fullName} did not match. Diff:{Environment.NewLine}{diff}{Environment.NewLine}Expected:{Environment.NewLine}{expected}{Environment.NewLine}Actual:{Environment.NewLine}{actual}", ex);
        }
    }

    private static string BasicDiffDisplay(string expected, string actual)
    {
        string[] expectedLines = expected.Split(Environment.NewLine);
        string[] actualLines = actual.Split("\n");

        List<string> diff = new();

        for (int i = 0; i < actualLines.Length; i++)
        {
            string line = "  ";

            if (i > expectedLines.Length - 1)
            {
                line = "+ ";
            }
            else if (expectedLines[i] != actualLines[i])
            {
                if (expectedLines.Length > actualLines.Length)
                {
                    line = "+ ";
                }
                else
                {
                    line = "- ";
                }
            }

            line += actualLines[i];

            diff.Add(line);
        }

        return string.Join(Environment.NewLine, diff);
    }
}
