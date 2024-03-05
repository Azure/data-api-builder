// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CommandLine;

namespace Cli
{
    public class ResultHandler
    {
        public static int ProcessErrorsAndReturnExitCode(IEnumerable<Error> err)
        {
            // To know if `--help` or `--version` was requested.
            bool isHelpOrVersionRequested = false;

            /// System.CommandLine considers --help and --version as NonParsed Errors
            /// ref: https://github.com/commandlineparser/commandline/issues/630
            /// This is a workaround to make sure our app exits with exit code 0,
            /// when user does --help or --versions.
            /// dab --help -> ErrorType.HelpVerbRequestedError
            /// dab [command-name] --help -> ErrorType.HelpRequestedError
            /// dab --version -> ErrorType.VersionRequestedError
            List<Error> errors = err.ToList();
            if (errors.Any(e => e.Tag == ErrorType.VersionRequestedError
                                || e.Tag == ErrorType.HelpRequestedError
                                || e.Tag == ErrorType.HelpVerbRequestedError))
            {
                isHelpOrVersionRequested = true;
            }

            return isHelpOrVersionRequested ? 0 : -1;
        }
    }
}
