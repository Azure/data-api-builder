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
    /// Concurrent same-key PUT/PATCH regression coverage shared by relational providers whose REST
    /// mutation fixtures support upserts. Each request targets an initially missing composite key.
    /// </summary>
    [TestClass]
    public abstract class UpsertConcurrencyTestBase : RestApiTestBase
    {
        private const int CONCURRENT_REQUEST_COUNT = 4;
        private const int ITERATION_COUNT = 8;

        public override string GetQuery(string key)
        {
            return string.Empty;
        }

        protected abstract string GetRowCountQuery(int pieceId);

        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        /// <summary>
        /// Concurrent PUT upserts for one missing key must result in one insert and only updates after it.
        /// </summary>
        [TestMethod]
        public async Task ConcurrentPutOnSameMissingPrimaryKeyResolvesCleanly()
        {
            await RunConcurrentSameKeyUpsertsAsync(HttpMethod.Put, startingPieceId: 9000);
        }

        /// <summary>
        /// Concurrent insert-capable PATCH upserts for one missing key must result in one insert and only
        /// updates after it.
        /// </summary>
        [TestMethod]
        public async Task ConcurrentPatchOnSameMissingPrimaryKeyResolvesCleanly()
        {
            await RunConcurrentSameKeyUpsertsAsync(HttpMethod.Patch, startingPieceId: 9100);
        }

        private async Task RunConcurrentSameKeyUpsertsAsync(HttpMethod method, int startingPieceId)
        {
            for (int iteration = 0; iteration < ITERATION_COUNT; iteration++)
            {
                int pieceId = startingPieceId + iteration;
                string primaryKeyRoute = $"categoryid/0/pieceid/{pieceId}";
                Task<HttpResponseMessage>[] requests = Enumerable.Range(1, CONCURRENT_REQUEST_COUNT)
                    .Select(requestNumber => SendUpsertAsync(method, primaryKeyRoute, requestNumber))
                    .ToArray();

                HttpResponseMessage[] responses = await Task.WhenAll(requests);
                try
                {
                    foreach (HttpResponseMessage response in responses)
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        Assert.IsTrue(
                            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
                            $"{method} iteration {iteration} (pieceid {pieceId}) failed with " +
                            $"{(int)response.StatusCode} ({response.StatusCode}). Body: {responseBody}");
                    }

                    int createdCount = responses.Count(response => response.StatusCode == HttpStatusCode.Created);
                    int okCount = responses.Count(response => response.StatusCode == HttpStatusCode.OK);

                    Assert.AreEqual(
                        1,
                        createdCount,
                        $"{method} iteration {iteration} (pieceid {pieceId}): exactly one request should create the row.");
                    Assert.AreEqual(
                        CONCURRENT_REQUEST_COUNT - 1,
                        okCount,
                        $"{method} iteration {iteration} (pieceid {pieceId}): all requests after the insert should update the row.");

                    string rowCountJson = await GetDatabaseResultAsync(GetRowCountQuery(pieceId));
                    using JsonDocument rowCountDocument = JsonDocument.Parse(rowCountJson);
                    int rowCount = rowCountDocument.RootElement.GetProperty("cnt").GetInt32();

                    Assert.AreEqual(
                        1,
                        rowCount,
                        $"{method} iteration {iteration} (pieceid {pieceId}): exactly one logical row should remain.");
                }
                finally
                {
                    foreach (HttpResponseMessage response in responses)
                    {
                        response.Dispose();
                    }
                }
            }
        }

        private static Task<HttpResponseMessage> SendUpsertAsync(
            HttpMethod method,
            string primaryKeyRoute,
            int requestNumber)
        {
            string endpoint = $"api/{_Composite_NonAutoGenPK_EntityPath}/{primaryKeyRoute}";
            HttpRequestMessage request = new(method, endpoint)
            {
                Content = JsonContent.Create(new Dictionary<string, object>
                {
                    { "categoryName", "SciFi" },
                    { "piecesAvailable", requestNumber },
                    { "piecesRequired", requestNumber }
                })
            };

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
