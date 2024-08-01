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
    /// <summary>
    /// Provides functionality for exporting GraphQL schemas, either by generating from a Azure Cosmos DB database or fetching from a GraphQL API.
    /// </summary>
    internal static class Exporter
    {
        /// <summary>
        /// Exports the GraphQL schema to a file based on the provided options.
        /// </summary>
        /// <param name="options">The options for exporting, including output directory, schema file name, and other settings.</param>
        /// <param name="logger">The logger instance for logging information and errors.</param>
        /// <param name="loader">The loader for runtime configuration files.</param>
        /// <param name="fileSystem">The file system abstraction for handling file operations.</param>
        /// <returns>Returns 0 if the export is successful, otherwise returns -1.</returns>
        public static int Export(ExportOptions options, ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            StartOptions startOptions = new(false, LogLevel.None, false, options.Config!);

            CancellationTokenSource cancellationTokenSource = new();
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            // Attempt to locate the runtime configuration file based on CLI options
            if (!TryGetConfigFileBasedOnCliPrecedence(loader, options.Config, out string runtimeConfigFile))
            {
                logger.LogError("Failed to find the config file provided, check your options and try again.");
                return -1;
            }

            // Load the runtime configuration from the file
            if (!loader.TryLoadConfig(
                    runtimeConfigFile,
                    out RuntimeConfig? runtimeConfig,
                    replaceEnvVar: true) || runtimeConfig is null)
            {
                logger.LogError("Failed to read the config file: {0}.", runtimeConfigFile);
                return -1;
            }

            // Do not retry if schema generation logic is running
            int retryCount = 1;

            // If schema generation is not required, start the GraphQL engine
            if (!options.Generate)
            {
                _ = Task.Run(() =>
                {
                    _ = ConfigGenerator.TryStartEngineWithOptions(startOptions, loader, fileSystem);
                }, cancellationToken);

                retryCount = 5; // Increase retry count if not generating schema
            }

            bool isSuccess = false;
            if (options.GraphQL)
            {
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

        /// <summary>
        /// Exports the GraphQL schema either by generating it from a Azure Cosmos DB database or fetching it from a GraphQL API.
        /// </summary>
        /// <param name="options">The options for exporting, including sampling mode and schema file name.</param>
        /// <param name="runtimeConfig">The runtime configuration for the export process.</param>
        /// <param name="fileSystem">The file system abstraction for handling file operations.</param>
        /// <param name="logger">The logger instance for logging information and errors.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private static async Task ExportGraphQL(ExportOptions options, RuntimeConfig runtimeConfig, System.IO.Abstractions.IFileSystem fileSystem, ILogger logger)
        {
            string schemaText;
            if (options.Generate)
            {
                // Generate the schema from Azure Cosmos DB database
                logger.LogInformation("Generating schema from the Azure Cosmos DB database using {0}", options.SamplingMode);
                try
                {
                    schemaText = await SchemaGeneratorFactory.Create(runtimeConfig,
                      options.SamplingMode,
                      options.NumberOfRecords,
                      options.PartitionKeyPath,
                      options.MaxDays,
                      options.GroupCount,
                      logger);
                }
                catch (Exception e)
                {
                    logger.LogError("Failed to generate schema from Azure Cosmos DB database: {0}", e.Message);
                    logger.LogDebug(e.StackTrace);
                    return;
                }
            }
            else
            {
                // Fetch the schema from the GraphQL API
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

            // Write the schema content to a file
            WriteSchemaFile(options, fileSystem, schemaText);

            logger.LogInformation("Schema file exported successfully at {0}", options.OutputDirectory);
        }

        /// <summary>
        /// Writes the generated schema to a file in the specified output directory.
        /// </summary>
        /// <param name="options">The options containing the output directory and schema file name.</param>
        /// <param name="fileSystem">The file system abstraction for handling file operations.</param>
        /// <param name="content">The schema content to be written to the file.</param>
        private static void WriteSchemaFile(ExportOptions options, IFileSystem fileSystem, string content)
        {
            // Ensure the output directory exists
            if (!fileSystem.Directory.Exists(options.OutputDirectory))
            {
                fileSystem.Directory.CreateDirectory(options.OutputDirectory);
            }

            // Construct the path for the schema file and write the content to it
            string outputPath = fileSystem.Path.Combine(options.OutputDirectory, options.GraphQLSchemaFile);
            fileSystem.File.WriteAllText(outputPath, content);
        }
    }
}
