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
    }
}
