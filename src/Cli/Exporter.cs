// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Generator;
using Cli.Commands;
using HotChocolate.Utilities.Introspection;
using Microsoft.Extensions.Logging;
using static Cli.Utils;

// This assembly is used to create dynamic proxy objects at runtime for the purpose of mocking dependencies in the tests.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
namespace Cli
{
    /// <summary>
    /// Provides functionality for exporting GraphQL schemas, either by generating from a Azure Cosmos DB database or fetching from a GraphQL API.
    /// </summary>
    internal class Exporter
    {
        private const int COSMOS_DB_RETRY_COUNT = 1;
        private const int DAB_SERVICE_RETRY_COUNT = 5;

        private static readonly CancellationTokenSource _cancellationTokenSource = new();
        private static readonly CancellationToken _cancellationToken = _cancellationTokenSource.Token;

        /// <summary>
        /// Exports the GraphQL schema to a file based on the provided options.
        /// </summary>
        /// <param name="options">The options for exporting, including output directory, schema file name, and other settings.</param>
        /// <param name="logger">The logger instance for logging information and errors.</param>
        /// <param name="loader">The loader for runtime configuration files.</param>
        /// <param name="fileSystem">The file system abstraction for handling file operations.</param>
        /// <returns>Returns 0 if the export is successful, otherwise returns -1.</returns>
        public static bool Export(ExportOptions options, ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            // Attempt to locate the runtime configuration file based on CLI options
            if (!TryGetConfigFileBasedOnCliPrecedence(loader, options.Config, out string runtimeConfigFile))
            {
                logger.LogError("Failed to find the config file provided, check your options and try again.");
                return false;
            }

            // Load the runtime configuration from the file
            if (!loader.TryLoadConfig(
                    runtimeConfigFile,
                    out RuntimeConfig? runtimeConfig,
                    replaceEnvVar: true) || runtimeConfig is null)
            {
                logger.LogError("Failed to read the config file: {0}.", runtimeConfigFile);
                return false;
            }

            // Do not retry if schema generation logic is running
            int retryCount = options.Generate ? COSMOS_DB_RETRY_COUNT : DAB_SERVICE_RETRY_COUNT;

            bool isSuccess = false;
            if (options.GraphQL)
            {
                int tries = 0;

                while (tries < retryCount)
                {
                    try
                    {
                        ExportGraphQL(options, runtimeConfig, fileSystem, loader, logger).Wait();
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
            else
            {
                logger.LogError("Exporting GraphQL schema is not enabled. You need to pass --graphql.");
            }

            _cancellationTokenSource.Cancel();
            return isSuccess;
        }

        /// <summary>
        /// Exports the GraphQL schema either by generating it from a Azure Cosmos DB database or fetching it from a GraphQL API.
        /// </summary>
        /// <param name="options">The options for exporting, including sampling mode and schema file name.</param>
        /// <param name="runtimeConfig">The runtime configuration for the export process.</param>
        /// <param name="fileSystem">The file system abstraction for handling file operations.</param>
        /// <param name="logger">The logger instance for logging information and errors.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private static async Task ExportGraphQL(ExportOptions options, RuntimeConfig runtimeConfig, System.IO.Abstractions.IFileSystem fileSystem, FileSystemRuntimeConfigLoader loader, ILogger logger)
        {
            string schemaText;
            if (options.Generate)
            {
                schemaText = await ExportGraphQLFromCosmosDB(options, runtimeConfig, logger);
            }
            else
            {
                StartOptions startOptions = new(false, LogLevel.None, false, options.Config!);

                Task dabService = Task.Run(() =>
                {
                    _ = ConfigGenerator.TryStartEngineWithOptions(startOptions, loader, fileSystem);
                }, _cancellationToken);

                Exporter exporter = new();
                schemaText = exporter.ExportGraphQLFromDabService(runtimeConfig, logger);
            }

            if (string.IsNullOrEmpty(schemaText))
            {
                logger.LogError("Generated GraphQL schema is empty. Please ensure data is available to generate the schema.");
                return;
            }

            // Write the schema content to a file
            WriteSchemaFile(options, fileSystem, schemaText, logger);

            logger.LogInformation("Schema file exported successfully at {0}", options.OutputDirectory);
        }

        /// <summary>
        /// Fetches the GraphQL schema from the DAB service, attempting to use the HTTPS endpoint first.
        /// If the HTTPS endpoint fails, it falls back to using the HTTP endpoint.
        /// Logs the process of fetching the schema and any fallback actions taken.
        /// </summary>
        /// <param name="runtimeConfig">The runtime config object containing the GraphQL path.</param>
        /// <param name="logger">The logger instance used to log information and errors during the schema fetching process.</param>
        /// <returns>The GraphQL schema as a string.</returns>
        internal string ExportGraphQLFromDabService(RuntimeConfig runtimeConfig, ILogger logger)
        {
            string schemaText;
            // Fetch the schema from the GraphQL API
            logger.LogInformation("Fetching schema from GraphQL API.");

            try
            {
                logger.LogInformation("Trying to fetch schema from DAB Service using HTTPS endpoint.");
                schemaText = GetGraphQLSchema(runtimeConfig, useFallbackURL: false);
            }
            catch
            {
                logger.LogInformation("Failed to fetch schema from DAB Service using HTTPS endpoint. Trying with HTTP endpoint.");
                schemaText = GetGraphQLSchema(runtimeConfig, useFallbackURL: true);
            }

            return schemaText;
        }

        /// <summary>
        /// Retrieves the GraphQL schema from the DAB service using either the HTTPS or HTTP endpoint based on the specified fallback option.
        /// </summary>
        /// <param name="runtimeConfig">The runtime configuration containing the GraphQL path and other settings.</param>
        /// <param name="useFallbackURL">A boolean flag indicating whether to use the fallback HTTP endpoint. If false, the method attempts to use the HTTPS endpoint.</param>
        internal virtual string GetGraphQLSchema(RuntimeConfig runtimeConfig, bool useFallbackURL = false)
        {
            HttpClient client;
            if (!useFallbackURL)
            {
                client = new( // CodeQL[SM02185] Loading internal server connection
                                                    new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator }
                                                )
                {
                    BaseAddress = new Uri($"https://localhost:5001{runtimeConfig.GraphQLPath}")
                };
            }
            else
            {
                client = new()
                {
                    BaseAddress = new Uri($"http://localhost:5000{runtimeConfig.GraphQLPath}")
                };
            }

            IntrospectionClient introspectionClient = new();
            Task<HotChocolate.Language.DocumentNode> response = introspectionClient.DownloadSchemaAsync(client);
            response.Wait();

            HotChocolate.Language.DocumentNode node = response.Result;

            return node.ToString();
        }

        private static async Task<string> ExportGraphQLFromCosmosDB(ExportOptions options, RuntimeConfig runtimeConfig, ILogger logger)
        {
            // Generate the schema from Azure Cosmos DB database
            logger.LogInformation("Generating schema from the Azure Cosmos DB database using {0}", options.SamplingMode);
            try
            {
                return await SchemaGeneratorFactory.Create(runtimeConfig,
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
                return string.Empty;
            }
        }

        /// <summary>
        /// Writes the generated schema to a file in the specified output directory.
        /// </summary>
        /// <param name="options">The options containing the output directory and schema file name.</param>
        /// <param name="fileSystem">The file system abstraction for handling file operations.</param>
        /// <param name="content">The schema content to be written to the file.</param>
        private static void WriteSchemaFile(ExportOptions options, IFileSystem fileSystem, string content, ILogger logger)
        {

            if (string.IsNullOrEmpty(content))
            {
                logger.LogError("There is nothing to write");
                return;
            }

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
