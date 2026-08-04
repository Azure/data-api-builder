// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Tests for SQL Server native JSON column support via REST endpoints (read and write).
    /// DAB treats a JSON column exactly like a string: the raw JSON text is written from the
    /// request payload and read back as a JSON string (the MSSQL query builder casts the native
    /// json column to NVARCHAR(MAX) so FOR JSON PATH emits it as an escaped string rather than
    /// inlining it as a nested object). No new scalar or format is involved.
    /// Assertions compare the returned metadata semantically so they are robust to any
    /// whitespace / key-order normalization the engine applies to the JSON type.
    /// NOTE: The native JSON data type requires SQL Server 2025 / Azure SQL.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlRestJsonTypesTests : SqlTestBase
    {
        private const string JSON_TYPE_REST_PATH = "api/Profile";

        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        #region Read Tests

        /// <summary>
        /// GET /api/Profile - Verify the whole collection (5 seeded rows) is returned and that
        /// each metadata value renders either as a JSON string payload or null (row 5).
        /// </summary>
        [TestMethod]
        public async Task GetJsonTypeList()
        {
            HttpResponseMessage response = await HttpClient.GetAsync(JSON_TYPE_REST_PATH);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            JsonElement items = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
                .RootElement.GetProperty("value");
            Assert.AreEqual(5, items.GetArrayLength(), "Expected the 5 seeded profile rows.");

            // Rows 1-4 carry a JSON payload (returned as an escaped JSON string); row 5 is null.
            foreach (JsonElement record in items.EnumerateArray())
            {
                JsonValueKind metadataKind = record.GetProperty("metadata").ValueKind;
                Assert.IsTrue(
                    metadataKind is JsonValueKind.String or JsonValueKind.Null,
                    $"Expected metadata to be a JSON string or null, but was {metadataKind}.");
            }
        }

        /// <summary>
        /// GET /api/Profile/id/1 - Verify a simple object payload round-trips (verbatim value-equivalence).
        /// </summary>
        [TestMethod]
        public async Task GetJsonTypeByPrimaryKey()
        {
            JsonElement metadata = ParseMetadata(await GetRecordByIdAsync(1));
            Assert.AreEqual("admin", metadata.GetProperty("role").GetString());
            Assert.AreEqual(3, metadata.GetProperty("tier").GetInt32());
        }

        /// <summary>
        /// GET /api/Profile/id/5 - Verify a SQL NULL metadata value is rendered as JSON null.
        /// </summary>
        [TestMethod]
        public async Task GetJsonTypeWithNull()
        {
            JsonElement record = await GetRecordByIdAsync(5);
            Assert.AreEqual(JsonValueKind.Null, record.GetProperty("metadata").ValueKind);
        }

        /// <summary>
        /// GET /api/Profile/id/2 - Verify an array-bearing payload is preserved.
        /// </summary>
        [TestMethod]
        public async Task GetJsonTypeWithArrayPayload()
        {
            JsonElement metadata = ParseMetadata(await GetRecordByIdAsync(2));
            JsonElement tags = metadata.GetProperty("tags");
            Assert.AreEqual(JsonValueKind.Array, tags.ValueKind);
            Assert.AreEqual(3, tags.GetArrayLength());
            Assert.AreEqual("a", tags[0].GetString());
            Assert.AreEqual("b", tags[1].GetString());
            Assert.AreEqual("c", tags[2].GetString());
        }

        /// <summary>
        /// GET /api/Profile/id/3 - Verify a deeply nested object payload is preserved.
        /// </summary>
        [TestMethod]
        public async Task GetJsonTypeWithNestedPayload()
        {
            JsonElement metadata = ParseMetadata(await GetRecordByIdAsync(3));
            Assert.IsTrue(metadata.GetProperty("nested").GetProperty("key").GetProperty("deep").GetBoolean());
        }

        /// <summary>
        /// GET /api/Profile/id/4 - Verify unicode (including a multi-byte emoji) round-trips intact.
        /// </summary>
        [TestMethod]
        public async Task GetJsonTypeWithUnicode()
        {
            JsonElement metadata = ParseMetadata(await GetRecordByIdAsync(4));
            Assert.AreEqual("éü😀", metadata.GetProperty("unicode").GetString());
        }

        /// <summary>
        /// GET /api/Profile?$filter=metadata ne null - Verify filtering a json column (treated as a
        /// string) passes through to SQL: the 4 non-null rows match and the null row (id 5) does not.
        /// </summary>
        [TestMethod]
        public async Task FilterJsonColumnIsNotNull_Succeeds()
        {
            HttpResponseMessage response = await HttpClient.GetAsync($"{JSON_TYPE_REST_PATH}?$filter=metadata ne null");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "Filtering a json column as a string should pass through and succeed.");

            JsonElement items = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
                .RootElement.GetProperty("value");
            Assert.AreEqual(4, items.GetArrayLength(), "Only the 4 rows with non-null metadata should match.");
        }

        #endregion

        #region Write Tests

        /// <summary>
        /// POST /api/Profile - Verify a new record with a JSON payload can be inserted, the value
        /// echoes back, and it is persisted (read-back). Also covers inserting a null payload.
        /// </summary>
        [DataTestMethod]
        [DataRow("{ \"metadata\": \"{\\\"role\\\":\\\"guest\\\"}\" }", false, DisplayName = "Insert profile with valid JSON object")]
        [DataRow("{ \"metadata\": null }", true, DisplayName = "Insert profile with null metadata")]
        public async Task InsertJsonType(string requestBody, bool expectNull)
        {
            HttpResponseMessage postResponse = await HttpClient.PostAsync(
                JSON_TYPE_REST_PATH,
                new StringContent(requestBody, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.Created, postResponse.StatusCode);

            JsonElement postElement = JsonDocument.Parse(await postResponse.Content.ReadAsStringAsync())
                .RootElement.GetProperty("value")[0];
            int newId = postElement.GetProperty("id").GetInt32();

            JsonElement readBack = await GetRecordByIdAsync(newId);
            if (expectNull)
            {
                Assert.AreEqual(JsonValueKind.Null, readBack.GetProperty("metadata").ValueKind);
            }
            else
            {
                Assert.AreEqual("guest", ParseMetadata(readBack).GetProperty("role").GetString());
            }

            await DeleteProfile(newId);
        }

        /// <summary>
        /// PUT /api/Profile/id/1 - Verify a full update replaces the metadata payload, then restore it.
        /// </summary>
        [TestMethod]
        public async Task PutJsonType_Update()
        {
            try
            {
                HttpResponseMessage response = await HttpClient.PutAsync(
                    $"{JSON_TYPE_REST_PATH}/id/1",
                    new StringContent("{ \"metadata\": \"{\\\"role\\\":\\\"owner\\\"}\" }", Encoding.UTF8, "application/json"));
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.AreEqual("owner", ParseMetadata(await GetRecordByIdAsync(1)).GetProperty("role").GetString());
            }
            finally
            {
                // Restore original value so shared row 1 is left intact for other tests.
                HttpResponseMessage restore = await HttpClient.PutAsync(
                    $"{JSON_TYPE_REST_PATH}/id/1",
                    new StringContent("{ \"metadata\": \"{\\\"role\\\":\\\"admin\\\",\\\"tier\\\":3}\" }", Encoding.UTF8, "application/json"));
                Assert.AreEqual(HttpStatusCode.OK, restore.StatusCode);
            }
        }

        /// <summary>
        /// PATCH /api/Profile/id/1 - Verify a partial update sets a new metadata payload, then restore it.
        /// </summary>
        [TestMethod]
        public async Task PatchJsonType_Update()
        {
            try
            {
                HttpResponseMessage response = await HttpClient.PatchAsync(
                    $"{JSON_TYPE_REST_PATH}/id/1",
                    new StringContent("{ \"metadata\": \"{\\\"role\\\":\\\"editor\\\"}\" }", Encoding.UTF8, "application/json"));
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.AreEqual("editor", ParseMetadata(await GetRecordByIdAsync(1)).GetProperty("role").GetString());
            }
            finally
            {
                // Restore original value so shared row 1 is left intact for other tests.
                HttpResponseMessage restore = await HttpClient.PutAsync(
                    $"{JSON_TYPE_REST_PATH}/id/1",
                    new StringContent("{ \"metadata\": \"{\\\"role\\\":\\\"admin\\\",\\\"tier\\\":3}\" }", Encoding.UTF8, "application/json"));
                Assert.AreEqual(HttpStatusCode.OK, restore.StatusCode);
            }
        }

        /// <summary>
        /// PATCH /api/Profile/id/2 - Verify metadata can be cleared to null.
        /// </summary>
        [TestMethod]
        public async Task PatchJsonType_ToNull()
        {
            try
            {
                HttpResponseMessage response = await HttpClient.PatchAsync(
                    $"{JSON_TYPE_REST_PATH}/id/2",
                    new StringContent("{ \"metadata\": null }", Encoding.UTF8, "application/json"));
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.AreEqual(JsonValueKind.Null, (await GetRecordByIdAsync(2)).GetProperty("metadata").ValueKind);
            }
            finally
            {
                // Restore original array payload so shared row 2 is left intact for other tests.
                HttpResponseMessage restore = await HttpClient.PutAsync(
                    $"{JSON_TYPE_REST_PATH}/id/2",
                    new StringContent("{ \"metadata\": \"{\\\"tags\\\":[\\\"a\\\",\\\"b\\\",\\\"c\\\"]}\" }", Encoding.UTF8, "application/json"));
                Assert.AreEqual(HttpStatusCode.OK, restore.StatusCode);
            }
        }

        /// <summary>
        /// POST /api/Profile - Verify that supplying invalid JSON for the json column is rejected by
        /// SQL Server and surfaced as HTTP 400 (a client input error), not a 500. DAB treats the value
        /// as a normal string, so JSON validation happens at the database boundary.
        /// </summary>
        [DataTestMethod]
        [DataRow("{ \"metadata\": \"{ not valid json\" }", DisplayName = "Unclosed / unquoted object")]
        [DataRow("{ \"metadata\": \"{\\\"key\\\": }\" }", DisplayName = "Missing value")]
        public async Task InsertMalformedJson_ReturnsBadRequest(string requestBody)
        {
            HttpResponseMessage response = await HttpClient.PostAsync(
                JSON_TYPE_REST_PATH,
                new StringContent(requestBody, Encoding.UTF8, "application/json"));

            Assert.AreEqual(
                HttpStatusCode.BadRequest,
                response.StatusCode,
                "SQL Server rejects invalid JSON for a native json column; DAB must surface it as HTTP 400.");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// DELETE /api/Profile/id/{id} - Verify a record can be deleted.
        /// </summary>
        private static async Task DeleteProfile(int id)
        {
            HttpResponseMessage deleteResponse = await HttpClient.DeleteAsync($"{JSON_TYPE_REST_PATH}/id/{id}");
            Assert.AreEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        }

        /// <summary>
        /// Fetches a single Profile record by its primary key and returns the record element.
        /// </summary>
        private static async Task<JsonElement> GetRecordByIdAsync(int id)
        {
            HttpResponseMessage response = await HttpClient.GetAsync($"{JSON_TYPE_REST_PATH}/id/{id}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(body).RootElement.GetProperty("value")[0].Clone();
        }

        /// <summary>
        /// Returns the metadata field parsed as a JSON element. DAB treats a JSON column as a string,
        /// so a non-null metadata value arrives at the REST boundary as a JSON string carrying the
        /// payload. This helper asserts that and parses the string payload for semantic inspection.
        /// </summary>
        private static JsonElement ParseMetadata(JsonElement record)
        {
            JsonElement metadata = record.GetProperty("metadata");
            Assert.AreEqual(
                JsonValueKind.String,
                metadata.ValueKind,
                "A json column must be returned as a JSON string at the REST boundary (treated as a normal string).");

            return JsonDocument.Parse(metadata.GetString()!).RootElement.Clone();
        }

        #endregion
    }
}
