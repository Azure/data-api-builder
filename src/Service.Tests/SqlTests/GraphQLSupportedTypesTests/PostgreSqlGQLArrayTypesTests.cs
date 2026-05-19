// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLSupportedTypesTests
{

    /// <summary>
    /// Tests for PostgreSQL array column support (read-only).
    /// Verifies that array columns (int[], text[], boolean[], bigint[], json[], jsonb[], money[]) are correctly
    /// returned as JSON arrays via GraphQL queries.
    /// </summary>
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGQLArrayTypesTests : SqlTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Query a row with array columns by primary key and verify arrays are returned as JSON arrays.
        /// </summary>
        [TestMethod]
        public async Task QueryArrayColumnsByPrimaryKey()
        {
            string gqlQuery = @"{
                arrayType_by_pk(id: 1) {
                    id
                    int_array_col
                    text_array_col
                    bool_array_col
                    long_array_col
                    json_array_col
                    jsonb_array_col
                    money_array_col
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, "arrayType_by_pk", isAuthenticated: false);

            Assert.AreEqual(1, actual.GetProperty("id").GetInt32());

            // Note: Array elements are serialized as strings by the DAB query pipeline
            // (QueryExecutor reads CLR arrays from DbDataReader and serializes via JsonSerializer).
            // Using ToString() for value comparison is intentional given this serialization behavior.

            // Verify int array
            JsonElement intArray = actual.GetProperty("int_array_col");
            Assert.AreEqual(JsonValueKind.Array, intArray.ValueKind, $"int_array_col actual: {intArray}");
            Assert.AreEqual(3, intArray.GetArrayLength());
            Assert.AreEqual("1", intArray[0].ToString());
            Assert.AreEqual("2", intArray[1].ToString());
            Assert.AreEqual("3", intArray[2].ToString());

            // Verify text array
            JsonElement textArray = actual.GetProperty("text_array_col");
            Assert.AreEqual(JsonValueKind.Array, textArray.ValueKind, $"text_array_col actual: {textArray}");
            Assert.AreEqual(2, textArray.GetArrayLength());
            Assert.AreEqual("hello", textArray[0].GetString());
            Assert.AreEqual("world", textArray[1].GetString());

            // Verify boolean array
            JsonElement boolArray = actual.GetProperty("bool_array_col");
            Assert.AreEqual(JsonValueKind.Array, boolArray.ValueKind, $"bool_array_col actual: {boolArray}");
            Assert.AreEqual(2, boolArray.GetArrayLength());
            Assert.AreEqual("true", boolArray[0].ToString().ToLowerInvariant());
            Assert.AreEqual("false", boolArray[1].ToString().ToLowerInvariant());

            // Verify long array
            JsonElement longArray = actual.GetProperty("long_array_col");
            Assert.AreEqual(JsonValueKind.Array, longArray.ValueKind, $"long_array_col actual: {longArray}");
            Assert.AreEqual(3, longArray.GetArrayLength());
            Assert.AreEqual("100", longArray[0].ToString());
            Assert.AreEqual("200", longArray[1].ToString());
            Assert.AreEqual("300", longArray[2].ToString());

            // Verify json array
            JsonElement jsonArray = actual.GetProperty("json_array_col");
            Assert.AreEqual(JsonValueKind.Array, jsonArray.ValueKind, $"json_array_col actual: {jsonArray}");
            Assert.AreEqual(2, jsonArray.GetArrayLength());
            Assert.IsTrue(jsonArray[0].ToString().Contains("key"));
            Assert.IsTrue(jsonArray[1].ToString().Contains("42"));

            // Verify jsonb array
            JsonElement jsonbArray = actual.GetProperty("jsonb_array_col");
            Assert.AreEqual(JsonValueKind.Array, jsonbArray.ValueKind, $"jsonb_array_col actual: {jsonbArray}");
            Assert.AreEqual(2, jsonbArray.GetArrayLength());
            Assert.IsTrue(jsonbArray[0].ToString().Contains("key"));
            Assert.IsTrue(jsonbArray[1].ToString().Contains("42"));

            // Verify money array
            JsonElement moneyArray = actual.GetProperty("money_array_col");
            Assert.AreEqual(JsonValueKind.Array, moneyArray.ValueKind, $"money_array_col actual: {moneyArray}");
            Assert.AreEqual(3, moneyArray.GetArrayLength());
        }

        /// <summary>
        /// Query a row where all array columns are NULL and verify they come back as JSON null.
        /// </summary>
        [TestMethod]
        public async Task QueryNullArrayColumns()
        {
            string gqlQuery = @"{
                arrayType_by_pk(id: 3) {
                    id
                    int_array_col
                    text_array_col
                    bool_array_col
                    long_array_col
                    json_array_col
                    jsonb_array_col
                    money_array_col
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, "arrayType_by_pk", isAuthenticated: false);

            Assert.AreEqual(3, actual.GetProperty("id").GetInt32());
            Assert.AreEqual(JsonValueKind.Null, actual.GetProperty("int_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, actual.GetProperty("text_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, actual.GetProperty("bool_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, actual.GetProperty("long_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, actual.GetProperty("json_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, actual.GetProperty("jsonb_array_col").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, actual.GetProperty("money_array_col").ValueKind);
        }

        /// <summary>
        /// Query a row where array columns contain NULL elements (e.g., '{1,NULL,3}')
        /// and verify that null values within arrays are correctly returned.
        /// This validates that the EDM element type is marked as nullable (isNullable: true),
        /// which is required because PostgreSQL arrays can contain NULL elements.
        /// </summary>
        [TestMethod]
        public async Task QueryArrayColumnsWithNullElements()
        {
            string gqlQuery = @"{
                arrayType_by_pk(id: 4) {
                    id
                    int_array_col
                    text_array_col
                    bool_array_col
                    long_array_col
                    json_array_col
                    jsonb_array_col
                    money_array_col
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, "arrayType_by_pk", isAuthenticated: false);

            Assert.AreEqual(4, actual.GetProperty("id").GetInt32());

            // Note: The GraphQL pipeline serializes array elements as strings via JsonSerializer.
            // Null elements within arrays are serialized as JSON null values.
            // This test verifies that arrays with null elements are returned successfully,
            // which requires the EDM element type to be nullable (isNullable: true).

            // Verify int array contains null element
            JsonElement intArray = actual.GetProperty("int_array_col");
            Assert.AreEqual(JsonValueKind.Array, intArray.ValueKind);
            Assert.AreEqual(3, intArray.GetArrayLength());
            Assert.AreEqual("1", intArray[0].ToString());
            Assert.IsTrue(intArray[1].ValueKind == JsonValueKind.Null || intArray[1].ToString() == "", "Expected null element inside int array");
            Assert.AreEqual("3", intArray[2].ToString());

            // Verify text array contains null element
            JsonElement textArray = actual.GetProperty("text_array_col");
            Assert.AreEqual(JsonValueKind.Array, textArray.ValueKind);
            Assert.AreEqual(3, textArray.GetArrayLength());
            Assert.AreEqual("hello", textArray[0].ToString());
            Assert.IsTrue(textArray[1].ValueKind == JsonValueKind.Null || textArray[1].ToString() == "", "Expected null element inside text array");
            Assert.AreEqual("world", textArray[2].ToString());

            // Verify boolean array contains null element
            JsonElement boolArray = actual.GetProperty("bool_array_col");
            Assert.AreEqual(JsonValueKind.Array, boolArray.ValueKind);
            Assert.AreEqual(3, boolArray.GetArrayLength());
            Assert.AreEqual("true", boolArray[0].ToString().ToLowerInvariant());
            Assert.IsTrue(boolArray[1].ValueKind == JsonValueKind.Null || boolArray[1].ToString() == "", "Expected null element inside bool array");
            Assert.AreEqual("false", boolArray[2].ToString().ToLowerInvariant());

            // Verify long array contains null element
            JsonElement longArray = actual.GetProperty("long_array_col");
            Assert.AreEqual(JsonValueKind.Array, longArray.ValueKind);
            Assert.AreEqual(3, longArray.GetArrayLength());
            Assert.AreEqual("100", longArray[0].ToString());
            Assert.IsTrue(longArray[1].ValueKind == JsonValueKind.Null || longArray[1].ToString() == "", "Expected null element inside long array");
            Assert.AreEqual("300", longArray[2].ToString());

            // Verify json array contains null element
            JsonElement jsonArray = actual.GetProperty("json_array_col");
            Assert.AreEqual(JsonValueKind.Array, jsonArray.ValueKind);
            Assert.AreEqual(2, jsonArray.GetArrayLength());
            Assert.IsTrue(jsonArray[0].ToString().Contains("key"));
            Assert.IsTrue(jsonArray[1].ValueKind == JsonValueKind.Null || jsonArray[1].ToString() == "", "Expected null element inside json array");

            // Verify jsonb array contains null element
            JsonElement jsonbArray = actual.GetProperty("jsonb_array_col");
            Assert.AreEqual(JsonValueKind.Array, jsonbArray.ValueKind);
            Assert.AreEqual(2, jsonbArray.GetArrayLength());
            Assert.IsTrue(jsonbArray[0].ToString().Contains("key"));
            Assert.IsTrue(jsonbArray[1].ValueKind == JsonValueKind.Null || jsonbArray[1].ToString() == "", "Expected null element inside jsonb array");

            // Verify money array contains null element
            JsonElement moneyArray = actual.GetProperty("money_array_col");
            Assert.AreEqual(JsonValueKind.Array, moneyArray.ValueKind);
            Assert.AreEqual(3, moneyArray.GetArrayLength());
            Assert.IsTrue(moneyArray[1].ValueKind == JsonValueKind.Null || moneyArray[1].ToString() == "", "Expected null element inside money array");
        }

        /// <summary>
        /// Query multiple rows with array columns and verify the list result.
        /// </summary>
        [TestMethod]
        public async Task QueryMultipleRowsWithArrayColumns()
        {
            string gqlQuery = @"{
                arrayTypes(first: 2, orderBy: { id: ASC }) {
                    items {
                        id
                        int_array_col
                        text_array_col
                        json_array_col
                        jsonb_array_col
                        money_array_col
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, "arrayTypes", isAuthenticated: false);
            JsonElement items = actual.GetProperty("items");

            Assert.AreEqual(2, items.GetArrayLength());

            // First row
            Assert.AreEqual(1, items[0].GetProperty("id").GetInt32());
            Assert.AreEqual(3, items[0].GetProperty("int_array_col").GetArrayLength());
            Assert.AreEqual(2, items[0].GetProperty("json_array_col").GetArrayLength());
            Assert.AreEqual(2, items[0].GetProperty("jsonb_array_col").GetArrayLength());
            Assert.AreEqual(3, items[0].GetProperty("money_array_col").GetArrayLength());

            // Second row
            Assert.AreEqual(2, items[1].GetProperty("id").GetInt32());
            Assert.AreEqual(2, items[1].GetProperty("int_array_col").GetArrayLength());
            Assert.AreEqual(3, items[1].GetProperty("text_array_col").GetArrayLength());
            Assert.AreEqual("foo", items[1].GetProperty("text_array_col")[0].ToString());
            Assert.AreEqual(1, items[1].GetProperty("json_array_col").GetArrayLength());
            Assert.AreEqual(1, items[1].GetProperty("jsonb_array_col").GetArrayLength());
            Assert.AreEqual(2, items[1].GetProperty("money_array_col").GetArrayLength());
        }
    }
}
