// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Cli.Tests")]
namespace Cli.Constants
{
    internal class CliReturnCode
    {
        public const int SUCCESS = 0;
        public const int GENERAL_ERROR = -1;
    }
}
