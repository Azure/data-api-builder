// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions.TestingHelpers;
using System.Reflection;

namespace Cli.Tests;

internal static class FileSystemUtils
{
    public static MockFileSystem ProvisionMockFileSystem()
    {
        MockFileSystem fileSystem = new();

        fileSystem.AddFile(
            fileSystem.Path.Combine(
                fileSystem.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
                "dab.draft.schema.json"),
            new MockFileData("{ \"additionalProperties\": {\"version\": \"https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json\"} }"));

        return fileSystem;
    }
}
