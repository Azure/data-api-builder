// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;

namespace Cli.Tests;

internal static class FileSystemUtils
{
    public static MockFileSystem ProvisionMockFileSystem()
    {
        MockFileSystem fileSystem = new();

        // We need to have this file in the file system so that the schema can be resolved when we are
        // generating a new config file using the CLI. Since we're not using a "real" file system, we
        // need to add it here. See: https://github.com/Azure/data-api-builder/pull/1564#discussion_r1253806984
        fileSystem.AddFile(
            fileSystem.Path.Combine(
                fileSystem.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
                "dab.draft.schema.json"),
            new MockFileData("{ \"$id\": \"https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json\" }"));

        return fileSystem;
    }
}
