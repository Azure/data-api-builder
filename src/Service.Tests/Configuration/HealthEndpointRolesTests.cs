// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Configuration
{
    [TestClass]
    public class HealthEndpointRolesTests
    {
        private const string STARTUP_CONFIG_ROLE = "authenticated";

        private const string CUSTOM_CONFIG_FILENAME = "custom-config.json";

        [TestCleanup]
        public void CleanupAfterEachTest()
        {
            if (File.Exists(CUSTOM_CONFIG_FILENAME))
            {
                File.Delete(CUSTOM_CONFIG_FILENAME);
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(null, null, DisplayName = "Validate Health Report when roles is not configured and HostMode is null.")]
        [DataRow(null, HostMode.Development, DisplayName = "Validate Health Report when roles is not configured and HostMode is Development.")]
        [DataRow(null, HostMode.Production, DisplayName = "Validate Health Report when roles is not configured and HostMode is Production.")]
        [DataRow("authenticated", HostMode.Production, DisplayName = "Validate Health Report when roles is configured to 'authenticated' and HostMode is Production.")]
        [DataRow("temp-role", HostMode.Production, DisplayName = "Validate Health Report when roles is configured to 'temp-role' which is not in token and HostMode is Production.")]
        [DataRow("authenticated", HostMode.Development, DisplayName = "Validate Health Report when roles is configured to 'authenticated' and HostMode is Development.")]
        [DataRow("temp-role", HostMode.Development, DisplayName = "Validate Health Report when roles is configured to 'temp-role' which is not in token and HostMode is Development.")]
        public async Task ComprehensiveHealthEndpoint_RolesTests(string role, HostMode hostMode)
        {
            // Arrange
            // At least one entity is required in the runtime config for the engine to start.
            // Even though this entity is not under test, it must be supplied enable successful
            // config file creation.
            Entity requiredEntity = new(
                Health: new(enabled: true),
                Source: new("books", EntitySourceType.Table, null, null),
                Rest: new(Enabled: true),
                GraphQL: new("book", "books", true),
                Permissions: new[] { ConfigurationTests.GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null);

            Dictionary<string, Entity> entityMap = new()
            {
                { "Book", requiredEntity }
            };

            CreateCustomConfigFile(entityMap, role, hostMode);

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                // Sends a GET request to a protected entity which requires a specific role to access.
                // Authorization checks
                HttpRequestMessage message = new(method: HttpMethod.Get, requestUri: $"/health");
                string swaTokenPayload = AuthTestHelper.CreateStaticWebAppsEasyAuthToken(
                    addAuthenticated: true,
                    specificRole: STARTUP_CONFIG_ROLE);
                message.Headers.Add(AuthenticationOptions.CLIENT_PRINCIPAL_HEADER, swaTokenPayload);
                message.Headers.Add(AuthorizationResolver.CLIENT_ROLE_HEADER, STARTUP_CONFIG_ROLE);
                HttpResponseMessage authorizedResponse = await client.SendAsync(message);

                switch (role)
                {
                    case null:
                        if (hostMode == HostMode.Development)
                        {
                            Assert.AreEqual(expected: HttpStatusCode.OK, actual: authorizedResponse.StatusCode);
                        }
                        else
                        {
                            Assert.AreEqual(expected: HttpStatusCode.Forbidden, actual: authorizedResponse.StatusCode);
                        }

                        break;
                    case "temp-role":
                        Assert.AreEqual(expected: HttpStatusCode.Forbidden, actual: authorizedResponse.StatusCode);
                        break;

                    default:
                        Assert.AreEqual(expected: HttpStatusCode.OK, actual: authorizedResponse.StatusCode);
                        break;
                }
            }
        }

        /// <summary>
        /// Helper function to write custom configuration file with minimal REST/GraphQL global settings
        /// using the supplied entities.
        /// </summary>
        /// <param name="entityMap">Collection of entityName -> Entity object.</param>
        /// <param name="role">Allowed Roles for comprehensive health endpoint.</param>
        private static void CreateCustomConfigFile(Dictionary<string, Entity> entityMap, string? role, HostMode hostMode = HostMode.Production)
        {
            DataSource dataSource = new(
                DatabaseType.MSSQL,
                ConfigurationTests.GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL),
                Options: null,
                Health: new(true));
            HostOptions hostOptions = new(Mode: hostMode, Cors: null, Authentication: new() { Provider = nameof(EasyAuthType.StaticWebApps) });

            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: dataSource,
                Runtime: new(
                    Health: new(enabled: true, roles: role != null ? new HashSet<string> { role } : null),
                    Rest: new(Enabled: true),
                    GraphQL: new(Enabled: true),
                    Host: hostOptions
                ),
                Entities: new(entityMap));

            File.WriteAllText(
                path: CUSTOM_CONFIG_FILENAME,
                contents: runtimeConfig.ToJson());
        }
    }
}
