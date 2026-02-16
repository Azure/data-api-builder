// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.AspNetCore.TestHost;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace Azure.DataApiBuilder.Service.Tests.OpenApiIntegration
{
    // Defines helpers used to help generate an OpenApiDocument object which
    // can be validated in tests so that a constantly running DAB instance
    // isn't necessary.
    internal class OpenApiTestBootstrap
    {
        /// <summary>
        /// Bootstraps a test server instance using a runtime config file generated
        /// from the provided entity collection. The test server is only used to generate
        /// and return the OpenApiDocument for use this method's callers.
        /// </summary>
        /// <param name="runtimeEntities">Collection of entities to include in the config.</param>
        /// <param name="configFileName">Name of the config file to generate.</param>
        /// <param name="databaseEnvironment">Database environment identifier.</param>
        /// <returns>Generated OpenApiDocument</returns>
        internal static async Task<OpenApiDocument> GenerateOpenApiDocumentAsync(
            RuntimeEntities runtimeEntities,
            string configFileName,
            string databaseEnvironment)
        {
            TestHelper.SetupDatabaseEnvironment(databaseEnvironment);
            FileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            loader.TryLoadKnownConfig(out RuntimeConfig config);

            RuntimeConfig configWithCustomHostMode = config with
            {
                Runtime = config.Runtime with
                {
                    Host = config.Runtime?.Host with { Mode = HostMode.Production }
                },
                Entities = runtimeEntities
            };

            return await GenerateOpenApiDocumentAsyncInternal(configWithCustomHostMode, configFileName);
        }

        /// <summary>
        /// Bootstraps a test server instance using a runtime config file generated
        /// from the provided entity collection and REST options. The test server is only used to generate
        /// and return the OpenApiDocument for use this method's callers.
        /// </summary>
        /// <param name="runtimeEntities">Collection of entities to include in the config.</param>
        /// <param name="configFileName">Name of the config file to generate.</param>
        /// <param name="databaseEnvironment">Database environment identifier.</param>
        /// <param name="restOptions">REST runtime options to customize request-body-strict setting.</param>
        /// <returns>Generated OpenApiDocument</returns>
        internal static async Task<OpenApiDocument> GenerateOpenApiDocumentAsync(
            RuntimeEntities runtimeEntities,
            string configFileName,
            string databaseEnvironment,
            RestRuntimeOptions restOptions)
        {
            TestHelper.SetupDatabaseEnvironment(databaseEnvironment);
            FileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            loader.TryLoadKnownConfig(out RuntimeConfig config);

            RuntimeConfig configWithCustomHostMode = config with
            {
                Runtime = config.Runtime with
                {
                    Rest = restOptions,
                    Host = config.Runtime?.Host with { Mode = HostMode.Production }
                },
                Entities = runtimeEntities
            };

            return await GenerateOpenApiDocumentAsyncInternal(configWithCustomHostMode, configFileName);
        }

        /// <summary>
        /// Internal helper that performs the actual OpenAPI document generation.
        /// </summary>
        private static async Task<OpenApiDocument> GenerateOpenApiDocumentAsyncInternal(
            RuntimeConfig configWithCustomHostMode,
            string configFileName)
        {
            File.WriteAllText(configFileName, configWithCustomHostMode.ToJson());
            string[] args = new[]
            {
                $"--ConfigFileName={configFileName}"
            };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();
            {
                HttpRequestMessage request = new(HttpMethod.Get, "/api/openapi");

                HttpResponseMessage response = await client.SendAsync(request);
                Stream responseStream = await response.Content.ReadAsStreamAsync();

                // Read V3 as YAML
                OpenApiDocument openApiDocument = new OpenApiStreamReader().Read(responseStream, out OpenApiDiagnostic diagnostic);

                TestHelper.UnsetAllDABEnvironmentVariables();
                return openApiDocument;
            }
        }

        /// <summary>
        /// Creates basic permissions collection with the anonymous and authenticated roles
        /// where all actions are permitted.
        /// </summary>
        /// <returns>Array of EntityPermission objects.</returns>
        internal static EntityPermission[] CreateBasicPermissions()
        {
            List<EntityPermission> permissions = new()
            {
                new EntityPermission(Role: "anonymous", Actions: new EntityAction[]
                {
                    new(Action: EntityActionOperation.All, Fields: null, Policy: new())
                }),
                new EntityPermission(Role: "authenticated", Actions: new EntityAction[]
                {
                    new(Action: EntityActionOperation.All, Fields: null, Policy: new())
                })
            };

            return permissions.ToArray();
        }
    }
}
