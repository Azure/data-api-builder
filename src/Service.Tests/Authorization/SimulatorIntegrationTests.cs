// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization
{
    [TestClass]
    public class SimulatorIntegrationTests
    {
        private const string SIMULATOR_CONFIG = $"simulator-config.{TestCategory.MSSQL}.json";
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

        [TestCleanup]
        public void CleanupAfterEachTest()
        {
            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Tests REST and GraphQL requests against the engine when configured
        /// with the authentication simulator.
        /// - Ensures authentication errors (HTTP 401 Unauthorized) are not returned
        /// because the Simulator provider guarantees that all requests are authenticated
        /// and not necessarily authorized.
        /// - Test entity: Journal
        /// - The role AuthorizationHandlerTester has read permissions on Journal
        /// and the roles Anonymous and Authenticated do not have read permissions on Journal.
        /// - Validate that requests fails authorization (HTTP 403 Forbidden) if the clientRole
        /// used does not match role permissions, and succeed (HTTP 200 OK) otherwise.
        /// </summary>
        /// <param name="clientRole">Role for which the engine should evaluate authorization.</param>
        /// <param name="expectError">Whether an error is expected for the requests.</param>
        /// <param name="expectedStatusCode">Response's Http StatusCode.</param>
        /// <returns></returns>
        [TestCategory(TestCategory.MSSQL)]
        [DataTestMethod]
        [DataRow("Anonymous", true, HttpStatusCode.Forbidden, DisplayName = "Simulator - Anonymous role does not have proper permissions.")]
        [DataRow("Authenticated", true, HttpStatusCode.Forbidden, DisplayName = "Simulator - Authenticated but Authenticated role does not have proper permissions.")]
        [DataRow("authorizationHandlerTester", false, HttpStatusCode.OK, DisplayName = "Simulator - Successful access with role: AuthorizationHandlerTester")]
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

            HttpRequestMessage request = new(HttpMethod.Get, "api/Journal");
            request.Headers.Add(AuthorizationResolver.CLIENT_ROLE_HEADER, clientRole);
            HttpResponseMessage response = await _client.SendAsync(request);
            Assert.AreEqual(expectedStatusCode, response.StatusCode);
        }

        private static void SetupCustomRuntimeConfiguration()
        {
            TestHelper.SetupDatabaseEnvironment(TestCategory.MSSQL);
            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(TestHelper.GetRuntimeConfigLoader());
            RuntimeConfig config = configProvider.GetConfig();

            AuthenticationOptions AuthenticationOptions = new(Provider: AuthenticationOptions.SIMULATOR_AUTHENTICATION, null);
            RuntimeConfig configWithCustomHostMode = config
                with
            {
                Runtime = config.Runtime
                with
                {
                    Host = config.Runtime.Host
                with
                    { Authentication = AuthenticationOptions }
                }
            };

            File.WriteAllText(
                SIMULATOR_CONFIG,
                configWithCustomHostMode.ToJson());
        }
    }
}
