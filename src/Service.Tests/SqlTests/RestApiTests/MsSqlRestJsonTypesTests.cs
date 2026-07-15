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
    /// DAB does nothing special for a JSON column - it is written from the request payload and
    /// read back with no custom handling, new scalar, or format. On read, SQL Server's
    /// FOR JSON PATH projection (which DAB uses to shape every result) inlines a native json
    /// column as a nested JSON value, so the metadata surfaces at the REST boundary as a JSON
    /// object rather than an escaped string. Writes still supply the payload as a JSON string,
    /// exactly as a normal string column would be written.
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
        /// each metadata value renders either as a native JSON object payload or null (row 5).
        /// </summary>
        [TestMethod]
        public async Task GetJsonTypeList()
        {
            HttpResponseMessage response = await HttpClient.GetAsync(JSON_TYPE_REST_PATH);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            JsonElement items = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
                .RootElement.GetProperty("value");
            Assert.AreEqual(5, items.GetArrayLength(), "Expected the 5 seeded profile rows.");

            // Rows 1-4 carry a JSON payload (inlined as a native JSON object); row 5 is null.
            foreach (JsonElement record in items.EnumerateArray())
            {
                JsonValueKind metadataKind = record.GetProperty("metadata").ValueKind;
                Assert.IsTrue(
                    metadataKind is JsonValueKind.Object or JsonValueKind.Null,
                    $"Expected metadata to be a JSON object or null, but was {metadataKind}.");
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
            HttpResponseMessage response = await HttpClient.PutAsync(
                $"{JSON_TYPE_REST_PATH}/id/1",
                new StringContent("{ \"metadata\": \"{\\\"role\\\":\\\"owner\\\"}\" }", Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("owner", ParseMetadata(await GetRecordByIdAsync(1)).GetProperty("role").GetString());

            // Restore original value.
            HttpResponseMessage restore = await HttpClient.PutAsync(
                $"{JSON_TYPE_REST_PATH}/id/1",
                new StringContent("{ \"metadata\": \"{\\\"role\\\":\\\"admin\\\",\\\"tier\\\":3}\" }", Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, restore.StatusCode);
            Assert.AreEqual("admin", ParseMetadata(await GetRecordByIdAsync(1)).GetProperty("role").GetString());
        }

        /// <summary>
        /// PATCH /api/Profile/id/1 - Verify a partial update sets a new metadata payload, then restore it.
        /// </summary>
        [TestMethod]
        public async Task PatchJsonType_Update()
        {
            HttpResponseMessage response = await HttpClient.PatchAsync(
                $"{JSON_TYPE_REST_PATH}/id/1",
                new StringContent("{ \"metadata\": \"{\\\"role\\\":\\\"editor\\\"}\" }", Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("editor", ParseMetadata(await GetRecordByIdAsync(1)).GetProperty("role").GetString());

            // Restore original value.
            HttpResponseMessage restore = await HttpClient.PutAsync(
                $"{JSON_TYPE_REST_PATH}/id/1",
                new StringContent("{ \"metadata\": \"{\\\"role\\\":\\\"admin\\\",\\\"tier\\\":3}\" }", Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, restore.StatusCode);
        }

        /// <summary>
        /// PATCH /api/Profile/id/2 - Verify metadata can be cleared to null.
        /// </summary>
        [TestMethod]
        public async Task PatchJsonType_ToNull()
        {
            HttpResponseMessage response = await HttpClient.PatchAsync(
                $"{JSON_TYPE_REST_PATH}/id/2",
                new StringContent("{ \"metadata\": null }", Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(JsonValueKind.Null, (await GetRecordByIdAsync(2)).GetProperty("metadata").ValueKind);

            // Restore original array payload.
            HttpResponseMessage restore = await HttpClient.PutAsync(
                $"{JSON_TYPE_REST_PATH}/id/2",
                new StringContent("{ \"metadata\": \"{\\\"tags\\\":[\\\"a\\\",\\\"b\\\",\\\"c\\\"]}\" }", Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, restore.StatusCode);
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
        /// Returns the metadata field as a JSON element. DAB applies no special handling to a JSON
        /// column, so SQL Server's FOR JSON PATH projection inlines it as a native JSON object at the
        /// REST boundary. This helper asserts the value is a JSON object and returns it for inspection.
        /// </summary>
        private static JsonElement ParseMetadata(JsonElement record)
        {
            JsonElement metadata = record.GetProperty("metadata");
            Assert.AreEqual(
                JsonValueKind.Object,
                metadata.ValueKind,
                "A native JSON column is inlined as a JSON object at the REST boundary via FOR JSON PATH.");

            return metadata.Clone();
        }

        #endregion
    }
}
