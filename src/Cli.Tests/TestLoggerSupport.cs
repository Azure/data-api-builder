// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests;

internal static class TestLoggerSupport
{
    public static ILoggerFactory ProvisionLoggerFactory() =>
        LoggerFactory.Create(builder =>
    {
        builder.AddConsole();
    });
}
