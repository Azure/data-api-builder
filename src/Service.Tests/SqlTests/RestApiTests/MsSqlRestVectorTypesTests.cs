// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Tests for SQL Server vector column support via REST endpoints (read and write).
    /// Verifies that vector columns are returned as JSON arrays of numbers via REST GET requests
    /// and can be inserted/updated/deleted via REST POST/PUT/PATCH/DELETE requests.
    /// This mirrors the pattern used for PostgreSQL array types in
    /// <see cref="Find.PostgreSqlRestArrayTypesTests"/>.
    /// NOTE: The vector data type requires SQL Server 2025 / Azure SQL.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlRestVectorTypesTests : SqlTestBase
    {
        private const string VECTOR_TYPE_REST_PATH = "api/VectorType";

        /// <summary>
        /// Tolerance used when comparing the single-precision components of a vector,
        /// since vector(N) stores 32-bit floats which may not round-trip exactly through JSON.
        /// </summary>
        private const double VECTOR_COMPONENT_DELTA = 0.0001;

        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        #region Read Tests

        [DataTestMethod]
        [DataRow(VECTOR_TYPE_REST_PATH, 7, new[] { 0.5f, 0.25f, 0.75f }, DisplayName = "GET for Vector data type")]
        [DataRow($"{VECTOR_TYPE_REST_PATH}/id/2", 1, new[] { 1.5f, -2.5f, 3.5f }, DisplayName = "GET for Vector data type by primary key")]
        [DataRow($"{VECTOR_TYPE_REST_PATH}/id/3", 1, null, DisplayName = "GET for Vector data type with null vector")]
        public async Task GetVectorTypeList(string vectorRestPath, int expectedItems, float[] expectedValues)
        {
            HttpResponseMessage response = await HttpClient.GetAsync(vectorRestPath);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            JsonElement root = JsonDocument.Parse(body).RootElement;
            JsonElement items = root.GetProperty("value");

            Assert.AreEqual(expectedItems, items.GetArrayLength(), $"Expected {expectedItems} items, got {items.GetArrayLength()}");

            // Records are ordered by the primary key ascending, so the first record is id = 1.
            JsonElement first = items[0];
            AssertVectorEquals(first.GetProperty("vector_data"), expectedValues);
        }

        /// <summary>
        /// GET /api/VectorType/id/7 - Verify that a vector using the maximum supported dimension count (1998)
        /// round-trips through REST and is returned with the correct number of dimensions.
        /// </summary>
        [TestMethod]
        public async Task GetVectorTypeWithMaxDimensions()
        {
            HttpResponseMessage response = await HttpClient.GetAsync($"{VECTOR_TYPE_REST_PATH}/id/7");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            JsonElement value = JsonDocument.Parse(body).RootElement.GetProperty("value")[0];

            Assert.AreEqual(7, value.GetProperty("id").GetInt32());

            JsonElement maxVector = value.GetProperty("vector_data_max");
            Assert.AreEqual(JsonValueKind.Array, maxVector.ValueKind, "Expected the maximum-dimension vector to be serialized as a JSON array.");
            Assert.AreEqual(1998, maxVector.GetArrayLength(), "Expected the maximum-dimension vector to have 1998 components.");

            int i = 1;
            foreach (JsonElement vectorVal in maxVector.EnumerateArray())
            {
                Assert.AreEqual(i, vectorVal.GetDouble(), VECTOR_COMPONENT_DELTA);
                i++;
            }
        }

        #endregion

        #region Write Tests

        /// <summary>
        /// POST /api/VectorType - Verify that a new record with a vector value can be inserted and is
        /// returned (and persisted) as a JSON array.
        /// </summary>
        [DataTestMethod]
        [DataRow("{ \"vector_data\": [0.125, 0.25, 0.5] }", new[] { 0.125f, 0.25f, 0.5f }, true, DisplayName = "Insert valid vector")]
        [DataRow("{ \"vector_data\": null }", null, true, DisplayName = "Insert valid null vector")]
        [DataRow("{ \"vector_data\": [5e-1, 2.5e-1, 7.5e-1] }", new[] { 0.5f, 0.25f, 0.75f }, true, DisplayName = "Insert valid vector with scientific notation")]
        [DataRow("{ \"vector_data\": [\"0.5\", \"0.25\", \"0.75\"] }", new[] { 0.5f, 0.25f, 0.75f }, true, DisplayName = "Insert valid vector with numbers as string values")]
        [DataRow("{ \"vector_data\": [1.25, 2.25, 3.25, 4.25] }", null, false, DisplayName = "Insert invalid vector with more dimensions than allowed")]
        [DataRow("{ \"vector_data\": [\"not\", \"a\", \"number\"] }", null, false, DisplayName = "Insert invalid vector with invalid values")]
        public async Task InsertVectorType(string requestBody, float[] expectedValue, bool expectedSuccess)
        {
            HttpResponseMessage postResponse = await HttpClient.PostAsync(
                VECTOR_TYPE_REST_PATH,
                new StringContent(requestBody, Encoding.UTF8, "application/json"));

            if (expectedSuccess)
            {
                Assert.AreEqual(HttpStatusCode.Created, postResponse.StatusCode);

                JsonElement postElement = JsonDocument.Parse(await postResponse.Content.ReadAsStringAsync())
                    .RootElement.GetProperty("value")[0];
                int newId = postElement.GetProperty("id").GetInt32();

                // Confirm the value was persisted by reading it back.
                JsonElement readBack = await GetRecordByIdAsync(newId);
                AssertVectorEquals(readBack.GetProperty("vector_data"), expectedValue);
                await DeleteVectorType(newId);
            }
            else
            {
                Assert.IsFalse(postResponse.IsSuccessStatusCode, "Expected that inserting vector should fail.");
            }
        }

        /// <summary>
        /// PUT Verify that an existing record's vector value is replaced (full update).
        /// </summary>
        [TestMethod]
        public async Task PutVectorType_Update()
        {
            // Change vector value
            float[] expected = new[] { 9.5f, 8.5f, 7.5f };
            string requestBody = "{ \"vector_data\": [9.5, 8.5, 7.5] }";

            HttpResponseMessage response = await HttpClient.PutAsync(
                $"{VECTOR_TYPE_REST_PATH}/id/4",
                new StringContent(requestBody, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            JsonElement updated = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
                .RootElement.GetProperty("value")[0];
            Assert.AreEqual(4, updated.GetProperty("id").GetInt32());

            JsonElement readBack = await GetRecordByIdAsync(4);
            AssertVectorEquals(readBack.GetProperty("vector_data"), expected);

            // Restore vector value to original
            expected = new[] { 1.0f, 2.0f, 3.0f };
            requestBody = "{ \"vector_data\": [1.0, 2.0, 3.0] }";

            HttpResponseMessage restoreResponse = await HttpClient.PutAsync(
                $"{VECTOR_TYPE_REST_PATH}/id/4",
                new StringContent(requestBody, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, restoreResponse.StatusCode);

            JsonElement restoreUpdated = JsonDocument.Parse(await restoreResponse.Content.ReadAsStringAsync())
                .RootElement.GetProperty("value")[0];
            Assert.AreEqual(4, restoreUpdated.GetProperty("id").GetInt32());

            JsonElement restoreReadBack = await GetRecordByIdAsync(4);
            AssertVectorEquals(restoreReadBack.GetProperty("vector_data"), expected);
        }

        /// <summary>
        /// PATCH Verify that an existing record's vector value is updated.
        /// </summary>
        [TestMethod]
        public async Task PatchVectorType_Update()
        {
            // Change vector value
            float[] expected = new[] { 1.25f, 2.25f, 3.25f };
            string requestBody = "{ \"vector_data\": [1.25, 2.25, 3.25] }";

            HttpResponseMessage response = await HttpClient.PatchAsync(
                $"{VECTOR_TYPE_REST_PATH}/id/4",
                new StringContent(requestBody, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            JsonElement updated = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
                .RootElement.GetProperty("value")[0];
            Assert.AreEqual(4, updated.GetProperty("id").GetInt32());

            JsonElement readBack = await GetRecordByIdAsync(4);
            AssertVectorEquals(readBack.GetProperty("vector_data"), expected);

            // Restore vector value to original
            expected = new[] { 1.0f, 2.0f, 3.0f };
            requestBody = "{ \"vector_data\": [1.0, 2.0, 3.0] }";

            HttpResponseMessage restoreResponse = await HttpClient.PutAsync(
                $"{VECTOR_TYPE_REST_PATH}/id/4",
                new StringContent(requestBody, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, restoreResponse.StatusCode);

            JsonElement restoreUpdated = JsonDocument.Parse(await restoreResponse.Content.ReadAsStringAsync())
                .RootElement.GetProperty("value")[0];
            Assert.AreEqual(4, restoreUpdated.GetProperty("id").GetInt32());

            JsonElement restoreReadBack = await GetRecordByIdAsync(4);
            AssertVectorEquals(restoreReadBack.GetProperty("vector_data"), expected);
        }

        #endregion

        #region Query Option Tests

        [DataTestMethod]
        [DataRow("?$filter=vector_data%20eq%201", DisplayName = "Fail GET with $filter on vector column")]
        [DataRow("?$orderby=vector_data ASC", DisplayName = "Fail GET with $orderby on vector column")]
        public async Task ArgumentsOnVectorColumnFail(string queryOptions)
        {
            HttpResponseMessage response = await HttpClient.GetAsync($"{VECTOR_TYPE_REST_PATH}{queryOptions}");
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, "Query options on vector columns should be rejected.");
        }

        /// <summary>
        /// GET /api/VectorType?$first=2&amp;$orderby=id - Verify that pagination works for an entity that has a
        /// vector column. The first request returns a page plus a nextLink, and issuing a second request using
        /// the $after token extracted from that nextLink also succeeds.
        /// </summary>
        [TestMethod]
        public async Task FindWithFirstThenAfterPaginationSucceedsVectorType()
        {
            // First page: limit to two records, ordered by primary key for a deterministic cursor.
            HttpResponseMessage firstPageResponse = await HttpClient.GetAsync($"{VECTOR_TYPE_REST_PATH}?$first=2&$orderby=id");
            Assert.AreEqual(HttpStatusCode.OK, firstPageResponse.StatusCode);

            JsonElement firstPageRoot = JsonDocument.Parse(await firstPageResponse.Content.ReadAsStringAsync()).RootElement;
            Assert.AreEqual(2, firstPageRoot.GetProperty("value").GetArrayLength(), "Expected the first page to contain exactly two records.");
            Assert.IsTrue(firstPageRoot.TryGetProperty("nextLink", out JsonElement nextLinkElement), "Expected a nextLink on the first page.");

            // Extract the $after token from the nextLink and use it to request the next page.
            string afterToken = ExtractAfterToken(nextLinkElement.GetString());
            Assert.IsFalse(string.IsNullOrEmpty(afterToken), "Expected a non-empty $after token in the nextLink.");

            HttpResponseMessage secondPageResponse = await HttpClient.GetAsync($"{VECTOR_TYPE_REST_PATH}?$first=2&$orderby=id&$after={afterToken}");
            Assert.AreEqual(HttpStatusCode.OK, secondPageResponse.StatusCode, "Expected the request using the $after token to succeed.");

            JsonElement secondPageRoot = JsonDocument.Parse(await secondPageResponse.Content.ReadAsStringAsync()).RootElement;
            Assert.IsTrue(secondPageRoot.GetProperty("value").GetArrayLength() >= 1, "Expected the second page to contain at least one record.");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// DELETE /api/VectorType/id/6 - Verify that a record with a vector column can be deleted and is
        /// no longer retrievable.
        /// </summary>
        private static async Task DeleteVectorType(int id)
        {
            HttpResponseMessage deleteResponse = await HttpClient.DeleteAsync($"{VECTOR_TYPE_REST_PATH}/id/{id}");
            Assert.AreEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        }

        /// <summary>
        /// Fetches a single VectorType record by its primary key and returns the record element.
        /// </summary>
        private static async Task<JsonElement> GetRecordByIdAsync(int id)
        {
            HttpResponseMessage response = await HttpClient.GetAsync($"{VECTOR_TYPE_REST_PATH}/id/{id}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(body).RootElement.GetProperty("value")[0].Clone();
        }

        /// <summary>
        /// Extracts the raw (URL-encoded) value of the $after query parameter from a pagination nextLink.
        /// The token is returned exactly as emitted by the engine so it can be replayed verbatim in a
        /// follow-up request without any re-encoding.
        /// </summary>
        private static string ExtractAfterToken(string nextLink)
        {
            if (string.IsNullOrEmpty(nextLink))
            {
                return string.Empty;
            }

            const string afterParam = "$after=";
            int afterIndex = nextLink.IndexOf(afterParam, StringComparison.Ordinal);
            if (afterIndex < 0)
            {
                return string.Empty;
            }

            string afterToken = nextLink.Substring(afterIndex + afterParam.Length);
            int ampersandIndex = afterToken.IndexOf('&');
            return ampersandIndex >= 0 ? afterToken.Substring(0, ampersandIndex) : afterToken;
        }

        /// <summary>
        /// Asserts that the given JSON element is an array whose components match the expected vector
        /// within <see cref="VECTOR_COMPONENT_DELTA"/>.
        /// </summary>
        private static void AssertVectorEquals(JsonElement actual, float[] expected)
        {
            if (expected == null)
            {
                Assert.AreEqual(JsonValueKind.Null, actual.ValueKind, "Expected a null vector, but got a non-null value.");
                return;
            }

            Assert.AreEqual(expected.Length, actual.GetArrayLength(), "Vector dimension mismatch.");

            int i = 0;
            foreach (JsonElement element in actual.EnumerateArray())
            {
                Assert.AreEqual(expected[i], element.GetDouble(), VECTOR_COMPONENT_DELTA, $"Vector component at expected {expected[i]} and got {element.GetDouble()}.");
                i++;
            }
        }

        #endregion
    }
}
