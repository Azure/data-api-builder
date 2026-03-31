// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Find
{
    /// <summary>
    /// Tests for PostgreSQL array column support via REST endpoints (read-only).
    /// Verifies that array columns are correctly returned as JSON arrays via REST GET requests.
    /// </summary>
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlRestArrayTypesTests : SqlTestBase
    {
        private const string ARRAY_TYPE_REST_PATH = "api/ArrayType";

        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture();
        }

        /// <summary>
        /// GET /api/ArrayType - Verify that listing array type entities returns array columns as JSON arrays.
        /// </summary>
        [TestMethod]
        public async Task GetArrayTypeList()
        {
            HttpResponseMessage response = await HttpClient.GetAsync(ARRAY_TYPE_REST_PATH);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            JsonElement root = JsonDocument.Parse(body).RootElement;
            JsonElement items = root.GetProperty("value");

            Assert.IsTrue(items.GetArrayLength() >= 2, $"Expected at least 2 items, got {items.GetArrayLength()}");

            // First row should have array values
            JsonElement first = items[0];
            Assert.AreEqual(1, first.GetProperty("id").GetInt32());
            Assert.AreEqual(JsonValueKind.Array, first.GetProperty("int_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Array, first.GetProperty("text_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Array, first.GetProperty("bool_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Array, first.GetProperty("long_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Array, first.GetProperty("json_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Array, first.GetProperty("jsonb_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Array, first.GetProperty("money_array_col").ValueKind);
        }

        /// <summary>
        /// GET /api/ArrayType/id/1 - Verify that fetching by primary key returns array columns correctly.
        /// </summary>
        [TestMethod]
        public async Task GetArrayTypeByPrimaryKey()
        {
            HttpResponseMessage response = await HttpClient.GetAsync($"{ARRAY_TYPE_REST_PATH}/id/1");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            JsonElement root = JsonDocument.Parse(body).RootElement;
            JsonElement value = root.GetProperty("value")[0];

            Assert.AreEqual(1, value.GetProperty("id").GetInt32());

            // Verify int array
            JsonElement intArray = value.GetProperty("int_array_col");
            Assert.AreEqual(JsonValueKind.Array, intArray.ValueKind);
            Assert.AreEqual(3, intArray.GetArrayLength());

            // Verify text array
            JsonElement textArray = value.GetProperty("text_array_col");
            Assert.AreEqual(JsonValueKind.Array, textArray.ValueKind);
            Assert.AreEqual(2, textArray.GetArrayLength());

            // Verify boolean array
            JsonElement boolArray = value.GetProperty("bool_array_col");
            Assert.AreEqual(JsonValueKind.Array, boolArray.ValueKind);
            Assert.AreEqual(2, boolArray.GetArrayLength());

            // Verify long array
            JsonElement longArray = value.GetProperty("long_array_col");
            Assert.AreEqual(JsonValueKind.Array, longArray.ValueKind);
            Assert.AreEqual(3, longArray.GetArrayLength());

            // Verify json array
            JsonElement jsonArray = value.GetProperty("json_array_col");
            Assert.AreEqual(JsonValueKind.Array, jsonArray.ValueKind);
            Assert.AreEqual(2, jsonArray.GetArrayLength());

            // Verify jsonb array
            JsonElement jsonbArray = value.GetProperty("jsonb_array_col");
            Assert.AreEqual(JsonValueKind.Array, jsonbArray.ValueKind);
            Assert.AreEqual(2, jsonbArray.GetArrayLength());

            // Verify money array
            JsonElement moneyArray = value.GetProperty("money_array_col");
            Assert.AreEqual(JsonValueKind.Array, moneyArray.ValueKind);
            Assert.AreEqual(3, moneyArray.GetArrayLength());
        }

        /// <summary>
        /// GET /api/ArrayType/id/3 - Verify that null array columns are returned as JSON null.
        /// </summary>
        [TestMethod]
        public async Task GetArrayTypeWithNullArrays()
        {
            HttpResponseMessage response = await HttpClient.GetAsync($"{ARRAY_TYPE_REST_PATH}/id/3");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            JsonElement root = JsonDocument.Parse(body).RootElement;
            JsonElement value = root.GetProperty("value")[0];

            Assert.AreEqual(3, value.GetProperty("id").GetInt32());
            Assert.AreEqual(JsonValueKind.Null, value.GetProperty("int_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, value.GetProperty("text_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, value.GetProperty("bool_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, value.GetProperty("long_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, value.GetProperty("json_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, value.GetProperty("jsonb_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, value.GetProperty("money_array_col").ValueKind);
        }
    }
}
