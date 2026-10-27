// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Oracle.ManagedDataAccess.Client;
using HealthCheckUtilities = Azure.DataApiBuilder.Service.HealthCheck.Utilities;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

[TestClass, TestCategory("OracleUnit")]
public class OracleProviderTests
{
    [TestMethod]
    public void ConfigurationRoundTripSelectsOracleProviders()
    {
        OracleTestFixture fixture = new();
        RuntimeConfig config = fixture.Config.GetConfig();
        StringAssert.Contains(config.ToJson(), "\"database-type\": \"oracle\"");
        Assert.IsTrue(config.SqlDataSourceUsed);
        QueryManagerFactory factory = new(fixture.Config, NullLogger<IQueryExecutor>.Instance, fixture.Http, null);
        Assert.IsInstanceOfType(factory.GetQueryBuilder(DatabaseType.Oracle), typeof(OracleQueryBuilder));
        Assert.IsInstanceOfType(factory.GetQueryExecutor(DatabaseType.Oracle), typeof(OracleQueryExecutor));
        Assert.IsInstanceOfType(factory.GetDbExceptionParser(DatabaseType.Oracle), typeof(OracleDbExceptionParser));
        Assert.AreEqual("SELECT 1 FROM DUAL", HealthCheckUtilities.GetDatSourceQuery(DatabaseType.Oracle));
        Assert.AreSame(OracleClientFactory.Instance, HealthCheckUtilities.GetDbProviderFactory(DatabaseType.Oracle));
    }

    [TestMethod]
    public void CommandBindsRepeatedParametersByNameAndPreservesQuotedText()
    {
        OracleTestFixture fixture = new();
        using OracleConnection connection = new();
        const string sql = "SELECT '@literal', \"@identifier\", q'[it's @literal]', :second FROM DUAL " +
            "WHERE @first = @first /* @comment */ -- @line\n";
        using OracleCommand command = (OracleCommand)fixture.Executor.PrepareDbCommand(connection, sql,
            new Dictionary<string, DbConnectionParam>
            {
                ["unused"] = new(9),
                ["second"] = new(null, DbType.String),
                ["@first"] = new(42, DbType.Int32)
            }, null, fixture.SourceName);
        Assert.IsTrue(command.BindByName);
        Assert.AreEqual(sql.Replace("@first", ":first", StringComparison.Ordinal), command.CommandText);
        Assert.AreEqual(2, command.Parameters.Count);
        Assert.AreEqual(DBNull.Value, command.Parameters["second"].Value);
        Assert.AreEqual(42, command.Parameters["first"].Value);
        Assert.AreEqual(OracleDbType.Int32, command.Parameters["first"].OracleDbType);
    }

    [TestMethod]
    public void CommandWithoutParametersSupportsSchemaAndDiagnosticQueries()
    {
        OracleTestFixture fixture = new();
        using OracleConnection connection = new();
        const string sql = "SELECT 1 FROM DUAL";
        using OracleCommand command = (OracleCommand)fixture.Executor.PrepareDbCommand(connection, sql, null, null, fixture.SourceName);
        Assert.AreEqual(sql, command.CommandText);
        Assert.AreEqual(0, command.Parameters.Count);
        Assert.IsTrue(command.BindByName);
    }

