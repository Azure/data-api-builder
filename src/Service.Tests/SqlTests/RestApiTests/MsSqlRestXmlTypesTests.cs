// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.Tests.SqlTests.SqlTestHelper;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// CRUD tests for SQL Server <c>xml</c> column support via REST endpoints.
    /// DAB treats an xml column exactly like a string: the raw XML text is written from the request
    /// payload and read back as a JSON string. Because XML attributes use double quotes, the value
    /// is JSON-escaped on the wire (e.g. <c>id=\"100\"</c>); these tests assert that once the field is
    /// read through a JSON parser the escaping is gone and the result is well-formed XML - i.e. the
    /// value round-trips correctly through create/read/update/delete.
    /// This complements the GraphQL coverage in
    /// <see cref="GraphQLQueryTests.MsSqlGraphQLXmlTypesTests"/>.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlRestXmlTypesTests : SqlTestBase
    {
        private const string XML_TYPE_REST_PATH = "api/XmlProfile";

        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        #region Read Tests

        /// <summary>
        /// GET /api/XmlProfile - Verify the whole collection is returned and that each content value
        /// renders either as a JSON string carrying valid XML or as null.
        /// </summary>
        [TestMethod]
        public async Task GetXmlTypeList()
        {
            HttpResponseMessage response = await HttpClient.GetAsync(XML_TYPE_REST_PATH);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            JsonElement items = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
                .RootElement.GetProperty("value");
            Assert.AreEqual(4, items.GetArrayLength(), "Expected the 4 seeded xml_profiles rows.");

            foreach (JsonElement record in items.EnumerateArray())
            {
                JsonElement content = record.GetProperty("content");
                Assert.IsTrue(
                    content.ValueKind is JsonValueKind.String or JsonValueKind.Null,
                    $"Expected content to be a JSON string or null, but was {content.ValueKind}.");

                // When present, the string must be well-formed XML once JSON-decoded.
                if (content.ValueKind is JsonValueKind.String)
                {
                    AssertIsValidXml(content.GetString());
                }
            }
        }

        /// <summary>
        /// GET /api/XmlProfile/id/1 - Verify the double-quoted attribute value round-trips as valid XML
        /// with no stray backslashes once the JSON field is parsed.
        /// </summary>
        [TestMethod]
        public async Task GetXmlTypeByPrimaryKey()
        {
            // The raw field is a JSON string; after parsing it must be valid XML with the quotes intact.
            XElement root = AssertXmlWithId(GetContentString(await GetRecordByIdAsync(1)), "100");
            Assert.AreEqual("order", root.Name.LocalName);
        }

        /// <summary>
        /// GET /api/XmlProfile/id/2 - Verify an element with multiple quoted attributes round-trips.
        /// </summary>
        [TestMethod]
        public async Task GetXmlTypeWithMultipleAttributes()
        {
            string content = GetContentString(await GetRecordByIdAsync(2));
            XElement root = AssertIsValidXml(content);
            XElement setting = root.Element("setting");
            Assert.AreEqual("true", setting!.Attribute("enabled")!.Value);
            Assert.AreEqual("cache", setting.Attribute("name")!.Value);
        }

        /// <summary>
        /// GET /api/XmlProfile/id/4 - Verify a SQL NULL xml value is rendered as JSON null.
        /// </summary>
        [TestMethod]
        public async Task GetXmlTypeWithNull()
        {
            JsonElement record = await GetRecordByIdAsync(4);
            Assert.AreEqual(JsonValueKind.Null, record.GetProperty("content").ValueKind);
        }

        /// <summary>
        /// GET /api/XmlProfile?$filter=content ne null - Verify filtering an xml column (treated as a
        /// string) passes through to SQL: the 3 non-null rows match and the null row (id 4) does not.
        /// </summary>
        [TestMethod]
        public async Task FilterXmlColumnIsNotNull_Succeeds()
        {
            HttpResponseMessage response = await HttpClient.GetAsync($"{XML_TYPE_REST_PATH}?$filter=content%20ne%20null");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "Filtering an xml column as a string should pass through and succeed.");

            JsonElement items = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
                .RootElement.GetProperty("value");
            Assert.AreEqual(3, items.GetArrayLength(), "Only the 3 rows with non-null content should match.");
        }

        #endregion

        #region Write Tests

        /// <summary>
        /// POST /api/XmlProfile - Verify a new record with an XML payload (with quoted attributes) can be
        /// inserted, echoes back as valid XML, and is persisted. Also covers inserting a null payload.
        /// </summary>
        [DataTestMethod]
        [DataRow("<order id=\"200\"><item>Pen</item></order>", "200", false, DisplayName = "Insert xml with quoted attribute")]
        [DataRow(null, null, true, DisplayName = "Insert null xml")]
        public async Task InsertXmlType(string xml, string expectedId, bool expectNull)
        {
            HttpResponseMessage postResponse = await HttpClient.PostAsync(XML_TYPE_REST_PATH, XmlBody(xml));
            Assert.AreEqual(HttpStatusCode.Created, postResponse.StatusCode);

            int newId = JsonDocument.Parse(await postResponse.Content.ReadAsStringAsync())
                .RootElement.GetProperty("value")[0].GetProperty("id").GetInt32();

            JsonElement readBack = await GetRecordByIdAsync(newId);
            if (expectNull)
            {
                Assert.AreEqual(JsonValueKind.Null, readBack.GetProperty("content").ValueKind);
            }
            else
            {
                AssertXmlWithId(GetContentString(readBack), expectedId);
            }

            await DeleteXmlProfile(newId);
        }

        /// <summary>
        /// PUT /api/XmlProfile/id/1 - Verify a full update replaces the XML payload, then restore it.
        /// </summary>
        [TestMethod]
        public async Task PutXmlType_Update()
        {
            try
            {
                HttpResponseMessage response = await PutXmlAsync(1, "<order id=\"999\"><item>Updated</item></order>");
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                AssertXmlWithId(GetContentString(await GetRecordByIdAsync(1)), "999");
            }
            finally
            {
                // Restore original value so shared row 1 is left intact for other tests.
                HttpResponseMessage restore = await PutXmlAsync(1, "<order id=\"100\"><item>Book</item><qty>2</qty></order>");
                Assert.AreEqual(HttpStatusCode.OK, restore.StatusCode);
            }
        }

        /// <summary>
        /// PATCH /api/XmlProfile/id/1 - Verify a partial update sets a new XML payload, then restore it.
        /// </summary>
        [TestMethod]
        public async Task PatchXmlType_Update()
        {
            try
            {
                HttpResponseMessage response = await HttpClient.PatchAsync(
                    $"{XML_TYPE_REST_PATH}/id/1",
                    XmlBody("<order id=\"555\"><item>Patched</item></order>"));
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                AssertXmlWithId(GetContentString(await GetRecordByIdAsync(1)), "555");
            }
            finally
            {
                // Restore original value so shared row 1 is left intact for other tests.
                HttpResponseMessage restore = await PutXmlAsync(1, "<order id=\"100\"><item>Book</item><qty>2</qty></order>");
                Assert.AreEqual(HttpStatusCode.OK, restore.StatusCode);
            }
        }

        /// <summary>
        /// PATCH /api/XmlProfile/id/2 - Verify content can be cleared to null.
        /// </summary>
        [TestMethod]
        public async Task PatchXmlType_ToNull()
        {
            try
            {
                HttpResponseMessage response = await HttpClient.PatchAsync($"{XML_TYPE_REST_PATH}/id/2", XmlBody(null));
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.AreEqual(JsonValueKind.Null, (await GetRecordByIdAsync(2)).GetProperty("content").ValueKind);
            }
            finally
            {
                // Restore original value so shared row 2 is left intact for other tests.
                HttpResponseMessage restore = await PutXmlAsync(2, "<config><setting enabled=\"true\" name=\"cache\"/></config>");
                Assert.AreEqual(HttpStatusCode.OK, restore.StatusCode);
            }
        }

        /// <summary>
        /// POST /api/XmlProfile - Verify that supplying malformed XML for the xml column is rejected by
        /// SQL Server and surfaced as HTTP 400 (a client input error), not a 500. DAB treats the value
        /// as a normal string, so XML validation happens at the database boundary.
        /// </summary>
        [DataTestMethod]
        [DataRow("<order><item>Book</order>", DisplayName = "Mismatched closing tag")]
        [DataRow("<order id=unquoted></order>", DisplayName = "Unquoted attribute")]
        public async Task InsertMalformedXml_ReturnsBadRequest(string xml)
        {
            HttpResponseMessage response = await HttpClient.PostAsync(XML_TYPE_REST_PATH, XmlBody(xml));

            Assert.AreEqual(
                HttpStatusCode.BadRequest,
                response.StatusCode,
                "SQL Server rejects malformed XML for an xml column; DAB must surface it as HTTP 400.");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// DELETE /api/XmlProfile/id/{id} - Verify a record can be deleted.
        /// </summary>
        private static async Task DeleteXmlProfile(int id)
        {
            HttpResponseMessage deleteResponse = await HttpClient.DeleteAsync($"{XML_TYPE_REST_PATH}/id/{id}");
            Assert.AreEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        }

        /// <summary>
        /// Fetches a single XmlProfile record by its primary key and returns the record element.
        /// </summary>
        private static async Task<JsonElement> GetRecordByIdAsync(int id)
        {
            HttpResponseMessage response = await HttpClient.GetAsync($"{XML_TYPE_REST_PATH}/id/{id}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(body).RootElement.GetProperty("value")[0].Clone();
        }

        /// <summary>
        /// Returns the content field as a string. DAB treats an xml column as a string, so a non-null
        /// value arrives at the REST boundary as a JSON string carrying the XML text.
        /// </summary>
        private static string GetContentString(JsonElement record)
        {
            JsonElement content = record.GetProperty("content");
            Assert.AreEqual(
                JsonValueKind.String,
                content.ValueKind,
                "An xml column must be returned as a JSON string at the REST boundary (treated as a normal string).");
            return content.GetString()!;
        }

        /// <summary>
        /// Builds a request body <c>{ "content": &lt;xml&gt; }</c> (or null) without manual escaping.
        /// </summary>
        private static StringContent XmlBody(string xml)
            => new(JsonSerializer.Serialize(new { content = xml }), Encoding.UTF8, "application/json");

        /// <summary>
        /// PUT /api/XmlProfile/id/{id} with the given XML content.
        /// </summary>
        private static Task<HttpResponseMessage> PutXmlAsync(int id, string xml)
            => HttpClient.PutAsync($"{XML_TYPE_REST_PATH}/id/{id}", XmlBody(xml));

        #endregion
    }
}
