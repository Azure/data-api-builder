// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config;
using HotChocolate.Utilities.Introspection;
using Microsoft.Extensions.Logging;
using static Cli.Utils;

namespace Cli
{
    internal static class Exporter
    {
        public static void Export(ExportOptions options, ILogger logger)
        {
            StartOptions startOptions = new(false, LogLevel.None, false, options.Config!);

            CancellationTokenSource cancellationTokenSource = new();
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            if (!TryGetConfigFileBasedOnCliPrecedence(options.Config, out string runtimeConfigFile))
            {
                logger.LogError("Failed to find the config file provided, check your options and try again.");
                return;
            }

            if (!TryReadRuntimeConfig(runtimeConfigFile, out string runtimeConfigJson))
            {
                logger.LogError("Failed to read the config file: {runtimeConfigFile}.", runtimeConfigFile);
                return;
            }

            if (!RuntimeConfig.TryGetDeserializedRuntimeConfig(runtimeConfigJson, out RuntimeConfig? runtimeConfig, logger))
            {
                logger.LogError("Failed to parse runtime config file: {runtimeConfigFile}", runtimeConfigFile);
                return;
            }

            Task server = Task.Run(() =>
            {
                _ = ConfigGenerator.TryStartEngineWithOptions(startOptions);
            }, cancellationToken);

            if (options.GraphQL)
            {
                int retryCount = 5;
                int tries = 0;

                while (tries < retryCount)
                {
                    try
                    {
                        ExportGraphQL(options, runtimeConfig);
                        break;
                    }
                    catch
                    {
                        tries++;
                    }
                }

                if (tries == retryCount)
                {
                    logger.LogError("Failed to export GraphQL schema.");
                }
            }

            cancellationTokenSource.Cancel();
        }

        private static void ExportGraphQL(ExportOptions options, RuntimeConfig runtimeConfig)
        {
            HttpClient client = new( // CodeQL[SM02185] Loading internal server connection
                                        new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator }
                                    )
            { BaseAddress = new Uri($"https://localhost:5001{runtimeConfig.GraphQLGlobalSettings.Path}") };

            IntrospectionClient introspectionClient = new();
            Task<HotChocolate.Language.DocumentNode> response = introspectionClient.DownloadSchemaAsync(client);
            response.Wait();

            HotChocolate.Language.DocumentNode node = response.Result;

            if (!Directory.Exists(options.OutputDirectory))
            {
                Directory.CreateDirectory(options.OutputDirectory);
            }

            string outputPath = Path.Combine(options.OutputDirectory, options.GraphQLSchemaFile);
            File.WriteAllText(outputPath, node.ToString());
        }
    }
}
