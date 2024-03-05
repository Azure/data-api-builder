// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Cli.Commands;
using CommandLine;
using NodaTime.Calendars;

namespace Cli
{
    public class ResultHandler
    {
        public static int RunInitAndReturnExitCode(InitOptions o)
        {
            Console.WriteLine("Success");
            int exitCode = 0;

            return exitCode;
        }

        public static int ProcessErrorsAndReturnExitCode(IEnumerable<Error> err)
        {
            /// System.CommandLine considers --help and --version as NonParsed Errors
            /// ref: https://github.com/commandlineparser/commandline/issues/630
            /// This is a workaround to make sure our app exits with exit code 0,
            /// when user does --help or --versions.
            /// dab --help -> ErrorType.HelpVerbRequestedError
            /// dab [command-name] --help -> ErrorType.HelpRequestedError
            /// dab --version -> ErrorType.VersionRequestedError

            bool isHelpOrVersionRequested = false;
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
