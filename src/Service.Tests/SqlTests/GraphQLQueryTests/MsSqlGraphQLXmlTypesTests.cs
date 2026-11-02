// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.Tests.SqlTests.SqlTestHelper;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLQueryTests
{
    /// <summary>
    /// CRUD tests for SQL Server <c>xml</c> column support via GraphQL.
    /// DAB exposes an xml column as the built-in <c>String</c> scalar: the raw XML text is written from
    /// the mutation input and read back as a string. Because XML attributes use double quotes, the value
    /// is JSON-escaped on the wire; these tests assert that once the GraphQL <c>String</c> field is read
    /// through a JSON parser the escaping is gone and the result is well-formed XML - i.e. the value
    /// round-trips correctly through create/read/update/delete.
    /// This complements the REST coverage in
    /// <see cref="RestApiTests.MsSqlRestXmlTypesTests"/>.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGraphQLXmlTypesTests : SqlTestBase
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        #region Read Tests

        /// <summary>
        /// Query the xml column by primary key and verify the double-quoted attribute value round-trips
        /// as valid XML with no stray backslashes.
        /// </summary>
        [TestMethod]
        public async Task QueryXmlTypeByPk()
        {
            JsonElement result = await GetRecordByPkAsync(1);

            Assert.AreEqual(1, result.GetProperty("id").GetInt32());
            XElement root = AssertXmlWithId(ContentOf(result), "100");
            Assert.AreEqual("order", root.Name.LocalName);
        }

        /// <summary>
        /// Query the xml column with multiple quoted attributes and verify they round-trip.
        /// </summary>
        [TestMethod]
        public async Task QueryXmlTypeWithMultipleAttributes()
        {
            JsonElement result = await GetRecordByPkAsync(2);
            XElement setting = AssertIsValidXml(ContentOf(result)).Element("setting");
            Assert.AreEqual("true", setting!.Attribute("enabled")!.Value);
            Assert.AreEqual("cache", setting.Attribute("name")!.Value);
        }

        /// <summary>
        /// Query the xml column where the value is SQL NULL and verify null is returned.
        /// </summary>
        [TestMethod]
        public async Task QueryXmlTypeWithNull()
        {
            JsonElement result = await GetRecordByPkAsync(4);
            Assert.AreEqual(JsonValueKind.Null, result.GetProperty("content").ValueKind);
        }

        /// <summary>
        /// Query the list of xml records and verify the first record (ordered by primary key) carries
        /// valid XML.
        /// </summary>
        [TestMethod]
        public async Task QueryXmlTypeList()
        {
            string graphQLQueryName = "xmlProfiles";
            string graphQLQuery = @"{
                xmlProfiles(filter: { id: { gte: 1, lte: 4 } }, orderBy: { id: ASC }) {
                    items {
                        id
                        content
                    }
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);

            JsonElement items = result.GetProperty("items");
            Assert.AreEqual(4, items.GetArrayLength(), "Expected the 4 seeded xml_profiles rows.");

            JsonElement first = items[0];
            Assert.AreEqual(1, first.GetProperty("id").GetInt32());
            AssertXmlWithId(ContentOf(first), "100");
        }

        #endregion

        #region Write Tests

        /// <summary>
        /// Insert a new record with an XML value (with a quoted attribute) via a create mutation and verify
        /// the persisted value is returned as valid XML and can be read back. The record is deleted afterwards.
        /// </summary>
        [TestMethod]
        public async Task InsertXmlType()
        {
            string createMutationName = "createXmlProfile";
            string createMutation = @"mutation {
                createXmlProfile(item: { content: ""<order id=\""200\""><item>Pen</item></order>"" }) {
                    id
                    content
                }
            }";

            JsonElement created = await ExecuteGraphQLRequestAsync(createMutation, createMutationName, isAuthenticated: false);

            int newId = created.GetProperty("id").GetInt32();
            try
            {
                AssertXmlWithId(ContentOf(created), "200");
                JsonElement readBack = await GetRecordByPkAsync(newId);
                Assert.AreEqual(ContentOf(created), ContentOf(readBack));
                AssertXmlWithId(ContentOf(readBack), "200");
            }
            finally
            {
                await DeleteXmlProfileAsync(newId);
            }
        }

        /// <summary>
        /// Insert a new record with a null xml value via a create mutation and verify null round-trips.
        /// </summary>
        [TestMethod]
        public async Task InsertNullXmlType()
        {
            string createMutationName = "createXmlProfile";
            string createMutation = @"mutation {
                createXmlProfile(item: { content: null }) {
                    id
                    content
                }
            }";

            JsonElement created = await ExecuteGraphQLRequestAsync(createMutation, createMutationName, isAuthenticated: false);

            int newId = created.GetProperty("id").GetInt32();
            try
            {
                Assert.AreEqual(JsonValueKind.Null, created.GetProperty("content").ValueKind);
                JsonElement readBack = await GetRecordByPkAsync(newId);
                Assert.AreEqual(JsonValueKind.Null, readBack.GetProperty("content").ValueKind);
            }
            finally
            {
                await DeleteXmlProfileAsync(newId);
            }
        }

        /// <summary>
        /// Update an existing record's xml value via an update mutation and verify the new value is
        /// persisted, then restore the original value.
        /// </summary>
        [TestMethod]
        public async Task UpdateXmlType()
        {
            JsonElement created = await ExecuteGraphQLRequestAsync(
                "mutation { createXmlProfile(item: { content: \"<fixture/>\" }) { id } }",
                "createXmlProfile", isAuthenticated: false);
            int id = created.GetProperty("id").GetInt32();
            string updateMutationName = "updateXmlProfile";
            string updateMutation = @"mutation {
                updateXmlProfile(id: " + id + @", item: { content: ""<order id=\""999\""><item>Updated</item></order>"" }) {
                    id
                    content
                }
            }";

            try
            {
                JsonElement updated = await ExecuteGraphQLRequestAsync(updateMutation, updateMutationName, isAuthenticated: false);
                Assert.AreEqual(id, updated.GetProperty("id").GetInt32());
                AssertXmlWithId(ContentOf(updated), "999");

                JsonElement readBack = await GetRecordByPkAsync(id);
                Assert.AreEqual(ContentOf(updated), ContentOf(readBack));
                AssertXmlWithId(ContentOf(readBack), "999");
            }
            finally
            {
                await DeleteXmlProfileAsync(id);
            }
        }

        /// <summary>
        /// Insert malformed XML via a create mutation and verify the mutation fails with a GraphQL error
        /// rather than persisting bad data (SQL Server validates the xml type at the database boundary).
        /// </summary>
        [TestMethod]
        public async Task InsertInvalidXmlTypeFails()
        {
            string createMutationName = "createXmlProfile";
            string createMutation = @"mutation {
                createXmlProfile(item: { content: ""<order><item>Book</order>"" }) {
                    id
                    content
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(createMutation, createMutationName, isAuthenticated: false, expectsError: true);

            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: "DatabaseInputError");
        }

        #endregion

        [DataTestMethod]
        [DataRow("<order id=\"200\"><item/></order>", false)]
        [DataRow("<order><item></order>", true)]
        public async Task StoredProcedureXmlParameterAndResult(string xml, bool expectsError)
        {
            string query = "{ executeEchoXml(doc: " + JsonSerializer.Serialize(xml) + ") { content } }";
            JsonElement result = await ExecuteGraphQLRequestAsync(query, "executeEchoXml",
                isAuthenticated: false, expectsError: expectsError);
            if (expectsError)
            {
                TestForErrorInGraphQLResponse(result.ToString(), statusCode: "DatabaseInputError");
            }
            else
            {
                Assert.AreEqual(1, result.GetArrayLength());
                AssertXmlContent(result[0], xml, "content");
            }
        }

        [DataTestMethod]
        [DataRow("filter: { content: { eq: \"<order/>\" } }")]
        [DataRow("filter: { content: { contains: \"order\" } }")]
        [DataRow("orderBy: { content: ASC }")]
        public async Task UnsupportedXmlOperationsReturnInputError(string arguments)
        {
            JsonElement result = await ExecuteGraphQLRequestAsync(
                "{ xmlProfiles(" + arguments + ") { items { id } } }", "xmlProfiles",
                isAuthenticated: false, expectsError: true);
            TestForErrorInGraphQLResponse(result.ToString(), statusCode: "DatabaseInputError");
        }

        #region Helpers

        /// <summary>
        /// Fetches a single XmlProfile record by its primary key via GraphQL and returns the record element.
        /// </summary>
        private async Task<JsonElement> GetRecordByPkAsync(int id)
        {
            string graphQLQueryName = "xmlProfile_by_pk";
            string graphQLQuery = @"{
                xmlProfile_by_pk(id: " + id + @") {
                    id
                    content
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            return result.Clone();
        }

        /// <summary>
        /// Deletes an XmlProfile record by its primary key via a delete mutation.
        /// </summary>
        private async Task DeleteXmlProfileAsync(int id)
        {
            string deleteMutationName = "deleteXmlProfile";
            string deleteMutation = @"mutation {
                deleteXmlProfile(id: " + id + @") {
                    id
                }
            }";

            JsonElement result = await ExecuteGraphQLRequestAsync(deleteMutation, deleteMutationName, isAuthenticated: false);
            Assert.AreEqual(id, result.GetProperty("id").GetInt32());
        }

        /// <summary>
        /// Returns the string value of a record's <c>content</c> field.
        /// </summary>
        private static string ContentOf(JsonElement record) => record.GetProperty("content").GetString();

        #endregion
    }
}