    [TestMethod]
    public void ReadUsesOrderedClobJsonAndOraclePagination()
    {
        OracleTestFixture fixture = new();
        SqlQueryStructure query = fixture.Read();
        query.DbPolicyPredicatesForOperations[EntityActionOperation.Read] = "\"ID\" > 0";
        string sql = fixture.Builder.Build(query);
        StringAssert.Contains(sql, "JSON_ARRAYAGG(\"data\" FORMAT JSON ORDER BY dab_order RETURNING CLOB)");
        StringAssert.Contains(sql, "JSON_ARRAY(RETURNING CLOB)");
        StringAssert.Contains(sql, "'TITLE' VALUE \"DAB_DAB_ORACLE_BOOKS\".\"TITLE\"");
        StringAssert.Contains(sql, "FETCH FIRST 3 ROWS ONLY");
        StringAssert.Contains(sql, "WHERE \"ID\" > 0");
        Assert.IsFalse(sql.Contains(" AS \"DAB_DAB_ORACLE_BOOKS\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void NestedReadEmbedsJsonWithoutQuotingIt()
    {
        OracleTestFixture fixture = new();
        SqlQueryStructure parent = fixture.Read();
        SqlQueryStructure child = fixture.Read();
        parent.JoinQueries.Add("children", child);
        parent.Columns.Add(new LabelledColumn("", "", SqlQueryStructure.DATA_IDENT, "children", "children"));
        string sql = fixture.Builder.Build(parent);
        StringAssert.Contains(sql, "'children' VALUE (SELECT COALESCE(JSON_ARRAYAGG");
        StringAssert.Contains(sql, ") FORMAT JSON");
    }

    [TestMethod]
    public void InsertReturnsAliasedColumnsThroughRefCursor()
    {
        OracleTestFixture fixture = new();
        SqlInsertStructure insert = fixture.Insert(1, "O'Brien @literal");
        string sql = fixture.Builder.Build(insert);
        using OracleConnection connection = new();
        using OracleCommand command = (OracleCommand)fixture.Executor.PrepareDbCommand(connection, sql, insert.Parameters, null, fixture.SourceName);
        StringAssert.Contains(command.CommandText, "INSERT INTO \"DAB\".\"DAB_ORACLE_BOOKS\"");
        StringAssert.Contains(command.CommandText, "RETURNING \"ID\", \"TITLE\" INTO dab_value0, dab_value1");
        StringAssert.Contains(command.CommandText, "OPEN :dab_result FOR SELECT");
        Assert.IsFalse(command.CommandText.Contains("O'Brien", StringComparison.Ordinal));
        Assert.AreEqual(OracleDbType.RefCursor, command.Parameters["dab_result"].OracleDbType);
        Assert.AreEqual(ParameterDirection.Output, command.Parameters["dab_result"].Direction);
    }

    [TestMethod]
    public void UpsertPreservesPolicyOnBothUpdateAttempts()
    {
        OracleTestFixture fixture = new();
        SqlUpsertQueryStructure upsert = fixture.Upsert(1, "updated");
        upsert.DbPolicyPredicatesForOperations[EntityActionOperation.Update] = "\"TITLE\" <> 'locked'";
        string sql = fixture.Builder.Build(upsert);
        Assert.AreEqual(2, sql.Split("UPDATE \"DAB\".\"DAB_ORACLE_BOOKS\"", StringSplitOptions.None).Length - 1);
        Assert.AreEqual(2, sql.Split("\"TITLE\" <> 'locked'", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(sql, "EXCEPTION WHEN DUP_VAL_ON_INDEX THEN SELECT COUNT(*) INTO dab_exists");
        StringAssert.Contains(sql, "IF dab_exists = 0 THEN RAISE; END IF;");
        StringAssert.Contains(sql, "RAISE_APPLICATION_ERROR(-20001");
        StringAssert.Contains(sql, "dab_updated AS \"___dab_updated___\"");
    }

    [TestMethod]
    public void CreatePolicyIsRejectedInsteadOfIgnored()
    {
        OracleTestFixture fixture = new();
        SqlInsertStructure insert = fixture.Insert(1, "blocked");
        insert.DbPolicyPredicatesForOperations[EntityActionOperation.Create] = "1 = 0";
        Assert.ThrowsException<NotSupportedException>(() => fixture.Builder.Build(insert));
    }

    [TestMethod]
    public void DeleteHonorsKeyAndPolicy()
    {
        OracleTestFixture fixture = new();
        SqlDeleteStructure delete = fixture.Delete(7);
        delete.DbPolicyPredicatesForOperations[EntityActionOperation.Delete] = "\"TITLE\" <> 'locked'";
        Assert.AreEqual("DELETE FROM \"DAB\".\"DAB_ORACLE_BOOKS\" WHERE \"TITLE\" <> 'locked' AND \"ID\" = @param0", fixture.Builder.Build(delete));
    }

    [TestMethod]
    public async Task UpsertResultRemovesInternalIndicatorAndReportsInsertOrUpdate()
    {
        OracleTestFixture fixture = new();
        foreach (int indicator in new[] { 0, 1 })
        {
            DataTable table = new();
            table.Columns.Add("ID", typeof(decimal));
            table.Columns.Add("___dab_updated___", typeof(decimal));
            table.Rows.Add(5m, (decimal)indicator);
            using DataTableReader reader = table.CreateDataReader();
            DbResultSet result = await fixture.Executor.GetMultipleResultSetsIfAnyAsync(reader);
            Assert.AreEqual(indicator == 1, result.ResultProperties[SqlMutationEngine.IS_UPDATE_RESULT_SET]);
            Assert.AreEqual(1, result.Rows.Single().Columns.Count);
            Assert.AreEqual(5m, result.Rows.Single().Columns["ID"]);
        }
    }

    [TestMethod]
    public void CompositeForeignKeysMatchOwnerAndPosition()
    {
        string sql = new OracleQueryBuilder().BuildForeignKeyInfoQuery(2);
        StringAssert.Contains(sql, "parent.OWNER = fk.R_OWNER");
        StringAssert.Contains(sql, "parent.POSITION = child.POSITION");
        StringAssert.Contains(sql, "child.OWNER IN (:schemaName0, :schemaName1)");
        StringAssert.Contains(sql, "child.TABLE_NAME IN (:tableName0, :tableName1)");
    }

    [DataTestMethod]
    [DataRow(1, HttpStatusCode.Conflict)]
    [DataRow(1400, HttpStatusCode.BadRequest)]
    [DataRow(2291, HttpStatusCode.BadRequest)]
    [DataRow(20001, HttpStatusCode.Forbidden)]
    [DataRow(20002, HttpStatusCode.NotFound)]
    [DataRow(942, HttpStatusCode.InternalServerError)]
    public void OracleErrorCodesHaveExpectedHttpStatus(int number, HttpStatusCode expected)
    {
        Assert.AreEqual(expected, OracleDbExceptionParser.GetStatusCode(number));
    }

    [TestMethod]
    public void IdentifierQuotingEscapesEmbeddedDoubleQuotes()
    {
        Assert.AreEqual("\"a\"\"b\"", new OracleQueryBuilder().QuoteIdentifier("a\"b"));
    }

    [DataTestMethod]
    [DataRow(OrderBy.ASC, false, "(\"sort\" > @after OR \"sort\" IS NULL)")]
    [DataRow(OrderBy.ASC, true, "(1 = 0)")]
    [DataRow(OrderBy.DESC, false, "\"sort\" < @after")]
    [DataRow(OrderBy.DESC, true, "\"sort\" IS NOT NULL")]
    public void PaginationHonorsOracleNullOrdering(OrderBy direction, bool isNull, string expected)
    {
        OracleTestFixture fixture = new();
        SqlQueryStructure read = fixture.Read();
        read.PaginationMetadata.PaginationPredicate = new(new()
        {
            new("DAB", "BOOKS", "sort", isNull ? null : 2, direction: direction, paramName: "@after")
        });
        StringAssert.Contains(fixture.Builder.Build(read), expected);
    }
}

/// <summary>
/// Shared provider fixture with deterministic metadata; integration tests verify this metadata against Oracle.
/// </summary>
internal sealed class OracleTestFixture
{
    internal const string ENTITY = "Book";
    internal const string TABLE = "DAB_ORACLE_BOOKS";
    internal const string DEFAULT_CONNECTION = "User Id=DAB;Password=DabOracleTests1;Data Source=localhost:15219/XEPDB1";
    internal OracleQueryBuilder Builder { get; } = new();
    internal RuntimeConfigProvider Config { get; }
    internal OracleQueryExecutor Executor { get; }
    internal HttpContextAccessor Http { get; } = new() { HttpContext = new DefaultHttpContext() };
    internal Mock<ISqlMetadataProvider> Metadata { get; } = new();
    internal Mock<IAuthorizationResolver> Authorization { get; } = new();
    internal GQLFilterParser Filter { get; }
    internal DatabaseTable Table { get; }
    internal string SourceName => Config.GetConfig().DefaultDataSourceName;

    private delegate void MapColumn(string entity, string field, out string column);

    internal OracleTestFixture(string connectionString = DEFAULT_CONNECTION, string tableName = TABLE)
    {
        RuntimeConfig config = new(Schema: string.Empty, DataSource: new(DatabaseType.Oracle, connectionString),
            Entities: new(new Dictionary<string, Entity>()));
        Config = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
        Executor = new(Config, new OracleDbExceptionParser(Config), NullLogger<IQueryExecutor>.Instance, Http);
        SourceDefinition definition = new() { PrimaryKey = new() { "ID" } };
        definition.Columns.Add("ID", new() { SystemType = typeof(decimal), DbType = DbType.Decimal });
        definition.Columns.Add("TITLE", new() { SystemType = typeof(string), DbType = DbType.String, IsNullable = true });
        Table = new(new OracleConnectionStringBuilder(connectionString).UserID.ToUpperInvariant(), tableName) { TableDefinition = definition, SourceType = EntitySourceType.Table };
        Metadata.Setup(m => m.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject> { [ENTITY] = Table });
        Metadata.Setup(m => m.GetSourceDefinition(ENTITY)).Returns(definition);
        Metadata.Setup(m => m.GetDatabaseType()).Returns(DatabaseType.Oracle);
        Metadata.Setup(m => m.GetQueryBuilder()).Returns(Builder);
        string mapped;
        Metadata.Setup(m => m.TryGetBackingColumn(It.IsAny<string>(), It.IsAny<string>(), out mapped))
            .Callback(new MapColumn((string entity, string field, out string column) => column = field)).Returns(true);
        Metadata.Setup(m => m.TryGetExposedColumnName(It.IsAny<string>(), It.IsAny<string>(), out mapped))
            .Callback(new MapColumn((string entity, string field, out string column) => column = field)).Returns(true);
        Authorization.Setup(a => a.ResolveDBPolicy(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>(), It.IsAny<HttpContext>()))
            .Returns(ResolvedDatabasePolicy.Empty);
        Filter = new(Config, Mock.Of<IMetadataProviderFactory>());
        Http.HttpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = "anonymous";
    }

    internal SqlQueryStructure Read(bool many = true)
    {
        FindRequestContext context = new(ENTITY, Table, many) { First = 2, FieldsToBeReturned = new() { "ID", "TITLE" } };
        return new(context, Metadata.Object, Authorization.Object, Config, Filter, Http.HttpContext);
    }

    internal SqlInsertStructure Insert(int id, string title) => new(ENTITY, Metadata.Object, Authorization.Object, Filter,
        new Dictionary<string, object?> { ["ID"] = (decimal)id, ["TITLE"] = title }, Http.HttpContext);

    internal SqlUpsertQueryStructure Upsert(int id, string title) => new(ENTITY, Metadata.Object, Authorization.Object, Filter,
        new Dictionary<string, object?> { ["ID"] = (decimal)id, ["TITLE"] = title }, true, Http.HttpContext);

    internal SqlDeleteStructure Delete(int id) => new(ENTITY, Metadata.Object, Authorization.Object, Filter,
        new Dictionary<string, object?> { ["ID"] = id }, Http.HttpContext);
}
