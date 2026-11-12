// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests;

[TestClass, TestCategory(TestCategory.ORACLE)]
[DoNotParallelize]
public class OracleMetadataIntegrationTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable("DAB_ORACLE_TEST_CONNECTION_STRING");

    [DataTestMethod]
    [DataRow("LONG", "CLOB")]
    [DataRow("LONG RAW", "BLOB")]
    public async Task LegacyLongMetadataFailsAtStartup(string sqlType, string replacement)
    {
        string tableName = "DAB_META_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        using OracleConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"CREATE TABLE {tableName} (ID NUMBER PRIMARY KEY, PAYLOAD {sqlType})");
        try
        {
            OracleMetadataProvider metadata = CreateProvider(tableName, EntitySourceType.Table, EntityActionOperation.Read);

            NotSupportedException error = await Assert.ThrowsExceptionAsync<NotSupportedException>(() => metadata.InitializeAsync());

            StringAssert.Contains(error.Message, "PAYLOAD");
            StringAssert.Contains(error.Message, sqlType);
            StringAssert.Contains(error.Message, replacement);
        }
        finally
        {
            await ExecuteAsync(connection, $"DROP TABLE {tableName} PURGE");
        }
    }

    [DataTestMethod]
    [DataRow(EntityActionOperation.Read, true, false, false)]
    [DataRow(EntityActionOperation.Delete, true, false, false)]
    [DataRow(EntityActionOperation.Create, true, true, false)]
    [DataRow(EntityActionOperation.Update, true, true, false)]
    [DataRow(EntityActionOperation.All, true, true, false)]
    [DataRow(EntityActionOperation.Create, false, false, false)]
    [DataRow(EntityActionOperation.Update, false, false, false)]
    [DataRow(EntityActionOperation.Read, true, false, true)]
    [DataRow(EntityActionOperation.Delete, true, false, true)]
    [DataRow(EntityActionOperation.Create, true, true, true)]
    [DataRow(EntityActionOperation.Update, true, true, true)]
    [DataRow(EntityActionOperation.All, true, true, true)]
    [DataRow(EntityActionOperation.Create, false, false, true)]
    [DataRow(EntityActionOperation.Update, false, false, true)]
    public async Task InsteadOfTriggerViewValidatesWritePermissionsAtStartup(EntityActionOperation action, bool triggerEnabled, bool rejected, bool compound)
    {
        string prefix = "DAB_META_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        string tableName = prefix + "_T";
        string viewName = prefix + "_V";
        string triggerName = prefix + "_TR";
        using OracleConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"CREATE TABLE {tableName} (ID NUMBER PRIMARY KEY, TITLE VARCHAR2(100))");
        bool viewCreated = false;
        try
        {
            await ExecuteAsync(connection, $"CREATE VIEW {viewName} AS SELECT ID, TITLE FROM {tableName}");
            viewCreated = true;
            string body = $"IF INSERTING THEN INSERT INTO {tableName} (ID, TITLE) VALUES (:NEW.ID, :NEW.TITLE); " +
                $"ELSIF UPDATING THEN UPDATE {tableName} SET TITLE = :NEW.TITLE WHERE ID = :OLD.ID; " +
                $"ELSE DELETE FROM {tableName} WHERE ID = :OLD.ID; END IF;";
            string trigger = compound
                ? $"CREATE TRIGGER {triggerName} FOR INSERT OR UPDATE OR DELETE ON {viewName} COMPOUND TRIGGER " +
                    $"INSTEAD OF EACH ROW IS BEGIN {body} END INSTEAD OF EACH ROW; END;"
                : $"CREATE TRIGGER {triggerName} INSTEAD OF INSERT OR UPDATE OR DELETE ON {viewName} FOR EACH ROW BEGIN {body} END;";
            await ExecuteAsync(connection, trigger);
            if (!triggerEnabled)
            {
                await ExecuteAsync(connection, $"ALTER TRIGGER {triggerName} DISABLE");
            }

            using (OracleCommand catalog = connection.CreateCommand())
            {
                catalog.CommandText = "SELECT TRIGGER_TYPE, INSTEAD_OF_ROW, STATUS FROM USER_TRIGGERS WHERE TRIGGER_NAME = :name";
                catalog.Parameters.Add("name", OracleDbType.Varchar2).Value = triggerName;
                using var reader = await catalog.ExecuteReaderAsync();
                Assert.IsTrue(await reader.ReadAsync(), "The test trigger must exist in Oracle's catalog.");
                Assert.AreEqual(compound ? "COMPOUND" : "INSTEAD OF", reader.GetString(0));
                if (compound)
                {
                    Assert.AreEqual("YES", reader.GetString(1));
                }

                Assert.AreEqual(triggerEnabled ? "ENABLED" : "DISABLED", reader.GetString(2));
            }

            OracleMetadataProvider metadata = CreateProvider(viewName, EntitySourceType.View, action);
            if (rejected)
            {
                NotSupportedException error = await Assert.ThrowsExceptionAsync<NotSupportedException>(() => metadata.InitializeAsync());
                StringAssert.Contains(error.Message, "INSTEAD OF");
                StringAssert.Contains(error.Message, "RETURNING");
                StringAssert.Contains(error.Message, viewName);
                StringAssert.Contains(error.Message, "read/delete");
            }
            else
            {
                await metadata.InitializeAsync();
                SourceDefinition definition = metadata.GetSourceDefinition("Sample");
                CollectionAssert.AreEqual(new[] { "ID" }, definition.PrimaryKey);
                Assert.AreEqual("VARCHAR2", definition.Columns["TITLE"].DatabaseTypeName);
                if (!triggerEnabled)
                {
                    using OracleCommand command = connection.CreateCommand();
                    command.CommandText = $"BEGIN INSERT INTO {viewName} (ID, TITLE) VALUES (1, 'disabled') RETURNING ID INTO :id; END;";
                    command.Parameters.Add("id", OracleDbType.Decimal, ParameterDirection.Output);
                    await command.ExecuteNonQueryAsync();
                    Assert.AreEqual("1", command.Parameters["id"].Value.ToString());
                }
            }
        }
        finally
        {
            if (viewCreated)
            {
                await ExecuteAsync(connection, $"DROP VIEW {viewName}");
            }

            await ExecuteAsync(connection, $"DROP TABLE {tableName} PURGE");
        }
    }

    private static OracleMetadataProvider CreateProvider(string objectName, EntitySourceType sourceType, EntityActionOperation action)
    {
        Entity entity = new(new(objectName, sourceType, null, sourceType == EntitySourceType.View ? new[] { "ID" } : null),
            new("Sample", "Samples", false), null, new(),
            new[] { new EntityPermission("anonymous", new[] { new EntityAction(action, null, null) }) }, null, null);
        RuntimeConfig config = new(Schema: string.Empty, DataSource: new(DatabaseType.Oracle, ConnectionString),
            Entities: new(new Dictionary<string, Entity> { ["Sample"] = entity }));
        RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
        RuntimeConfigValidator validator = new(provider, new MockFileSystem(), NullLogger<RuntimeConfigValidator>.Instance);
        QueryManagerFactory factory = new(provider, NullLogger<IQueryExecutor>.Instance, new HttpContextAccessor(), null);
        return new(provider, validator, factory, NullLogger<ISqlMetadataProvider>.Instance, provider.GetConfig().DefaultDataSourceName);
    }

    private static async Task ExecuteAsync(OracleConnection connection, string sql)
    {
        using OracleCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
