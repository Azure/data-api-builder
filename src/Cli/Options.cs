// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CommandLine;

namespace Cli
{
    /// <summary>
    /// Common options for all the commands
    /// </summary>
    public class Options
    {
        public Options(string? config)
        {
            Config = config;
        }

        [Option('c', "config", Required = false, HelpText = "Path to config file. " +
            "Defaults to 'dab-config.json' unless 'dab-config.<DAB_ENVIRONMENT>.json' exists," +
            " where DAB_ENVIRONMENT is an environment variable.")]
        public string? Config { get; }
    }
}
