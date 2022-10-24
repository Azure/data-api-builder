using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization
{
    [TestClass]
    public class SimulatorIntegrationTests
    {
        private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;
        private const string SIMULATOR_CONFIG = "simulator-config.json";
        private static TestServer _server;
        private static HttpClient _client;

        /// <summary>
        /// Generate and write the custom simulator runtime config file.
        /// Instantiate server and client one time for all tests because
        /// the tests only validate how the engine responds to requests
        /// and not variations in configuration settings.
        /// </summary>
        [ClassInitialize]
        public static void SetupAsync(TestContext context)
        {
            SetupCustomRuntimeConfiguration();
            string[] args = new[]
            {
                $"--ConfigFileName={SIMULATOR_CONFIG}"
            };

            _server = new(Program.CreateWebHostBuilder(args));
            _client = _server.CreateClient();
        }

        /// <summary>
        /// Tests REST and GraphQL requests against the engine when configured
        /// with the authentication simulator.
        /// Validate that requests receive authorization failures if the clientRole
        /// used does not match role permissions, and succeed otherwise.
        /// Authentication errors (HTTP 401 Unauthorized) are not expected since the
        /// simulator authenticates all requests.
        /// </summary>
        /// <param name="clientRole">Role for which the engine should evaluate authorization.</param>
        /// <param name="expectError">Whether an error is expected for the requests.</param>
        /// <param name="expectedStatusCode">Response's Http StatusCode.</param>
        /// <returns></returns>
        [TestCategory(TestCategory.MSSQL)]
        [DataTestMethod]
        [DataRow("Authenticated", true, HttpStatusCode.Forbidden, DisplayName = "Simulator - Authenticated")]
        [DataRow("AuthorizationHandlerTester", false, HttpStatusCode.OK, DisplayName = "Simulator - Successful access with role: AuthorizationHandlerTester")]
        public async Task TestSimulatorRequests(string clientRole, bool expectError, HttpStatusCode expectedStatusCode)
        {
            string graphQLQueryName = "journal_by_pk";
            string graphQLQuery = @"{
                journal_by_pk(id: 1) {
                    id,
                    journalname 
                }
                }";
            string expectedResult = @"{ ""id"":1,""journalname"":""Journal1""}";

            JsonElement actual = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                _client,
                configProvider: _server.Services.GetRequiredService<RuntimeConfigProvider>(),
                query: graphQLQuery,
                queryName: graphQLQueryName,
                variables: null,
                clientRoleHeader: clientRole
                );

            if (expectError)
            {
                SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "The current user is not authorized to access this resource.",
                    path: @"[""journal_by_pk""]"
                );
            }
            else
            {
                SqlTestHelper.PerformTestEqualJsonStrings(expectedResult, actual.ToString());
            }

            // Validate that REST request:
            // - Succeed with 200 OK
            // - Fail with 403 Forbidden
            HttpRequestMessage request = new(HttpMethod.Get, "api/Journal");
            request.Headers.Add(AuthorizationResolver.CLIENT_ROLE_HEADER, clientRole);
            HttpResponseMessage response = await _client.SendAsync(request);
            Assert.AreEqual(expectedStatusCode, response.StatusCode);
        }

        private static void SetupCustomRuntimeConfiguration()
        {
            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(MSSQL_ENVIRONMENT);
            RuntimeConfig config = configProvider.GetRuntimeConfiguration();

            AuthenticationConfig authenticationConfig = new(Provider: SimulatorType.Simulator.ToString());
            HostGlobalSettings customHostGlobalSettings = config.HostGlobalSettings with { Authentication = authenticationConfig };
            JsonElement serializedCustomHostGlobalSettings =
                JsonSerializer.SerializeToElement(customHostGlobalSettings, RuntimeConfig.SerializerOptions);

            Dictionary<GlobalSettingsType, object> customRuntimeSettings = new(config.RuntimeSettings);
            customRuntimeSettings.Remove(GlobalSettingsType.Host);
            customRuntimeSettings.Add(GlobalSettingsType.Host, serializedCustomHostGlobalSettings);

            RuntimeConfig configWithCustomHostMode =
                config with { RuntimeSettings = customRuntimeSettings };

            File.WriteAllText(
                SIMULATOR_CONFIG,
                JsonSerializer.Serialize(configWithCustomHostMode, RuntimeConfig.SerializerOptions));
        }
    }
}
