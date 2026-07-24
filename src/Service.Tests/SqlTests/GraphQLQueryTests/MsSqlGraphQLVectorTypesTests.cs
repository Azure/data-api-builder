// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLQueryTests
{
    /// <summary>
    /// Tests for SQL Server vector column support via GraphQL (read and write).
    /// Verifies that vector columns are exposed as GraphQL lists of Single values, that they can be
    /// queried (list and by primary key) and mutated (create/update/delete), and that the values that
    /// are read from and written to the <c>vector_type_table</c> round-trip correctly.
    /// This complements the REST coverage in
    /// <see cref="RestApiTests.MsSqlRestVectorTypesTests"/>.
    /// NOTE: The vector data type requires SQL Server 2025 / Azure SQL.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGraphQLVectorTypesTests : SqlTestBase
    {
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

        /// <summary>
        /// Query the vector column by primary key and verify the returned list matches the values seeded
        /// in the database.
        /// </summary>
        [DataTestMethod]
        [DataRow(1, new[] { 0.5f, 0.25f, 0.75f }, DisplayName = "Query vector data type by primary key")]
        [DataRow(2, new[] { 1.5f, -2.5f, 3.5f }, DisplayName = "Query vector data type with negative components")]
        [DataRow(3, null, DisplayName = "Query vector data type with null vector")]
        public async Task QueryVectorTypeByPk(int id, float[] expectedValues)
        {
            string graphQLQueryName = "vectorType_by_pk";
            string graphQLQuery = @"{
                vectorType_by_pk(id: " + id + @") {
                    id
                    vector_data
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);

            Assert.AreEqual(id, result.GetProperty("id").GetInt32());
            AssertVectorEquals(result.GetProperty("vector_data"), expectedValues);
        }

        /// <summary>
        /// Query the list of vector records and verify the first record (ordered by primary key) matches
        /// the seeded value.
        /// </summary>
        [TestMethod]
        public async Task QueryVectorTypeList()
        {
            string graphQLQueryName = "vectorTypes";
            string graphQLQuery = @"{
                vectorTypes(orderBy: { id: ASC }) {
                    items {
                        id
                        vector_data
                    }
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);

            JsonElement items = result.GetProperty("items");
            Assert.IsTrue(items.GetArrayLength() >= 1, "Expected at least one vector record.");

            JsonElement first = items[0];
            Assert.AreEqual(1, first.GetProperty("id").GetInt32());
            AssertVectorEquals(first.GetProperty("vector_data"), new[] { 0.5f, 0.25f, 0.75f });
        }

        /// <summary>
        /// Query the maximum-dimension vector column and verify the returned list has the expected number
        /// of components and the correct values.
        /// </summary>
        [TestMethod]
        public async Task QueryVectorTypeWithMaxDimensions()
        {
            string graphQLQueryName = "vectorType_by_pk";
            string graphQLQuery = @"{
                vectorType_by_pk(id: 7) {
                    id
                    vector_data_max
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);

            Assert.AreEqual(7, result.GetProperty("id").GetInt32());

            JsonElement maxVector = result.GetProperty("vector_data_max");
            Assert.AreEqual(JsonValueKind.Array, maxVector.ValueKind, "Expected the maximum-dimension vector to be a list.");
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
        /// Insert a new record with a vector value via a create mutation and verify the persisted value is
        /// returned as a list and can be read back correctly. The record is deleted afterwards to keep the
        /// table clean.
        /// </summary>
        [DataTestMethod]
        [DataRow("[0.125, 0.25, 0.5]", new[] { 0.125f, 0.25f, 0.5f }, DisplayName = "Insert valid vector")]
        [DataRow("null", null, DisplayName = "Insert valid null vector")]
        [DataRow("[5e-1, 2.5e-1, 7.5e-1]", new[] { 0.5f, 0.25f, 0.75f }, DisplayName = "Insert valid vector with scientific notation")]
        public async Task InsertVectorType(string vectorLiteral, float[] expectedValue)
        {
            string createMutationName = "createVectorType";
            string createMutation = @"mutation {
                createVectorType(item: { vector_data: " + vectorLiteral + @" }) {
                    id
                    vector_data
                }
            }";

            JsonElement created = await ExecuteGraphQLRequestAsync(createMutation, createMutationName, isAuthenticated: false);

            int newId = created.GetProperty("id").GetInt32();
            AssertVectorEquals(created.GetProperty("vector_data"), expectedValue);

            // Confirm the value was persisted by reading it back.
            JsonElement readBack = await GetRecordByPkAsync(newId);
            AssertVectorEquals(readBack.GetProperty("vector_data"), expectedValue);

            await DeleteVectorTypeAsync(newId);
        }

        /// <summary>
        /// Insert an invalid vector (too many dimensions) via a create mutation and verify the mutation
        /// fails with a GraphQL error rather than persisting bad data.
        /// </summary>
        [TestMethod]
        public async Task InsertInvalidVectorTypeFails()
        {
            string createMutationName = "createVectorType";
            string createMutation = @"mutation {
                createVectorType(item: { vector_data: [1.25, 2.25, 3.25, 4.25] }) {
                    id
                    vector_data
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(createMutation, createMutationName, isAuthenticated: false, expectsError: true);

            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString());
        }

        /// <summary>
        /// Update an existing record's vector value via an update mutation and verify the new value is
        /// persisted, then restore the original value.
        /// </summary>
        [TestMethod]
        public async Task UpdateVectorType()
        {
            string updateMutationName = "updateVectorType";

            // Change vector value.
            float[] expected = new[] { 9.5f, 8.5f, 7.5f };
            string updateMutation = @"mutation {
                updateVectorType(id: 4, item: { vector_data: [9.5, 8.5, 7.5] }) {
                    id
                    vector_data
                }
            }";

            JsonElement updated = await ExecuteGraphQLRequestAsync(updateMutation, updateMutationName, isAuthenticated: false);
            Assert.AreEqual(4, updated.GetProperty("id").GetInt32());
            AssertVectorEquals(updated.GetProperty("vector_data"), expected);

            JsonElement readBack = await GetRecordByPkAsync(4);
            AssertVectorEquals(readBack.GetProperty("vector_data"), expected);

            // Restore vector value to original.
            float[] original = new[] { 1.0f, 2.0f, 3.0f };
            string restoreMutation = @"mutation {
                updateVectorType(id: 4, item: { vector_data: [1.0, 2.0, 3.0] }) {
                    id
                    vector_data
                }
            }";

            JsonElement restored = await ExecuteGraphQLRequestAsync(restoreMutation, updateMutationName, isAuthenticated: false);
            AssertVectorEquals(restored.GetProperty("vector_data"), original);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Fetches a single VectorType record by its primary key via GraphQL and returns the record element.
        /// </summary>
        private async Task<JsonElement> GetRecordByPkAsync(int id)
        {
            string graphQLQueryName = "vectorType_by_pk";
            string graphQLQuery = @"{
                vectorType_by_pk(id: " + id + @") {
                    id
                    vector_data
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            return result.Clone();
        }

        /// <summary>
        /// Deletes a VectorType record by its primary key via a delete mutation.
        /// </summary>
        private async Task DeleteVectorTypeAsync(int id)
        {
            string deleteMutationName = "deleteVectorType";
            string deleteMutation = @"mutation {
                deleteVectorType(id: " + id + @") {
                    id
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(deleteMutation, deleteMutationName, isAuthenticated: false);
            Assert.AreEqual(id, result.GetProperty("id").GetInt32());
        }

        /// <summary>
        /// Asserts that the given JSON element is a list whose components match the expected vector within
        /// <see cref="VECTOR_COMPONENT_DELTA"/>.
        /// </summary>
        private static void AssertVectorEquals(JsonElement actual, float[] expected)
        {
            if (expected == null)
            {
                Assert.AreEqual(JsonValueKind.Null, actual.ValueKind, "Expected a null vector, but got a non-null value.");
                return;
            }

            Assert.AreEqual(JsonValueKind.Array, actual.ValueKind, "Expected the vector to be serialized as a list.");
            Assert.AreEqual(expected.Length, actual.GetArrayLength(), "Vector dimension mismatch.");

            int i = 0;
            foreach (JsonElement element in actual.EnumerateArray())
            {
                Assert.AreEqual(expected[i], element.GetDouble(), VECTOR_COMPONENT_DELTA, $"Vector component mismatch at index {i}.");
                i++;
            }
        }

        #endregion
    }
}
