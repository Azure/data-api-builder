// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Generator;
using Cli.Commands;
using HotChocolate.Utilities.Introspection;
using Microsoft.Extensions.Logging;
using static Cli.Utils;

namespace Cli
{
    internal static class Exporter
    {
        public static int Export(ExportOptions options, ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            StartOptions startOptions = new(false, LogLevel.None, false, options.Config!);

            CancellationTokenSource cancellationTokenSource = new();
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            if (!TryGetConfigFileBasedOnCliPrecedence(loader, options.Config, out string runtimeConfigFile))
            {
                logger.LogError("Failed to find the config file provided, check your options and try again.");
                return -1;
            }

            if (!loader.TryLoadConfig(
                    runtimeConfigFile,
                    out RuntimeConfig? runtimeConfig,
                    replaceEnvVar: true) || runtimeConfig is null)
            {
                logger.LogError($"Failed to read the config file: {runtimeConfigFile}.");
                return -1;
            }

            if (!options.Generate)
            {
                _ = Task.Run(() =>
                {
                    _ = ConfigGenerator.TryStartEngineWithOptions(startOptions, loader, fileSystem);
                }, cancellationToken);
            }

            bool isSuccess = false;
            if (options.GraphQL)
            {
                int retryCount = 5;
                int tries = 0;

                while (tries < retryCount)
                {
                    try
                    {
                        ExportGraphQL(options, runtimeConfig, fileSystem, logger).Wait();
                        isSuccess = true;
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
            return isSuccess ? 0 : -1;
        }

        private static async Task ExportGraphQL(ExportOptions options, RuntimeConfig runtimeConfig, System.IO.Abstractions.IFileSystem fileSystem, ILogger logger)
        {
            string schemaText;
            if (options.Generate)
            {
                logger.LogInformation("Generating schema from the CosmosDB database.");

                schemaText = await SchemaGeneratorFactory.Create(runtimeConfig,
                    options.SamplingMode,
                    options.NumberOfRecords,
                    options.PartitionKeyPath,
                    options.MaxDays,
                    options.GroupCount);
            }
            else
            {
                logger.LogInformation("Fetching schema from GraphQL API.");

                HttpClient client = new( // CodeQL[SM02185] Loading internal server connection
                                                        new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator }
                                                    )
                {
                    BaseAddress = new Uri($"https://localhost:5001{runtimeConfig.GraphQLPath}")
                };

                IntrospectionClient introspectionClient = new();
                Task<HotChocolate.Language.DocumentNode> response = introspectionClient.DownloadSchemaAsync(client);
                response.Wait();

                HotChocolate.Language.DocumentNode node = response.Result;

                schemaText = node.ToString();
            }

            WriteSchemaFile(options, fileSystem, schemaText);

            logger.LogInformation($"Schema file exported successfully at {options.OutputDirectory}");
        }

        private static void WriteSchemaFile(ExportOptions options, IFileSystem fileSystem, string content)
        {
            if (!fileSystem.Directory.Exists(options.OutputDirectory))
            {
                fileSystem.Directory.CreateDirectory(options.OutputDirectory);
            }

            string outputPath = fileSystem.Path.Combine(options.OutputDirectory, options.GraphQLSchemaFile);
            fileSystem.File.WriteAllText(outputPath, content);
        }
    }
}
