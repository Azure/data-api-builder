#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Tests.Configuration;
using HotChocolate;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLIntrospectionTests
{
    [TestClass]
    public class GraphQLSchemaIntrospectionTests
    {
        /// <summary>
        /// Validates that schema introspection requests fail when allow-introspection is false in the runtime configuration.
        /// </summary>
        /// <seealso cref="https://github.com/ChilliCream/hotchocolate/blob/6b2cfc94695cb65e2f68f5d8deb576e48397a98a/src/HotChocolate/Core/src/Abstractions/ErrorCodes.cs#L287"/>
        /// <returns></returns>
        [DataTestMethod]
        [DataRow(false, true, "Introspection is not allowed for the current request.", DisplayName = "Disabled introspection returns GraphQL error.")]
        [DataRow(true, false, null, DisplayName = "Enabled introspection does not return introspection forbidden error.")]

        public async Task TestSchemaIntrospectionQuery(bool enableIntrospection, bool expectError, string? errorMessage)
        {
            Dictionary<GlobalSettingsType, object> settings = new()
            {
                { GlobalSettingsType.GraphQL, JsonSerializer.SerializeToElement(new GraphQLGlobalSettings(){ AllowIntrospection = enableIntrospection }) }
            };

            DataSource dataSource = new(DatabaseType.mssql)
            {
                ConnectionString = ConfigurationTests.GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL)
            };

            RuntimeConfig configuration = InitMinimalRuntimeConfig(globalSettings: settings, dataSource: dataSource);
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(
                CUSTOM_CONFIG,
                JsonSerializer.Serialize(configuration, RuntimeConfig.SerializerOptions));

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            TestServer server = new(Program.CreateWebHostBuilder(args));

            HttpClient client = server.CreateClient();

            string graphQLQueryName = "__schema";
            string graphQLQuery = @"{
                __schema {
                    types {
                        name
                    }
                }
            }";

            string expectedErrorMessageFragment = "Introspection is not allowed for the current request.";

            try
            {
                RuntimeConfigProvider configProvider = server.Services.GetRequiredService<RuntimeConfigProvider>();

                JsonElement actual = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                    client,
                    configProvider,
                    query: graphQLQuery,
                    queryName: graphQLQueryName,
                    variables: null,
                    clientRoleHeader: null
                    );

                if (expectError)
                {
                    SqlTestHelper.TestForErrorInGraphQLResponse(
                        response: actual.ToString(),
                        message: expectedErrorMessageFragment,
                        statusCode: ErrorCodes.Validation.IntrospectionNotAllowed
                    );
                }
            }
            catch (Exception ex)
            {
                // ExecuteGraphQLRequestAsync will raise an exception when no "data" key
                // exists in the GraphQL JSON response.
                Assert.Fail(message: "No schema metadata in GraphQL response." + ex.Message);
            }
        }

        /// <summary>
        /// Instantiate minimal runtime config with custom allow-introspection flag
        /// in the GraphQL global settings.
        /// </summary>
        /// <param name="globalSettings">Settings containing global GraphQL config.</param>
        /// <param name="dataSource">DataSource to pull connectionstring required for engine start.</param>
        /// <returns></returns>
        public static RuntimeConfig InitMinimalRuntimeConfig(Dictionary<GlobalSettingsType, object> globalSettings, DataSource dataSource)
        {
            PermissionOperation actionForRole = new(
                Name: Operation.All,
                Fields: null,
                Policy: new(request: null, database: null)
                );

            PermissionSetting permissionForEntity = new(
                role: "Anonymous",
                operations: new object[] { JsonSerializer.SerializeToElement(actionForRole) }
                );

            Entity sampleEntity = new(
                Source: JsonSerializer.SerializeToElement("books"),
                Rest: null,
                GraphQL: null,
                Permissions: new PermissionSetting[] { permissionForEntity },
                Relationships: null,
                Mappings: null
                );

            Dictionary<string, Entity> entityMap = new()
            {
                { "Book", sampleEntity }
            };

            RuntimeConfig runtimeConfig = new(
                Schema: "IntegrationTestMinimalSchema",
                MsSql: null,
                CosmosDb: null,
                PostgreSql: null,
                MySql: null,
                DataSource: dataSource,
                RuntimeSettings: globalSettings,
                Entities: entityMap
                );

            runtimeConfig.DetermineGlobalSettings();
            return runtimeConfig;
        }
    }
}
