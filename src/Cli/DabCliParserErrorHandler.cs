// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CommandLine;

namespace Cli
{
    /// <summary>
    /// Processes errors that occur during parsing of CLI verbs (start, init, export, add, update, etc) and their arguments.
    /// </summary>
    public class DabCliParserErrorHandler
    {
        /// <summary>
        /// Processes errors accumulated by each parser in parser.ParseArguments<parsers>().
        /// For DAB CLI, this only includes scenarios where the user provides invalid DAB CLI input.
        /// e.g. incorrectly formed or missing options and parameters.
        /// Additionally, an error is tracked if the user uses:
        /// -> an unsupported CLI verb
        /// -> --help.
        /// -> --version
        /// </summary>
        /// <param name="err">Collection of Error objects collected by the CLI parser.</param>
        /// <returns>Return code: 0 when --help is used, otherwise -1.</returns>
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
