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
        /// <param name="runtimeEntities"></param>
        /// <param name="configFileName"></param>
        /// <param name="databaseEnvironment"></param>
        /// <param name="requestBodyStrict">Optional value for request-body-strict setting. If null, uses default (true).</param>
        /// <param name="role">Optional role to filter OpenAPI document. If null, returns superset of all roles.</param>
        /// <returns>Generated OpenApiDocument</returns>
        internal static async Task<OpenApiDocument> GenerateOpenApiDocumentAsync(
            RuntimeEntities runtimeEntities,
            string configFileName,
            string databaseEnvironment,
            bool? requestBodyStrict = null,
            string role = null)
        {
            TestHelper.SetupDatabaseEnvironment(databaseEnvironment);
            FileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            loader.TryLoadKnownConfig(out RuntimeConfig config);

            // Create Rest options with the specified request-body-strict setting
            RestRuntimeOptions restOptions = requestBodyStrict.HasValue
                ? config.Runtime?.Rest with { RequestBodyStrict = requestBodyStrict.Value } ?? new RestRuntimeOptions(RequestBodyStrict: requestBodyStrict.Value)
                : config.Runtime?.Rest ?? new RestRuntimeOptions();

            RuntimeConfig configWithCustomHostMode = config with
            {
                Runtime = config.Runtime with
                {
                    Host = config.Runtime?.Host with { Mode = HostMode.Development },
                    Rest = restOptions
                },
                Entities = runtimeEntities
            };

            File.WriteAllText(configFileName, configWithCustomHostMode.ToJson());
            string[] args = new[]
            {
                $"--ConfigFileName={configFileName}"
            };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();
            {
                string requestUrl = role is null ? "/api/openapi" : $"/api/openapi/{role}";
                HttpRequestMessage request = new(HttpMethod.Get, requestUrl);

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
