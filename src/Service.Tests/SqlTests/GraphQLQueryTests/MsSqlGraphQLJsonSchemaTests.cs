// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLQueryTests
{
    /// <summary>
    /// GraphQL schema-discovery (introspection) tests for a SQL Server 2025 native <c>json</c> column.
    /// DAB does nothing special for a JSON column - it is treated as a normal <c>string</c>, so the
    /// GraphQL schema must expose it using the built-in <c>String</c> scalar (no bespoke JSON scalar),
    /// honoring the column's nullability. This is the GraphQL counterpart to the REST/OpenAPI coverage
    /// in <see cref="RestApiTests.MsSqlRestJsonTypesTests"/> and <c>JsonTypeSchemaTests</c>.
    /// NOTE: The native JSON data type requires SQL Server 2025 / Azure SQL.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGraphQLJsonSchemaTests : SqlTestBase
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Introspecting the Profile type must report its json-backed <c>metadata</c> field as the
        /// built-in nullable <c>String</c> scalar - proving JSON gets no custom scalar in the schema.
        /// </summary>
        [TestMethod]
        public async Task JsonColumn_IsIntrospectedAsBuiltInStringScalar()
        {
            string introspectionQuery = @"{
                __type(name: ""Profile"") {
                    name
                    fields {
                        name
                        type { kind name ofType { kind name } }
                    }
                }
            }";

            JsonElement type = await ExecuteGraphQLRequestAsync(introspectionQuery, "__type", isAuthenticated: false);

            Assert.AreEqual("Profile", type.GetProperty("name").GetString(), "Introspection should resolve the Profile GraphQL type.");

            JsonElement metadataField = type.GetProperty("fields").EnumerateArray()
                .Single(f => f.GetProperty("name").GetString() == "metadata");

            // A nullable column surfaces as the bare scalar (no NON_NULL wrapper), so type.kind/name
            // describe the scalar directly.
            JsonElement metadataType = metadataField.GetProperty("type");
            Assert.AreEqual("SCALAR", metadataType.GetProperty("kind").GetString(), "A json column must map to a scalar, not a custom object/scalar type.");
            Assert.AreEqual("String", metadataType.GetProperty("name").GetString(), "A json column must use the built-in String scalar (no bespoke JSON scalar).");
        }

        /// <summary>
        /// profile_by_pk(id: 1) { metadata } - Verify that reading the json-backed field through GraphQL
        /// succeeds and returns the payload as a JSON string. The String leaf resolver calls
        /// JsonElement.GetString(), which only works because the engine casts the json column to
        /// NVARCHAR(MAX) so it is emitted as an escaped string rather than an inlined JSON object.
        /// This guards against the introspection test passing while a real read throws a GraphQLMapping error.
        /// </summary>
        [TestMethod]
        public async Task JsonColumn_GraphQLRead_ReturnsPayloadAsString()
        {
            string query = @"{
                profile_by_pk(id: 1) {
                    metadata
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(query, "profile_by_pk", isAuthenticated: false);

            JsonElement metadata = result.GetProperty("metadata");
            Assert.AreEqual(JsonValueKind.String, metadata.ValueKind, "A json column must be returned as a JSON string through GraphQL (treated as a normal string).");

            JsonElement parsed = JsonDocument.Parse(metadata.GetString()!).RootElement;
            Assert.AreEqual("admin", parsed.GetProperty("role").GetString());
            Assert.AreEqual(3, parsed.GetProperty("tier").GetInt32());
        }

        /// <summary>
        /// createProfile with malformed JSON in the metadata field must fail with a GraphQL error
        /// (surfaced from SQL Server's json validation) rather than persisting invalid data.
        /// </summary>
        [TestMethod]
        public async Task JsonColumn_GraphQLCreateWithMalformedJson_Fails()
        {
            string createMutationName = "createProfile";
            string createMutation = @"mutation {
                createProfile(item: { metadata: ""{ not valid json"" }) {
                    id
                    metadata
                }
            }";

            JsonElement errors = await ExecuteGraphQLRequestAsync(createMutation, createMutationName, isAuthenticated: false);

            Assert.AreEqual(JsonValueKind.Array, errors.ValueKind, "Expected a GraphQL errors array for malformed JSON payload.");
            Assert.IsTrue(errors.GetArrayLength() > 0, "Expected at least one GraphQL error.");
        }
    }
}
