// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Core.AuthenticationHelpers.AppServiceAuthentication;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Concurrency regression coverage for the MySQL upsert (PUT) path. The insert-vs-update decision is
    /// made from a locking existence check (SELECT ... FOR UPDATE) which gap-locks a missing primary key,
    /// so two concurrent upserts for the same initially-absent PK are serialized rather than both
    /// attempting an insert. One request must create the record and the other must update it; neither may
    /// fail with a duplicate-key / database-operation error.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlUpsertConcurrencyTests : RestApiTestBase
    {
        private const string EntityPath = "commodities";

        #region Test Fixture Setup

        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MYSQL;
            await InitializeTestFixture();
        }

        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        #endregion

        public override string GetQuery(string key)
        {
            return string.Empty;
        }

        /// <summary>
        /// Two concurrent PUT requests targeting the same, initially-absent primary key must both succeed:
        /// exactly one inserts (201 Created) and the other updates (200 OK). Neither request may fail with a
        /// duplicate-key / database-operation error, which would occur if the insert-vs-update decision were
        /// based on an unlocked pre-count.
        /// </summary>
        [TestMethod]
        public async Task ConcurrentPutOnSameMissingPrimaryKeyResolvesCleanly()
        {
            // Primary key (0, 901) is absent at test start (not part of the seed data).
            // categoryName must reference an existing comics.categoryName value (foreign key).
            const string primaryKeyRoute = "categoryid/0/pieceid/901";
            string firstBody = @"{ ""categoryName"": ""SciFi"", ""piecesAvailable"": 1, ""piecesRequired"": 1 }";
            string secondBody = @"{ ""categoryName"": ""SciFi"", ""piecesAvailable"": 2, ""piecesRequired"": 2 }";

            // Issue both requests concurrently.
            Task<HttpResponseMessage> firstRequest = SendPutAsync(primaryKeyRoute, firstBody);
            Task<HttpResponseMessage> secondRequest = SendPutAsync(primaryKeyRoute, secondBody);

            HttpResponseMessage[] responses = await Task.WhenAll(firstRequest, secondRequest);

            foreach (HttpResponseMessage response in responses)
            {
                Assert.IsTrue(
                    response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
                    $"Expected 200 OK or 201 Created but received {(int)response.StatusCode} ({response.StatusCode}). " +
                    $"Body: {await response.Content.ReadAsStringAsync()}");
            }

            int createdCount = responses.Count(response => response.StatusCode == HttpStatusCode.Created);
            int okCount = responses.Count(response => response.StatusCode == HttpStatusCode.OK);

            Assert.AreEqual(1, createdCount, "Exactly one concurrent request should have created the record (201).");
            Assert.AreEqual(1, okCount, "Exactly one concurrent request should have updated the record (200).");
        }

        /// <summary>
        /// Builds and sends an authenticated PUT request to the commodities entity.
        /// </summary>
        private static Task<HttpResponseMessage> SendPutAsync(string primaryKeyRoute, string requestBody)
        {
            string endpoint = $"api/{EntityPath}/{primaryKeyRoute}";
            JsonElement requestBodyElement = JsonDocument.Parse(requestBody).RootElement.Clone();

            HttpRequestMessage request = new(HttpMethod.Put, endpoint)
            {
                Content = JsonContent.Create(requestBodyElement)
            };

            // The MySQL test configuration uses the AppService EasyAuth provider.
            request.Headers.Add(
                AuthenticationOptions.CLIENT_PRINCIPAL_HEADER,
                AuthTestHelper.CreateAppServiceEasyAuthToken(
                    roleClaimType: AuthenticationOptions.ROLE_CLAIM_TYPE,
                    additionalClaims: new List<AppServiceClaim>
                    {
                        new() { Typ = AuthenticationOptions.ROLE_CLAIM_TYPE, Val = "authenticated" }
                    }));
            request.Headers.Add(AuthorizationResolver.CLIENT_ROLE_HEADER, "authenticated");

            return HttpClient.SendAsync(request);
        }
    }
}
