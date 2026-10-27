// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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
    /// Concurrent same-key PUT/PATCH upsert coverage for PostgreSQL.
    /// </summary>
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlUpsertConcurrencyTests : UpsertConcurrencyTestBase
    {
        private const string FIXED_WIDTH_KEY_ENTITY = "FixedWidthKeyUpsert";

        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture(
                customEntities: new List<string[]>
                {
                    new[] { FIXED_WIDTH_KEY_ENTITY, "fixed_width_key_upsert" }
                });
        }

        protected override string GetRowCountQuery(int pieceId)
        {
            return "SELECT json_build_object('cnt', COUNT(*)) AS data " +
                $"FROM {_Composite_NonAutoGenPK_TableName} " +
                $"WHERE categoryid = 0 AND pieceid = {pieceId}";
        }

        /// <summary>
        /// Values with different trailing-space representations compare equal for a character(n) key and
        /// must therefore be serialized as the same logical key.
        /// </summary>
        [TestMethod]
        public async Task ConcurrentUpsertsSerializeDatabaseEqualFixedWidthKeys()
        {
            for (int iteration = 0; iteration < 8; iteration++)
            {
                string key = $"K{iteration:D3}";
                string[] databaseEqualKeys = { key, key + " ", key + "  ", key + "   " };
                Task<HttpResponseMessage>[] requests = databaseEqualKeys
                    .Select((databaseEqualKey, index) => SendFixedWidthKeyUpsertAsync(databaseEqualKey, index + 1))
                    .ToArray();

                HttpResponseMessage[] responses = await Task.WhenAll(requests);
                try
                {
                    foreach (HttpResponseMessage response in responses)
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        Assert.IsTrue(
                            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
                            $"Fixed-width key upsert failed with {(int)response.StatusCode} " +
                            $"({response.StatusCode}). Body: {responseBody}");
                    }

                    Assert.AreEqual(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
                    Assert.AreEqual(databaseEqualKeys.Length - 1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));

                    string rowCountJson = await GetDatabaseResultAsync(
                        $"SELECT json_build_object('cnt', COUNT(*)) AS data FROM fixed_width_key_upsert WHERE id = '{key}'");
                    using JsonDocument rowCountDocument = JsonDocument.Parse(rowCountJson);
                    Assert.AreEqual(1, rowCountDocument.RootElement.GetProperty("cnt").GetInt32());
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

        private static Task<HttpResponseMessage> SendFixedWidthKeyUpsertAsync(string key, int value)
        {
            HttpRequestMessage request = new(
                HttpMethod.Put,
                $"api/{FIXED_WIDTH_KEY_ENTITY}/id/{Uri.EscapeDataString(key)}")
            {
                Content = JsonContent.Create(new Dictionary<string, object> { { "value", value } })
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
