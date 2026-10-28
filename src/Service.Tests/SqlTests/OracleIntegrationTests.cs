// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Transactions;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.UnitTests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests;

/// <summary>
/// Oracle engine and provider integration tests using the shared SQL fixture and schema.
/// DAB_ORACLE_TEST_CONNECTION_STRING must identify a dedicated test user with CREATE TABLE privileges.
/// </summary>
[TestClass, TestCategory(TestCategory.ORACLE)]
[DoNotParallelize]
public class OracleIntegrationTests : SqlTestBase
{
    private OracleTestFixture _fixture;
    private string _connectionString;
    private string _tableName;

    [ClassInitialize]
    public static async Task SetupAsync(TestContext context)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DAB_ORACLE_TEST_CONNECTION_STRING")),
            "Set DAB_ORACLE_TEST_CONNECTION_STRING to a dedicated Oracle test schema before running TestCategory=Oracle.");
        DatabaseEngine = TestCategory.ORACLE;
        _queryEngineFactory = new Mock<IQueryEngineFactory>();
        await InitializeTestFixture();
    }

    [ClassCleanup]
    public static void CleanupFixture()
    {
        _application?.Dispose();
        TestHelper.UnsetAllDABEnvironmentVariables();
    }

    [TestInitialize]
    public async Task Initialize()
    {
        TestHelper.SetupDatabaseEnvironment(TestCategory.ORACLE);
        _connectionString = SqlTestHelper.SetupRuntimeConfig().DataSource.ConnectionString;
        _tableName = OracleTestFixture.TABLE;
        _fixture = new(_connectionString, _tableName);
        await ResetDbStateAsync();
    }

    [TestMethod]
    public async Task CrudReturnsRowsAndClobJsonBeyondFourKilobytes()
    {
        string title = new('x', 6000);
        SqlInsertStructure insert = _fixture.Insert(10, title);
        DbResultSet inserted = await Mutate(insert, _fixture.Builder.Build(insert));
        Assert.AreEqual(10m, Convert.ToDecimal(inserted.Rows.Single().Columns["ID"]));
        Assert.AreEqual(title, inserted.Rows.Single().Columns["TITLE"]);

        SqlQueryStructure read = _fixture.Read();
        using JsonDocument json = await Read(read);
        Assert.AreEqual(title, json.RootElement[0].GetProperty("TITLE").GetString());

        SqlUpdateStructure update = new(OracleTestFixture.ENTITY, _fixture.Metadata.Object, _fixture.Authorization.Object, _fixture.Filter,
            new Dictionary<string, object?> { ["ID"] = 10m, ["TITLE"] = "updated" }, _fixture.Http.HttpContext, true);
        DbResultSet updated = await Mutate(update, _fixture.Builder.Build(update));
        Assert.AreEqual("updated", updated.Rows.Single().Columns["TITLE"]);

        SqlDeleteStructure delete = _fixture.Delete(10);
        Dictionary<string, object> deletion = await _fixture.Executor.ExecuteQueryAsync(_fixture.Builder.Build(delete), delete.Parameters,
            _fixture.Executor.GetResultPropertiesAsync, dataSourceName: _fixture.SourceName);
        Assert.AreEqual(1, deletion["RecordsAffected"]);
        using JsonDocument empty = await Read(_fixture.Read());
        Assert.AreEqual(0, empty.RootElement.GetArrayLength());
    }

    [TestMethod]
    public async Task UpsertDistinguishesInsertUpdateAndPolicyFailure()
    {
        DbResultSet inserted = await Upsert(7, "new");
        Assert.AreEqual(false, inserted.ResultProperties[SqlMutationEngine.IS_UPDATE_RESULT_SET]);
        DbResultSet updated = await Upsert(7, "changed");
        Assert.AreEqual(true, updated.ResultProperties[SqlMutationEngine.IS_UPDATE_RESULT_SET]);
        Assert.AreEqual("changed", updated.Rows.Single().Columns["TITLE"]);

        SqlUpsertQueryStructure denied = _fixture.Upsert(7, "denied");
        denied.DbPolicyPredicatesForOperations[EntityActionOperation.Update] = "1 = 0";
        DataApiBuilderException error = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() =>
            _fixture.Executor.ExecuteQueryAsync(_fixture.Builder.Build(denied), denied.Parameters,
                _fixture.Executor.GetMultipleResultSetsIfAnyAsync, dataSourceName: _fixture.SourceName));
        Assert.AreEqual(HttpStatusCode.Forbidden, error.StatusCode);
        using JsonDocument json = await Read(_fixture.Read());
        Assert.AreEqual("changed", json.RootElement[0].GetProperty("TITLE").GetString());
    }

    [TestMethod]
    public async Task ConcurrentUpsertsReturnOneInsertAndOneUpdate()
    {
        DbResultSet[] results = await Task.WhenAll(Upsert(9, "first"), Upsert(9, "second"));
        Assert.AreEqual(1, results.Count(r => !(bool)r.ResultProperties[SqlMutationEngine.IS_UPDATE_RESULT_SET]));
        Assert.AreEqual(1, results.Count(r => (bool)r.ResultProperties[SqlMutationEngine.IS_UPDATE_RESULT_SET]));
        using JsonDocument json = await Read(_fixture.Read());
        Assert.AreEqual(1, json.RootElement.GetArrayLength());
    }

    [TestMethod]
    public async Task MutationsParticipateInAmbientTransactionRollback()
    {
        using (TransactionScope scope = new(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
        {
            SqlInsertStructure insert = _fixture.Insert(88, "rollback");
            await Mutate(insert, _fixture.Builder.Build(insert));
        }

        using JsonDocument json = await Read(_fixture.Read());
        Assert.AreEqual(0, json.RootElement.GetArrayLength());
    }

    [TestMethod]
    public async Task ReadSupportsKeysetPaginationAndCorrelatedRelationships()
    {
        await Upsert(1, "one");
        await Upsert(2, "two");
        await Upsert(3, "three");
        SqlQueryStructure parent = _fixture.Read();
        parent.Parameters["@after"] = new(1m, DbType.Decimal);
        parent.PaginationMetadata.PaginationPredicate = new(new()
        {
            new("DAB", _tableName, "ID", 1m, parent.SourceAlias, paramName: "@after")
        });
        SqlQueryStructure child = _fixture.Read();
        child.SourceAlias = "child";
        foreach (Column column in child.Columns.Concat<Column>(child.OrderByColumns))
        {
            column.TableAlias = "child";
        }

        child.Predicates.Add(new(new(new Column("DAB", _tableName, "ID", "child")), PredicateOperation.Equal,
            new(new Column("DAB", _tableName, "ID", parent.SourceAlias))));
        parent.JoinQueries.Add("children", child);
        parent.Columns.Add(new("", "", SqlQueryStructure.DATA_IDENT, "children", "children"));
        using JsonDocument json = await Read(parent);
        Assert.AreEqual(2, json.RootElement.GetArrayLength());
        Assert.AreEqual(2, json.RootElement[0].GetProperty("ID").GetInt32());
        Assert.AreEqual(3, json.RootElement[1].GetProperty("ID").GetInt32());
        Assert.AreEqual(1, json.RootElement[0].GetProperty("children").GetArrayLength());
        Assert.AreEqual("two", json.RootElement[0].GetProperty("children")[0].GetProperty("TITLE").GetString());
    }

    [TestMethod]
    public async Task MetadataDiscoversPrimaryKeyIdentityDefaultsAndClob()
    {
        Entity entity = new(new(_tableName, EntitySourceType.Table, null, null), new("Book", "Books"), null, new(),
            new[] { new EntityPermission("anonymous", new[] { new EntityAction(EntityActionOperation.All, null, null) }) }, null, null);
        RuntimeConfig config = new(Schema: string.Empty, DataSource: new(DatabaseType.Oracle, _connectionString),
            Entities: new(new Dictionary<string, Entity> { [OracleTestFixture.ENTITY] = entity }));
        RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
        RuntimeConfigValidator validator = new(provider, new MockFileSystem(), NullLogger<RuntimeConfigValidator>.Instance);
        QueryManagerFactory factory = new(provider, NullLogger<IQueryExecutor>.Instance, _fixture.Http, null);
        OracleMetadataProvider metadata = new(provider, validator, factory, NullLogger<ISqlMetadataProvider>.Instance, provider.GetConfig().DefaultDataSourceName);
        await metadata.InitializeAsync();
        SourceDefinition definition = metadata.GetSourceDefinition(OracleTestFixture.ENTITY);
        CollectionAssert.AreEqual(new[] { "ID" }, definition.PrimaryKey.ToArray());
        Assert.IsTrue(definition.Columns["ID"].IsAutoGenerated);
        Assert.IsTrue(definition.Columns["ID"].IsReadOnly);
        Assert.IsTrue(definition.Columns["TITLE"].HasDefault);
        Assert.AreEqual(typeof(string), definition.Columns["TITLE"].SystemType);
        Assert.IsTrue(definition.Columns["DOUBLED_ID"].IsReadOnly);
    }

    [TestMethod]
    public async Task ForeignKeyDiscoveryReturnsReferencedAndReferencingColumns()
    {
        await ExecuteSql($"ALTER TABLE {_tableName} ADD PARENT_ID NUMBER REFERENCES {_tableName}(ID)");
        DbResultSet keys = await _fixture.Executor.ExecuteQueryAsync(_fixture.Builder.BuildForeignKeyInfoQuery(1),
            new Dictionary<string, DbConnectionParam>
            {
                ["schemaName0"] = new(_fixture.Table.SchemaName, DbType.String),
                ["tableName0"] = new(_tableName, DbType.String)
            }, _fixture.Executor.ExtractResultSetFromDbDataReaderAsync, dataSourceName: _fixture.SourceName);
        Dictionary<string, object?> row = keys.Rows.Single().Columns;
        Assert.AreEqual("PARENT_ID", row["ReferencingColumns"]);
        Assert.AreEqual("ID", row["ReferencedColumns"]);
        Assert.AreEqual(_tableName, row["ReferencingSourceDefinition"]);
        Assert.AreEqual(_tableName, row["ReferencedSourceDefinition"]);
    }

    [TestMethod]
    public async Task InsertWithOnlyDefaultsReturnsGeneratedKey()
    {
        SqlInsertStructure insert = new(OracleTestFixture.ENTITY, _fixture.Metadata.Object, _fixture.Authorization.Object, _fixture.Filter,
            new Dictionary<string, object?>(), _fixture.Http.HttpContext);
        DbResultSet result = await Mutate(insert, _fixture.Builder.Build(insert));
        Assert.AreEqual(1m, Convert.ToDecimal(result.Rows.Single().Columns["ID"]));
        Assert.AreEqual("untitled", result.Rows.Single().Columns["TITLE"]);
    }

    [TestMethod]
    public async Task RestReadFiltersAndMapsColumns()
    {
        await SetupAndRunRestApiTest(
            primaryKeyRoute: null,
            queryString: "?$filter=publisher_id eq 1234&$orderby=id desc&$select=id,title",
            entityNameOrPath: "Book",
            sqlQuery: """
                SELECT JSON_ARRAYAGG(JSON_OBJECT('id' VALUE ID, 'title' VALUE TITLE) ORDER BY ID DESC RETURNING CLOB)
                FROM BOOKS WHERE PUBLISHER_ID = 1234
                """);
    }

    [TestMethod]
    public async Task RestCrudPersistsChanges()
    {
        using HttpResponseMessage created = await HttpClient.PostAsJsonAsync("api/Book", new { title = "REST book", publisher_id = 1234 });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        using JsonDocument createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        int id = createdBody.RootElement.GetProperty("value")[0].GetProperty("id").GetInt32();
        Assert.AreEqual(1000, id);
        StringAssert.EndsWith(created.Headers.Location.ToString(), $"id/{id}");

        using HttpResponseMessage patched = await HttpClient.PatchAsJsonAsync($"api/Book/id/{id}", new { title = "Updated REST book" });
        Assert.AreEqual(HttpStatusCode.OK, patched.StatusCode, await patched.Content.ReadAsStringAsync());
        await SetupAndRunRestApiTest(
            primaryKeyRoute: $"id/{id}", queryString: null, entityNameOrPath: "Book",
            sqlQuery: $"SELECT JSON_OBJECT('id' VALUE ID, 'title' VALUE TITLE, 'publisher_id' VALUE PUBLISHER_ID RETURNING CLOB) FROM BOOKS WHERE ID = {id}");
        using JsonDocument row = JsonDocument.Parse(await GetDatabaseResultAsync($"SELECT JSON_OBJECT('title' VALUE TITLE) FROM BOOKS WHERE ID = {id}"));
        Assert.AreEqual("Updated REST book", row.RootElement.GetProperty("title").GetString());

        using HttpResponseMessage deleted = await HttpClient.DeleteAsync($"api/Book/id/{id}");
        Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode, await deleted.Content.ReadAsStringAsync());
        using JsonDocument count = JsonDocument.Parse(await GetDatabaseResultAsync($"SELECT JSON_OBJECT('count' VALUE COUNT(*)) FROM BOOKS WHERE ID = {id}"));
        Assert.AreEqual(0, count.RootElement.GetProperty("count").GetInt32());
    }

    [TestMethod]
    public async Task RestPaginationReturnsRemainingRows()
    {
        using JsonDocument first = JsonDocument.Parse(await HttpClient.GetStringAsync("api/Book?$first=2&$orderby=id"));
        CollectionAssert.AreEqual(new[] { 1, 2 }, first.RootElement.GetProperty("value").EnumerateArray().Select(row => row.GetProperty("id").GetInt32()).ToArray());
        string nextLink = first.RootElement.GetProperty("nextLink").GetString();
        Assert.IsFalse(string.IsNullOrEmpty(nextLink));
        using JsonDocument last = JsonDocument.Parse(await HttpClient.GetStringAsync(nextLink));
        Assert.AreEqual(1, last.RootElement.GetProperty("value").GetArrayLength());
        Assert.AreEqual(3, last.RootElement.GetProperty("value")[0].GetProperty("id").GetInt32());
        Assert.IsFalse(last.RootElement.TryGetProperty("nextLink", out _));
    }

    [TestMethod]
    public async Task RestReadOnlyPermissionRejectsMutation()
    {
        using HttpResponseMessage response = await HttpClient.PostAsJsonAsync("api/Publisher", new { id = 99, name = "Denied" });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, await response.Content.ReadAsStringAsync());
        using JsonDocument count = JsonDocument.Parse(await GetDatabaseResultAsync("SELECT JSON_OBJECT('count' VALUE COUNT(*)) FROM PUBLISHERS WHERE ID = 99"));
        Assert.AreEqual(0, count.RootElement.GetProperty("count").GetInt32());
    }

    [TestMethod]
    public async Task GraphQLReadResolvesRelationshipsAndPagination()
    {
        JsonElement first = await ExecuteGraphQLRequestAsync(
            "{ books(first: 1, orderBy: { id: ASC }) { items { id title publisher { name } } endCursor hasNextPage } }",
            "books", isAuthenticated: false);
        Assert.AreEqual(1, first.GetProperty("items").GetArrayLength());
        Assert.AreEqual(1, first.GetProperty("items")[0].GetProperty("id").GetInt32());
        Assert.AreEqual("Big Company", first.GetProperty("items")[0].GetProperty("publisher").GetProperty("name").GetString());
        Assert.IsTrue(first.GetProperty("hasNextPage").GetBoolean());
        string cursor = first.GetProperty("endCursor").GetString();
        Assert.IsFalse(string.IsNullOrEmpty(cursor));

        JsonElement last = await ExecuteGraphQLRequestAsync(
            "query($after: String!) { books(first: 2, after: $after, orderBy: { id: ASC }) { items { id } hasNextPage } }",
            "books", isAuthenticated: false, variables: new() { ["after"] = cursor });
        CollectionAssert.AreEqual(new[] { 2, 3 }, last.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetInt32()).ToArray());
        Assert.IsFalse(last.GetProperty("hasNextPage").GetBoolean());

        JsonElement publishers = await ExecuteGraphQLRequestAsync(
            "{ publishers(filter: { id: { eq: 1234 } }) { items { books(orderBy: { id: ASC }) { items { id } } } } }",
            "publishers", isAuthenticated: false);
        CollectionAssert.AreEqual(new[] { 1, 2 }, publishers.GetProperty("items")[0].GetProperty("books").GetProperty("items")
            .EnumerateArray().Select(row => row.GetProperty("id").GetInt32()).ToArray());
    }

    [TestMethod]
    public async Task GraphQLMutationsPersistChanges()
    {
        JsonElement created = await ExecuteGraphQLRequestAsync(
            "mutation { createBook(item: { title: \"GraphQL book\", publisher_id: 1234 }) { id title } }",
            "createBook", isAuthenticated: false);
        int id = created.GetProperty("id").GetInt32();
        Assert.AreEqual(1000, id);
        Assert.AreEqual("GraphQL book", created.GetProperty("title").GetString());

        JsonElement updated = await ExecuteGraphQLRequestAsync(
            $"mutation {{ updateBook(id: {id}, item: {{ title: \"Updated GraphQL book\" }}) {{ id title }} }}",
            "updateBook", isAuthenticated: false);
        Assert.AreEqual(id, updated.GetProperty("id").GetInt32());
        Assert.AreEqual("Updated GraphQL book", updated.GetProperty("title").GetString());
        string persisted = await GetDatabaseResultAsync($"SELECT JSON_OBJECT('id' VALUE ID, 'title' VALUE TITLE) FROM BOOKS WHERE ID = {id}");
        SqlTestHelper.PerformTestEqualJsonStrings(persisted, updated.ToString());

        JsonElement deleted = await ExecuteGraphQLRequestAsync(
            $"mutation {{ deleteBook(id: {id}) {{ id }} }}", "deleteBook", isAuthenticated: false);
        Assert.AreEqual(id, deleted.GetProperty("id").GetInt32());
        using JsonDocument count = JsonDocument.Parse(await GetDatabaseResultAsync($"SELECT JSON_OBJECT('count' VALUE COUNT(*)) FROM BOOKS WHERE ID = {id}"));
        Assert.AreEqual(0, count.RootElement.GetProperty("count").GetInt32());
    }

    public static IEnumerable<object[]> SpecialCharacterStringFilterCases()
    {
        (string Literal, string NearMiss)[] values =
        {
            ("%", "X"),
            ("_", "X"),
            (@"\", ""),
            (@"\\", @"\"),
            ("[", "x"),
            ("]", "x"),
            (@"\[%_]\", "[XY]"),
            ("O'Reilly_100%", "O'ReillyX100")
        };
        foreach (string operation in new[] { "contains", "notContains", "startsWith", "endsWith" })
        {
            foreach ((string literal, string nearMiss) in values)
            {
                yield return new object[] { operation, literal, nearMiss };
            }
        }
    }

    [DataTestMethod]
    [DynamicData(nameof(SpecialCharacterStringFilterCases), DynamicDataSourceType.Method)]
    public async Task GraphQLStringFiltersTreatSpecialCharactersLiterally(string operation, string literal, string nearMiss)
    {
        await SetBookTitlesAsync($"before{literal}after", $"{literal}after", $"before{literal}", $"before{nearMiss}after");
        int[] expected = operation switch
        {
            "contains" => new[] { 1, 2, 3 },
            "notContains" => new[] { 4 },
            "startsWith" => new[] { 2 },
            "endsWith" => new[] { 3 },
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        await AssertStringFilterIdsAsync(operation, literal, expected);
    }

    [DataTestMethod]
    [DataRow("contains")]
    [DataRow("notContains")]
    [DataRow("startsWith")]
    [DataRow("endsWith")]
    public async Task GraphQLStringFiltersWithEmptySearchExcludeNulls(string operation)
    {
        await ExecuteSql("ALTER TABLE BOOKS MODIFY TITLE NULL");
        await SetBookTitlesAsync("plain", "100%_", @"[x]\", null);

        await AssertStringFilterIdsAsync(operation, "", operation == "notContains" ? Array.Empty<int>() : new[] { 1, 2, 3 });
    }

    [DataTestMethod]
    [DataRow("eq")]
    [DataRow("neq")]
    public async Task GraphQLStringEqualityPreservesSpecialCharacters(string operation)
    {
        const string literal = @"O'Reilly_[100%]\";
        await SetBookTitlesAsync(literal, "O'ReillyX100", $"before{literal}");

        await AssertStringFilterIdsAsync(operation, literal, operation == "eq" ? new[] { 1 } : new[] { 2, 3 });
    }

    [TestMethod]
    public async Task GraphQLStringFiltersEscapeLiteralsAndNestedPredicates()
    {
        const string literal = @"\[%_]\";
        await SetBookTitlesAsync($"before{literal}after", "other", "beforeXYafter");
        JsonElement books = await ExecuteGraphQLRequestAsync(
            $"{{ books(filter: {{ or: [{{ title: {{ contains: {JsonSerializer.Serialize(literal)} }} }}, {{ title: {{ eq: \"other\" }} }}] }}, orderBy: {{ id: ASC }}) {{ items {{ id }} }} }}",
            "books", isAuthenticated: false);
        CollectionAssert.AreEqual(new[] { 1, 2 }, books.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetInt32()).ToArray());

        JsonElement publishers = await ExecuteGraphQLRequestAsync(
            "query($value: String!) { publishers(filter: { books: { title: { contains: $value } } }) { items { id } } }",
            "publishers", isAuthenticated: false, variables: new() { ["value"] = literal });
        CollectionAssert.AreEqual(new[] { 1234 }, publishers.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetInt32()).ToArray());
    }

    private async Task AssertStringFilterIdsAsync(string operation, string value, int[] expected)
    {
        JsonElement result = await ExecuteGraphQLRequestAsync(
            $"query($value: String!) {{ books(filter: {{ title: {{ {operation}: $value }} }}, orderBy: {{ id: ASC }}) {{ items {{ id }} }} }}",
            "books", isAuthenticated: false, variables: new() { ["value"] = value });
        CollectionAssert.AreEqual(expected, result.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetInt32()).ToArray(),
            $"Unexpected matches for {operation}({JsonSerializer.Serialize(value)}).");
    }

    private async Task SetBookTitlesAsync(params string?[] titles)
    {
        await ExecuteSql("DELETE FROM BOOKS");
        using OracleConnection connection = new(_connectionString);
        await connection.OpenAsync();
        using OracleCommand command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = "INSERT INTO BOOKS (ID, TITLE, PUBLISHER_ID) VALUES (:id, :title, 1234)";
        command.Parameters.Add("id", OracleDbType.Int32);
        command.Parameters.Add("title", OracleDbType.Varchar2);
        for (int i = 0; i < titles.Length; i++)
        {
            command.Parameters["id"].Value = i + 1;
            command.Parameters["title"].Value = (object?)titles[i] ?? DBNull.Value;
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task ExecuteSql(string sql)
    {
        using OracleConnection connection = new(_connectionString);
        await connection.OpenAsync();
        using OracleCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private Task<DbResultSet> Mutate(BaseSqlQueryStructure structure, string sql) =>
        _fixture.Executor.ExecuteQueryAsync(sql, structure.Parameters, _fixture.Executor.ExtractResultSetFromDbDataReaderAsync, dataSourceName: _fixture.SourceName);

    private Task<DbResultSet> Upsert(int id, string title)
    {
        SqlUpsertQueryStructure structure = _fixture.Upsert(id, title);
        return _fixture.Executor.ExecuteQueryAsync(_fixture.Builder.Build(structure), structure.Parameters,
            _fixture.Executor.GetMultipleResultSetsIfAnyAsync, dataSourceName: _fixture.SourceName);
    }

    private Task<JsonDocument> Read(SqlQueryStructure structure) =>
        _fixture.Executor.ExecuteQueryAsync(_fixture.Builder.Build(structure), structure.Parameters,
            _fixture.Executor.GetJsonResultAsync<JsonDocument>, dataSourceName: _fixture.SourceName);
}
