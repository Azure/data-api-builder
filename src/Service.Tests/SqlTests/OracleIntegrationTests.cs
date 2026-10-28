// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
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
using Azure.DataApiBuilder.Product;
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

    [DataTestMethod]
    [DataRow(false, null, null)]
    [DataRow(true, null, null)]
    [DataRow(false, "", null)]
    [DataRow(true, "", null)]
    [DataRow(false, "oracle-test-app", "oracle-test-app")]
    [DataRow(true, "oracle-test-app", "oracle-test-app")]
    [DataRow(false, "123456789012345678901234567890123456789012345678", "123456789012345678901234567890123456789012345678")]
    [DataRow(true, "1234567890123456789012345678901234567890123456789", "123456789012345678901234567890123456789012345678")]
    [DataRow(false, "12345678901234567890123456789012345678901234567é", "12345678901234567890123456789012345678901234567")]
    [DataRow(true, "12345678901234567890123456789012345678901234567é", "12345678901234567890123456789012345678901234567")]
    public async Task ConnectionsExposeApplicationNameAfterEveryOpen(bool asynchronous, string applicationName, string expected)
    {
        string previousName = Environment.GetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV);
        try
        {
            Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, applicationName);
            OracleMetadataConnectionProbe metadata = CreateMetadataProvider();
            foreach (string source in new[] { "executor", "metadata" })
            {
                using OracleConnection connection = source == "executor"
                    ? _fixture.Executor.CreateConnection(_fixture.SourceName)
                    : metadata.CreateTestConnection();
                connection.ConnectionString = _connectionString;
                for (int opening = 0; opening < 2; opening++)
                {
                    if (asynchronous)
                    {
                        await connection.OpenAsync();
                    }
                    else
                    {
                        connection.Open();
                    }

                    using OracleCommand command = connection.CreateCommand();
                    command.CommandText = "SELECT SYS_CONTEXT('USERENV', 'MODULE') FROM DUAL";
                    object actual = asynchronous ? await command.ExecuteScalarAsync() : command.ExecuteScalar();
                    Assert.AreEqual(expected ?? ProductInfo.DAB_USER_AGENT, actual, $"{source}, opening {opening}");
                    connection.Close();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, previousName);
        }
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

    [DataTestMethod]
    [DataRow("sql-json")]
    [DataRow("rest")]
    [DataRow("graphql")]
    public async Task ScalarSerializationPreservesTypesValuesAndNulls(string surface)
    {
        JsonElement rows = await ReadScalarSamplesAsync(surface);
        Assert.AreEqual(3, rows.GetArrayLength());
        JsonElement first = rows[0];
        Assert.AreEqual(1, first.GetProperty("id").GetInt32());
        AssertWallClock(first.GetProperty("date_value"), new DateTime(2024, 2, 29, 23, 59, 58));
        AssertWallClock(first.GetProperty("timestamp_value"), new DateTime(2024, 2, 29, 23, 59, 58).AddTicks(1234560));
        AssertOffsetTimestamp(first.GetProperty("offset_value"), new DateTimeOffset(2024, 2, 29, 23, 59, 58, TimeSpan.FromMinutes(345)).AddTicks(6543210));
        Assert.AreEqual(JsonValueKind.Number, first.GetProperty("double_value").ValueKind);
        Assert.AreEqual(1.25, first.GetProperty("double_value").GetDouble());
        Assert.AreEqual("A\"\\     ", first.GetProperty("char_value").GetString());
        Assert.AreEqual(JsonValueKind.Number, first.GetProperty("flag_value").ValueKind);
        Assert.AreEqual(1, first.GetProperty("flag_value").GetInt32());

        JsonElement second = rows[1];
        Assert.AreEqual(2, second.GetProperty("id").GetInt32());
        AssertWallClock(second.GetProperty("date_value"), new DateTime(2000, 1, 1, 0, 0, 1));
        AssertWallClock(second.GetProperty("timestamp_value"), new DateTime(2000, 1, 1, 0, 0, 1).AddTicks(10));
        AssertOffsetTimestamp(second.GetProperty("offset_value"), new DateTimeOffset(2000, 1, 1, 0, 0, 1, TimeSpan.FromHours(-7)).AddTicks(10));
        Assert.AreEqual(JsonValueKind.Number, second.GetProperty("double_value").ValueKind);
        Assert.AreEqual(-double.MaxValue, second.GetProperty("double_value").GetDouble());
        Assert.AreEqual("ABCDEFGH", second.GetProperty("char_value").GetString());
        Assert.AreEqual(JsonValueKind.Number, second.GetProperty("flag_value").ValueKind);
        Assert.AreEqual(0, second.GetProperty("flag_value").GetInt32());

        JsonElement empty = rows[2];
        Assert.AreEqual(3, empty.GetProperty("id").GetInt32());
        foreach (string field in new[] { "date_value", "timestamp_value", "offset_value", "double_value", "char_value", "flag_value" })
        {
            Assert.AreEqual(JsonValueKind.Null, empty.GetProperty(field).ValueKind, $"{surface}: {field} must remain null.");
        }
    }

    [DataTestMethod]
    [DataRow("sql-json", 0.0)]
    [DataRow("rest", 0.0)]
    [DataRow("graphql", 0.0)]
    [DataRow("sql-json", double.Epsilon)]
    [DataRow("rest", double.Epsilon)]
    [DataRow("graphql", double.Epsilon)]
    public async Task BinaryDoubleSerializationPreservesZeroAndSubnormalValues(string surface, double value)
    {
        using OracleConnection connection = new(_connectionString);
        await connection.OpenAsync();
        using OracleCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE DAB_ORACLE_SCALARS SET DOUBLE_VALUE = :value WHERE ID = 1";
        command.Parameters.Add("value", OracleDbType.BinaryDouble).Value = value;
        Assert.AreEqual(1, await command.ExecuteNonQueryAsync());
        JsonElement actual = (await ReadScalarSamplesAsync(surface))[0].GetProperty("double_value");
        Assert.AreEqual(JsonValueKind.Number, actual.ValueKind);
        Assert.AreEqual(value, actual.GetDouble());
    }

    [DataTestMethod]
    [DataRow("sql-json")]
    [DataRow("rest")]
    [DataRow("graphql")]
    public async Task EmptyOracleCharSerializesAsNull(string surface)
    {
        await ExecuteSql("UPDATE DAB_ORACLE_SCALARS SET CHAR_VALUE = '' WHERE ID = 1");
        Assert.AreEqual(JsonValueKind.Null, (await ReadScalarSamplesAsync(surface))[0].GetProperty("char_value").ValueKind);
    }

    [DataTestMethod]
    [DataRow("sql-json", "BINARY_DOUBLE_INFINITY", "Inf")]
    [DataRow("rest", "BINARY_DOUBLE_INFINITY", "Inf")]
    [DataRow("graphql", "BINARY_DOUBLE_INFINITY", "Inf")]
    [DataRow("sql-json", "-BINARY_DOUBLE_INFINITY", "-Inf")]
    [DataRow("rest", "-BINARY_DOUBLE_INFINITY", "-Inf")]
    [DataRow("graphql", "-BINARY_DOUBLE_INFINITY", "-Inf")]
    [DataRow("sql-json", "BINARY_DOUBLE_NAN", "Nan")]
    [DataRow("rest", "BINARY_DOUBLE_NAN", "Nan")]
    [DataRow("graphql", "BINARY_DOUBLE_NAN", "Nan")]
    public async Task NonFiniteBinaryDoublesHaveExplicitApiBehavior(string surface, string sqlValue, string expected)
    {
        // Oracle emits JSON strings for non-finite numbers; GraphQL Float cannot represent them.
        await ExecuteSql($"UPDATE DAB_ORACLE_SCALARS SET DOUBLE_VALUE = {sqlValue} WHERE ID = 1");
        if (surface == "graphql")
        {
            using HttpResponseMessage response = await HttpClient.PostAsJsonAsync("graphql", new
            {
                query = "{ scalarSamples(orderBy: { id: ASC }) { items { id double_value } } }"
            });
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement errors = body.RootElement.GetProperty("errors");
            Assert.AreEqual(1, errors.GetArrayLength());
            StringAssert.Contains(errors[0].GetProperty("message").GetString(), "double_value");
            StringAssert.Contains(errors[0].GetProperty("message").GetString(), "Float");
            Assert.AreEqual(JsonValueKind.Null, body.RootElement.GetProperty("data").GetProperty("scalarSamples").GetProperty("items")[0].GetProperty("double_value").ValueKind);
        }
        else
        {
            JsonElement actual = (await ReadScalarSamplesAsync(surface))[0].GetProperty("double_value");
            Assert.AreEqual(JsonValueKind.String, actual.ValueKind);
            Assert.AreEqual(expected, actual.GetString());
        }
    }

    private static void AssertWallClock(JsonElement value, DateTime expected)
    {
        Assert.AreEqual(JsonValueKind.String, value.ValueKind);
        DateTime actual = DateTime.Parse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.AreEqual(expected.Ticks, actual.Ticks, "Date/time fields must preserve wall-clock time and fractional seconds.");
    }

    private static void AssertOffsetTimestamp(JsonElement value, DateTimeOffset expected)
    {
        Assert.AreEqual(JsonValueKind.String, value.ValueKind);
        DateTimeOffset actual = DateTimeOffset.Parse(value.GetString(), CultureInfo.InvariantCulture);
        Assert.AreEqual(expected.UtcTicks, actual.UtcTicks, "The timestamp's instant must be preserved.");
        Assert.AreEqual(expected.Offset, actual.Offset, "The stored time-zone offset must be preserved.");
    }

    private async Task<JsonElement> ReadScalarSamplesAsync(string surface)
    {
        if (surface == "graphql")
        {
            JsonElement result = await ExecuteGraphQLRequestAsync(
                "{ scalarSamples(orderBy: { id: ASC }) { items { id date_value timestamp_value offset_value double_value char_value flag_value } } }",
                "scalarSamples", isAuthenticated: false);
            return result.GetProperty("items");
        }

        if (surface == "rest")
        {
            using JsonDocument result = JsonDocument.Parse(await HttpClient.GetStringAsync("api/ScalarSample?$orderby=id"));
            return result.RootElement.GetProperty("value").Clone();
        }

        // Use the production builder and discovered metadata, not a hand-written JSON projection.
        FindRequestContext context = new("ScalarSample", _sqlMetadataProvider.EntityToDatabaseObject["ScalarSample"], true)
        {
            First = 10,
            FieldsToBeReturned = new() { "id", "date_value", "timestamp_value", "offset_value", "double_value", "char_value", "flag_value" }
        };
        SqlQueryStructure query = new(context, _sqlMetadataProvider, _fixture.Authorization.Object, _fixture.Config, _fixture.Filter, _fixture.Http.HttpContext);
        using JsonDocument json = await _fixture.Executor.ExecuteQueryAsync(_fixture.Builder.Build(query), query.Parameters,
            _fixture.Executor.GetJsonResultAsync<JsonDocument>, dataSourceName: _fixture.SourceName);
        return json.RootElement.Clone();
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
        OracleMetadataProvider metadata = CreateMetadataProvider();
        await metadata.InitializeAsync();
        SourceDefinition definition = metadata.GetSourceDefinition(OracleTestFixture.ENTITY);
        CollectionAssert.AreEqual(new[] { "ID" }, definition.PrimaryKey.ToArray());
        Assert.IsTrue(definition.Columns["ID"].IsAutoGenerated);
        Assert.IsFalse(definition.Columns["ID"].IsReadOnly);
        Assert.IsTrue(definition.Columns["TITLE"].HasDefault);
        Assert.AreEqual(typeof(string), definition.Columns["TITLE"].SystemType);
        Assert.IsTrue(definition.Columns["DOUBLED_ID"].IsReadOnly);
    }

    [DataTestMethod]
    [DataRow("RAW(16)")]
    [DataRow("BLOB")]
    [DataRow("LONG RAW")]
    public async Task BinaryColumnsAreRejectedDuringMetadataInitialization(string sqlType)
    {
        await ExecuteSql($"DROP TABLE {_tableName} PURGE");
        await ExecuteSql($"CREATE TABLE {_tableName} (ID NUMBER PRIMARY KEY, PAYLOAD {sqlType})");
        OracleMetadataProvider metadata = CreateMetadataProvider();
        NotSupportedException error = await Assert.ThrowsExceptionAsync<NotSupportedException>(() => metadata.InitializeAsync());
        StringAssert.Contains(error.Message, "PAYLOAD");
        StringAssert.Contains(error.Message, "binary");
        StringAssert.Contains(error.Message, "view");
    }

    [DataTestMethod]
    [DataRow("ALWAYS", true)]
    [DataRow("BY DEFAULT", false)]
    [DataRow("BY DEFAULT ON NULL", false)]
    public async Task IdentityMetadataRespectsGenerationType(string generationType, bool readOnly)
    {
        await ExecuteSql($"DROP TABLE {_tableName} PURGE");
        await ExecuteSql($"CREATE TABLE {_tableName} (ID NUMBER GENERATED {generationType} AS IDENTITY PRIMARY KEY, TITLE CLOB, DOUBLED_ID GENERATED ALWAYS AS (ID * 2))");
        OracleMetadataProvider metadata = CreateMetadataProvider();
        await metadata.InitializeAsync();
        SourceDefinition definition = metadata.GetSourceDefinition(OracleTestFixture.ENTITY);
        CollectionAssert.AreEqual(new[] { "ID" }, definition.PrimaryKey.ToArray());
        ColumnDefinition identity = definition.Columns["ID"];
        Assert.IsTrue(identity.IsAutoGenerated);
        Assert.AreEqual(readOnly, identity.IsReadOnly);
        Assert.IsTrue(metadata.GetSourceDefinition(OracleTestFixture.ENTITY).Columns["DOUBLED_ID"].IsReadOnly);
    }

    [DataTestMethod]
    [DataRow("BY DEFAULT")]
    [DataRow("BY DEFAULT ON NULL")]
    public async Task DefaultIdentityAcceptsExplicitAndGeneratedIdsThroughApis(string generationType)
    {
        await ExecuteSql($"ALTER TABLE BOOKS MODIFY ID GENERATED {generationType} AS IDENTITY");
        using HttpResponseMessage explicitId = await HttpClient.PostAsJsonAsync("api/Book", new { id = 42, title = "Explicit", publisher_id = 1234 });
        Assert.AreEqual(HttpStatusCode.Created, explicitId.StatusCode, await explicitId.Content.ReadAsStringAsync());
        using JsonDocument explicitBody = JsonDocument.Parse(await explicitId.Content.ReadAsStringAsync());
        Assert.AreEqual(42, explicitBody.RootElement.GetProperty("value")[0].GetProperty("id").GetInt32());

        using HttpResponseMessage generatedId = await HttpClient.PostAsJsonAsync("api/Book", new { title = "Generated", publisher_id = 1234 });
        Assert.AreEqual(HttpStatusCode.Created, generatedId.StatusCode, await generatedId.Content.ReadAsStringAsync());
        using JsonDocument generatedBody = JsonDocument.Parse(await generatedId.Content.ReadAsStringAsync());
        // ON NULL evaluates the sequence default even for the preceding explicit-ID insert.
        int expectedGeneratedId = generationType == "BY DEFAULT ON NULL" ? 1001 : 1000;
        Assert.AreEqual(expectedGeneratedId, generatedBody.RootElement.GetProperty("value")[0].GetProperty("id").GetInt32());

        JsonElement graphQL = await ExecuteGraphQLRequestAsync(
            "mutation { createBook(item: { id: 43, title: \"Explicit GraphQL\", publisher_id: 1234 }) { id } }",
            "createBook", isAuthenticated: false);
        Assert.AreEqual(43, graphQL.GetProperty("id").GetInt32());
        using JsonDocument persisted = JsonDocument.Parse(await GetDatabaseResultAsync($"SELECT JSON_OBJECT('count' VALUE COUNT(*)) FROM BOOKS WHERE ID IN (42, 43, {expectedGeneratedId})"));
        Assert.AreEqual(3, persisted.RootElement.GetProperty("count").GetInt32());
    }

    [TestMethod]
    public async Task IdentityMetadataPreservesCompositePrimaryKeyOrder()
    {
        await ExecuteSql($"DROP TABLE {_tableName} PURGE");
        await ExecuteSql($"CREATE TABLE {_tableName} (ID NUMBER GENERATED BY DEFAULT ON NULL AS IDENTITY, TENANT_ID NUMBER, PRIMARY KEY (TENANT_ID, ID))");
        OracleMetadataProvider metadata = CreateMetadataProvider();
        await metadata.InitializeAsync();
        SourceDefinition definition = metadata.GetSourceDefinition(OracleTestFixture.ENTITY);
        CollectionAssert.AreEqual(new[] { "TENANT_ID", "ID" }, definition.PrimaryKey.ToArray());
        Assert.IsFalse(definition.Columns["ID"].IsReadOnly);
    }

    [TestMethod]
    public async Task MetadataWithoutPrimaryKeyStillFailsInitialization()
    {
        await ExecuteSql($"DROP TABLE {_tableName} PURGE");
        await ExecuteSql($"CREATE TABLE {_tableName} (ID NUMBER, TITLE VARCHAR2(100))");
        OracleMetadataProvider metadata = CreateMetadataProvider();
        DataApiBuilderException error = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() => metadata.InitializeAsync());
        StringAssert.Contains(error.Message, "Primary key not configured");
    }

    private OracleMetadataConnectionProbe CreateMetadataProvider()
    {
        // These tests alter the same table between metadata initializations. Discard
        // pooled Oracle schema/statement caches so the adapter sees the new definition.
        OracleConnection.ClearAllPools();
        Entity entity = new(new(_tableName, EntitySourceType.Table, null, null), new("Book", "Books"), null, new(),
            new[] { new EntityPermission("anonymous", new[] { new EntityAction(EntityActionOperation.All, null, null) }) }, null, null);
        RuntimeConfig config = new(Schema: string.Empty, DataSource: new(DatabaseType.Oracle, _connectionString),
            Entities: new(new Dictionary<string, Entity> { [OracleTestFixture.ENTITY] = entity }));
        RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
        RuntimeConfigValidator validator = new(provider, new MockFileSystem(), NullLogger<RuntimeConfigValidator>.Instance);
        QueryManagerFactory factory = new(provider, NullLogger<IQueryExecutor>.Instance, _fixture.Http, null);
        return new(provider, validator, factory);
    }

    private sealed class OracleMetadataConnectionProbe : OracleMetadataProvider
    {
        public OracleMetadataConnectionProbe(RuntimeConfigProvider provider, RuntimeConfigValidator validator, QueryManagerFactory factory)
            : base(provider, validator, factory, NullLogger<ISqlMetadataProvider>.Instance, provider.GetConfig().DefaultDataSourceName)
        {
        }

        public OracleConnection CreateTestConnection() => CreateConnection();
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
