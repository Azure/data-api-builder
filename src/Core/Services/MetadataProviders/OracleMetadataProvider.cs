// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Core.Services;

/// <summary>
/// Discovers Oracle tables, views, defaults and identity columns from the accessible data dictionary.
/// </summary>
public class OracleMetadataProvider : SqlMetadataProvider<OracleConnection, OracleDataAdapter, OracleCommand>
{
    public OracleMetadataProvider(
        RuntimeConfigProvider runtimeConfigProvider,
        RuntimeConfigValidator runtimeConfigValidator,
        IAbstractQueryManagerFactory queryManagerFactory,
        ILogger<ISqlMetadataProvider> logger,
        string dataSourceName,
        bool isValidateOnly = false)
        : base(runtimeConfigProvider, runtimeConfigValidator, queryManagerFactory, logger, dataSourceName, isValidateOnly)
    {
    }

    /// <inheritdoc />
    protected override OracleConnection CreateConnection()
        => OracleQueryExecutor.ConfigureConnection(new OracleConnection());

    public override string GetDefaultSchemaName()
    {
        string user = new OracleConnectionStringBuilder(ConnectionString).UserID;
        if (string.IsNullOrWhiteSpace(user) || user == "/")
        {
            throw new NotSupportedException("Specify an explicit SCHEMA.TABLE source when Oracle authentication does not include a user ID.");
        }

        return user.StartsWith('"') && user.EndsWith('"')
            ? user[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal)
            : user.ToUpperInvariant();
    }

    protected override async Task<DataTable> GetColumnsAsync(string schemaName, string tableName, CancellationToken cancellationToken)
    {
        using OracleConnection connection = CreateConnection();
        connection.ConnectionString = ConnectionString;
        await connection.OpenAsync(cancellationToken);
        using OracleCommand command = connection.CreateCommand();
        command.BindByName = true;
        // DATA_DEFAULT is LONG on 19c; keep it last for sequential readers.
        command.CommandText = "SELECT c.COLUMN_NAME, c.DATA_TYPE, c.DATA_LENGTH, c.DATA_PRECISION, c.DATA_SCALE, c.IDENTITY_COLUMN, " +
            "i.GENERATION_TYPE, c.DATA_DEFAULT AS COLUMN_DEFAULT " +
            "FROM ALL_TAB_COLUMNS c LEFT JOIN ALL_TAB_IDENTITY_COLS i " +
            "ON i.OWNER = c.OWNER AND i.TABLE_NAME = c.TABLE_NAME AND i.COLUMN_NAME = c.COLUMN_NAME " +
            "WHERE c.OWNER = :owner AND c.TABLE_NAME = :table_name ORDER BY c.COLUMN_ID";
        command.Parameters.Add("owner", OracleDbType.Varchar2).Value = schemaName;
        command.Parameters.Add("table_name", OracleDbType.Varchar2).Value = tableName;
        command.InitialLONGFetchSize = -1;
        using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        DataTable columns = new();
        columns.Load(reader);
        cancellationToken.ThrowIfCancellationRequested();
        return columns;
    }

    protected override async Task<DataTable> FillSchemaForTableAsync(string schemaName, string tableName, CancellationToken cancellationToken)
    {
        DataTable table = await base.FillSchemaForTableAsync(schemaName, tableName, cancellationToken);
        if (table.PrimaryKey.Length == 0)
        {
            // The Oracle adapter can omit keys on BY DEFAULT ON NULL identities.
            // Recover the actual key, in constraint order, from the catalog.
            using OracleConnection connection = CreateConnection();
            connection.ConnectionString = ConnectionString;
            await connection.OpenAsync(cancellationToken);
            using OracleCommand command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = "SELECT col.COLUMN_NAME FROM ALL_CONSTRAINTS con " +
                "JOIN ALL_CONS_COLUMNS col ON col.OWNER = con.OWNER AND col.CONSTRAINT_NAME = con.CONSTRAINT_NAME " +
                "WHERE con.OWNER = :owner AND con.TABLE_NAME = :table_name AND con.CONSTRAINT_TYPE = 'P' ORDER BY col.POSITION";
            command.Parameters.Add("owner", OracleDbType.Varchar2).Value = schemaName;
            command.Parameters.Add("table_name", OracleDbType.Varchar2).Value = tableName;
            using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            List<DataColumn> key = new();
            while (await reader.ReadAsync(cancellationToken))
            {
                key.Add(table.Columns[reader.GetString(0)]!);
            }

            table.PrimaryKey = key.ToArray();
        }

        return table;
    }

    protected override void PopulateColumnDefinitionWithHasDefaultAndDbType(SourceDefinition sourceDefinition, DataTable allColumnsInTable)
    {
        base.PopulateColumnDefinitionWithHasDefaultAndDbType(sourceDefinition, allColumnsInTable);
        foreach (DataRow row in allColumnsInTable.Rows)
        {
            if (sourceDefinition.Columns.TryGetValue((string)row["COLUMN_NAME"], out ColumnDefinition? column))
            {
                string dataType = (string)row["DATA_TYPE"];
                if (dataType == "LONG RAW")
                {
                    throw new NotSupportedException($"Oracle binary column '{row["COLUMN_NAME"]}' (LONG RAW) is not supported. Migrate it to BLOB or expose supported text through a view.");
                }

                if (dataType is "RAW" or "BLOB")
                {
                    // The standard unbounded-length marker distinguishes LOB binds from RAW binds.
                    column.Length = dataType == "BLOB" ? -1 : Convert.ToInt32(row["DATA_LENGTH"]);
                }

                if (string.Equals(row["IDENTITY_COLUMN"].ToString(), "YES", StringComparison.Ordinal))
                {
                    column.IsAutoGenerated = true;
                    // Override the adapter's auto-increment/read-only flag: BY DEFAULT
                    // (including ON NULL) identities permit explicit values.
                    column.IsReadOnly = string.Equals(row["GENERATION_TYPE"].ToString(), "ALWAYS", StringComparison.Ordinal);
                }
            }
        }
    }

    protected override Task FillSchemaForStoredProcedureAsync(Entity entity, string entityName, string schemaName,
        string procedureName, StoredProcedureDefinition storedProcedureDefinition, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Oracle stored procedure entities are not supported. Expose a table or view instead.");

    public override Type SqlToCLRType(string sqlType) => sqlType.ToUpperInvariant() switch
    {
        "NUMBER" or "DECIMAL" or "NUMERIC" or "FLOAT" => typeof(decimal),
        "BINARY_FLOAT" => typeof(float),
        "BINARY_DOUBLE" => typeof(double),
        "DATE" or "TIMESTAMP" or "TIMESTAMP WITH LOCAL TIME ZONE" => typeof(DateTime),
        "TIMESTAMP WITH TIME ZONE" => typeof(DateTimeOffset),
        "RAW" or "LONG RAW" or "BLOB" => typeof(byte[]),
        "CHAR" or "NCHAR" or "VARCHAR2" or "NVARCHAR2" or "CLOB" or "NCLOB" or "LONG" => typeof(string),
        _ => throw new NotSupportedException($"Oracle type '{sqlType}' is not supported.")
    };
}
