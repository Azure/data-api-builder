using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Service.Tests.SqlTests.SqlTestHelper;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlMcpXmlTypesTests : McpToolTestBase
    {
        private const string ENTITY_NAME = "XmlProfile";
        private const string ORIGINAL_XML = "<order id=\"200\"><item>Pen</item></order>";

        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        /// <summary>Verifies MCP reads preserve XML attributes and SQL null values.</summary>
        [DataTestMethod]
        [DataRow(1, "<order id=\"100\"><item>Book</item><qty>2</qty></order>", DisplayName = "Read XML with a quoted attribute")]
        [DataRow(2, "<config><setting enabled=\"true\" name=\"cache\"/></config>", DisplayName = "Read XML with multiple quoted attributes")]
        [DataRow(4, null, DisplayName = "Read null XML content")]
        public async Task ReadXmlById_ReturnsXmlOrNull(int id, string expectedXml)
        {
            JsonElement records = await ReadXmlAsync($"id eq {id}");
            Assert.AreEqual(1, records.GetArrayLength());
            AssertXmlContent(records[0], expectedXml);
        }

        /// <summary>Verifies an MCP collection returns valid XML strings.</summary>
        [TestMethod]
        public async Task ReadXmlList_ReturnsStringsAndNull()
        {
            JsonElement records = await ReadXmlAsync("id ge 1 and id le 4");
            Assert.AreEqual(4, records.GetArrayLength());
            foreach (JsonElement record in records.EnumerateArray())
            {
                JsonElement content = record.GetProperty("content");
                if (record.GetProperty("id").GetInt32() == 4)
                {
                    Assert.AreEqual(JsonValueKind.Null, content.ValueKind);
                }
                else
                {
                    Assert.AreEqual(JsonValueKind.String, content.ValueKind);
                    AssertIsValidXml(content.GetString()!);
                }
            }
        }

        /// <summary>Verifies filtering an XML column excludes records with null content.</summary>
        [TestMethod]
        public async Task FilterXmlNotNull_ExcludesNullContent()
        {
            JsonElement records = await ReadXmlAsync("id ge 1 and id le 4 and content ne null");
            Assert.AreEqual(3, records.GetArrayLength());
            foreach (JsonElement record in records.EnumerateArray())
            {
                Assert.AreEqual(JsonValueKind.String, record.GetProperty("content").ValueKind);
                Assert.AreNotEqual(4, record.GetProperty("id").GetInt32());
            }
        }

        /// <summary>Verifies MCP create/read round trips preserve XML or null, and deletion removes the record.</summary>
        [DataTestMethod]
        [DataRow(ORIGINAL_XML, DisplayName = "Create and delete XML with a quoted attribute")]
        [DataRow("<config><setting enabled=\"true\" name=\"cache\"/></config>", DisplayName = "Create and delete XML with multiple quoted attributes")]
        [DataRow(null, DisplayName = "Create and delete null XML content")]
        public async Task CreateXml_RoundTripsAndDeletes(string xml)
        {
            CallToolResult result = await CreateXmlAsync(xml);
            AssertSuccess(result, "Creating XML content should succeed.");
            JsonElement created = GetRecordsArray(ParseResultRoot(result))[0];
            int id = created.GetProperty("id").GetInt32();
            try
            {
                AssertXmlContent(created, xml);
                AssertXmlContent((await ReadXmlAsync($"id eq {id}"))[0], xml);
            }
            finally
            {
                await DeleteXmlAsync(id);
            }

            Assert.AreEqual(0, (await ReadXmlAsync($"id eq {id}")).GetArrayLength());
        }

        /// <summary>Verifies MCP updates preserve XML attributes and escaped text, or clear content to null.</summary>
        [DataTestMethod]
        [DataRow("<order id=\"999\"><item>Updated &amp; quoted</item></order>", DisplayName = "Update XML with a quoted attribute and escaped text")]
        [DataRow("<config><setting enabled=\"false\" name=\"updated\"/></config>", DisplayName = "Update XML with multiple quoted attributes")]
        [DataRow(null, DisplayName = "Update XML content to null")]
        public async Task UpdateXml_RoundTrips(string xml)
        {
            CallToolResult created = await CreateXmlAsync(ORIGINAL_XML);
            AssertSuccess(created, "Creating the update fixture should succeed.");
            int id = GetRecordsArray(ParseResultRoot(created))[0].GetProperty("id").GetInt32();
            try
            {
                CallToolResult result = await UpdateXmlAsync(id, xml);
                AssertSuccess(result, "Updating XML content should succeed.");
                AssertXmlContent(ParseResultRoot(result).GetProperty("result"), xml);
                AssertXmlContent((await ReadXmlAsync($"id eq {id}"))[0], xml);
            }
            finally
            {
                await DeleteXmlAsync(id);
            }
        }

        /// <summary>Verifies MCP creation rejects malformed XML at the database boundary.</summary>
        [DataTestMethod]
        [DataRow("<order><item>Book</order>", DisplayName = "Reject XML creation with a mismatched closing tag")]
        [DataRow("<order id=unquoted></order>", DisplayName = "Reject XML creation with an unquoted attribute")]
        public async Task CreateMalformedXml_ReturnsError(string xml)
        {
            AssertError(await CreateXmlAsync(xml));
        }

        /// <summary>Verifies rejected malformed XML updates leave the stored content unchanged.</summary>
        [DataTestMethod]
        [DataRow("<order><item>Book</order>", DisplayName = "Reject XML update with a mismatched closing tag and preserve content")]
        [DataRow("<order id=unquoted></order>", DisplayName = "Reject XML update with an unquoted attribute and preserve content")]
        public async Task UpdateMalformedXml_ReturnsErrorAndPreservesContent(string xml)
        {
            CallToolResult created = await CreateXmlAsync(ORIGINAL_XML);
            AssertSuccess(created, "Creating the update fixture should succeed.");
            int id = GetRecordsArray(ParseResultRoot(created))[0].GetProperty("id").GetInt32();
            try
            {
                AssertError(await UpdateXmlAsync(id, xml));
                AssertXmlContent((await ReadXmlAsync($"id eq {id}"))[0], ORIGINAL_XML);
            }
            finally
            {
                await DeleteXmlAsync(id);
            }
        }

        private static async Task<JsonElement> ReadXmlAsync(string filter)
        {
            CallToolResult result = await ExecuteToolAsync(new ReadRecordsTool(), BuildQueryServiceProvider(),
                new Dictionary<string, object?> { { "entity", ENTITY_NAME }, { "filter", filter }, { "select", "id,content" } });
            AssertSuccess(result, "Reading XML content should succeed.");
            return GetRecordsArray(ParseResultRoot(result));
        }

        private static Task<CallToolResult> CreateXmlAsync(string xml)
        {
            return ExecuteToolAsync(new CreateRecordTool(), BuildMutationServiceProvider(),
                new Dictionary<string, object?>
                {
                    { "entity", ENTITY_NAME },
                    { "data", new Dictionary<string, object?> { { "content", xml } } }
                });
        }

        private static Task<CallToolResult> UpdateXmlAsync(int id, string xml)
        {
            return ExecuteToolAsync(new UpdateRecordTool(), BuildMutationServiceProvider(),
                new Dictionary<string, object?>
                {
                    { "entity", ENTITY_NAME },
                    { "keys", new Dictionary<string, object> { { "id", id } } },
                    { "fields", new Dictionary<string, object?> { { "content", xml } } }
                });
        }

        private static async Task DeleteXmlAsync(int id)
        {
            CallToolResult result = await ExecuteToolAsync(new DeleteRecordTool(), BuildMutationServiceProvider(),
                new Dictionary<string, object?>
                {
                    { "entity", ENTITY_NAME },
                    { "keys", new Dictionary<string, object> { { "id", id } } }
                });
            AssertSuccess(result, "Deleting the XML fixture should succeed.");
        }

    }
}