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
        /// exactly one inserts (201 Created) and the other updates (200 OK), exactly one row remains, and
        /// neither request fails with a 5xx (which would surface a duplicate-key or deadlock database error).
        /// The scenario is repeated over many distinct keys to expose the nondeterministic race, since HTTP
        /// requests cannot be paused at the exact transaction point to force overlap on a single key.
        /// </summary>
        [TestMethod]
        public async Task ConcurrentPutOnSameMissingPrimaryKeyResolvesCleanly()
        {
            // Number of times to repeat the race on distinct, initially-absent primary keys.
            const int iterations = 15;

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                // A distinct primary key (0, 900+iteration) that is absent at the start of each iteration
                // (not part of the seed data). categoryName references an existing comics.categoryName (FK).
                int pieceId = 900 + iteration;
                string primaryKeyRoute = $"categoryid/0/pieceid/{pieceId}";
                string firstBody = @"{ ""categoryName"": ""SciFi"", ""piecesAvailable"": 1, ""piecesRequired"": 1 }";
                string secondBody = @"{ ""categoryName"": ""SciFi"", ""piecesAvailable"": 2, ""piecesRequired"": 2 }";

                // Issue both requests concurrently (both started before awaiting to maximize overlap).
                Task<HttpResponseMessage> firstRequest = SendPutAsync(primaryKeyRoute, firstBody);
                Task<HttpResponseMessage> secondRequest = SendPutAsync(primaryKeyRoute, secondBody);

                HttpResponseMessage[] responses = await Task.WhenAll(firstRequest, secondRequest);

                foreach (HttpResponseMessage response in responses)
                {
                    // A 5xx here would indicate a duplicate-key / deadlock (error 1213) database-operation
                    // failure - the exact failure this fix must prevent.
                    Assert.IsTrue(
                        (int)response.StatusCode < 500,
                        $"Iteration {iteration} (pieceid {pieceId}): request failed with {(int)response.StatusCode} " +
                        $"({response.StatusCode}). Body: {await response.Content.ReadAsStringAsync()}");

                    Assert.IsTrue(
                        response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
                        $"Iteration {iteration} (pieceid {pieceId}): expected 200 OK or 201 Created but received " +
                        $"{(int)response.StatusCode} ({response.StatusCode}). Body: {await response.Content.ReadAsStringAsync()}");
                }

                int createdCount = responses.Count(response => response.StatusCode == HttpStatusCode.Created);
                int okCount = responses.Count(response => response.StatusCode == HttpStatusCode.OK);

                Assert.AreEqual(1, createdCount,
                    $"Iteration {iteration} (pieceid {pieceId}): exactly one concurrent request should have created the record (201).");
                Assert.AreEqual(1, okCount,
                    $"Iteration {iteration} (pieceid {pieceId}): exactly one concurrent request should have updated the record (200).");

                // Exactly one row must remain for the key.
                string rowCountJson = await GetDatabaseResultAsync(
                    "SELECT JSON_OBJECT('cnt', COUNT(*)) AS data FROM " + _Composite_NonAutoGenPK_TableName +
                    $" WHERE categoryid = 0 AND pieceid = {pieceId}");
                using JsonDocument rowCountDoc = JsonDocument.Parse(rowCountJson);
                int rowCount = rowCountDoc.RootElement.GetProperty("cnt").GetInt32();

                Assert.AreEqual(1, rowCount,
                    $"Iteration {iteration} (pieceid {pieceId}): exactly one row must remain for the key after the concurrent upserts.");
            }
        }

        /// <summary>
        /// Builds and sends an authenticated PUT request to the commodities entity.
        /// </summary>
        private static Task<HttpResponseMessage> SendPutAsync(string primaryKeyRoute, string requestBody)
        {
            string endpoint = $"api/{_Composite_NonAutoGenPK_EntityPath}/{primaryKeyRoute}";
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
