// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
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
    private readonly RuntimeConfigProvider _oracleRuntimeConfigProvider;

    public OracleMetadataProvider(
        RuntimeConfigProvider runtimeConfigProvider,
        RuntimeConfigValidator runtimeConfigValidator,
        IAbstractQueryManagerFactory queryManagerFactory,
        ILogger<ISqlMetadataProvider> logger,
        string dataSourceName,
        bool isValidateOnly = false)
        : base(runtimeConfigProvider, runtimeConfigValidator, queryManagerFactory, logger, dataSourceName, isValidateOnly)
    {
        _oracleRuntimeConfigProvider = runtimeConfigProvider;
    }

    /// <inheritdoc />
    protected override OracleConnection CreateConnection()
        => OracleQueryExecutor.ConfigureConnection(new OracleConnection());

    protected override StringComparer SkippedColumnNameComparer => StringComparer.Ordinal;

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
        await ValidateViewMutationSupportAsync(schemaName, tableName, cancellationToken);
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

    private async Task ValidateViewMutationSupportAsync(string schemaName, string tableName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RuntimeConfig config = _oracleRuntimeConfigProvider.GetConfig();
        string[] writableViews = EntityToDatabaseObject.Where(pair =>
            pair.Value.SchemaName == schemaName && pair.Value.Name == tableName &&
            config.Entities.TryGetValue(pair.Key, out Entity? entity) &&
            entity.Source.Type == EntitySourceType.View &&
            entity.Permissions.Any(permission => permission.Actions.Any(action =>
                action.Action is EntityActionOperation.All or EntityActionOperation.Create or EntityActionOperation.Update)))
            .Select(pair => pair.Key).ToArray();
        if (writableViews.Length == 0)
        {
            return;
        }

        using OracleConnection connection = CreateConnection();
        connection.ConnectionString = ConnectionString;
        await connection.OpenAsync(cancellationToken);
        using OracleCommand command = connection.CreateCommand();
        command.BindByName = true;
        // Compound view triggers expose their INSTEAD OF section through INSTEAD_OF_ROW.
        command.CommandText = "SELECT COUNT(*) FROM ALL_TRIGGERS WHERE TABLE_OWNER = :owner AND TABLE_NAME = :table_name " +
            "AND BASE_OBJECT_TYPE = 'VIEW' AND (TRIGGER_TYPE = 'INSTEAD OF' OR INSTEAD_OF_ROW = 'YES') AND STATUS = 'ENABLED'";
        command.Parameters.Add("owner", OracleDbType.Varchar2).Value = schemaName;
        command.Parameters.Add("table_name", OracleDbType.Varchar2).Value = tableName;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0)
        {
            throw new NotSupportedException($"Oracle view '{schemaName}.{tableName}' has an enabled INSTEAD OF trigger and cannot support create/update permissions " +
                $"for entities {string.Join(", ", writableViews)} because Oracle prohibits RETURNING on these views. Configure read/delete permissions only, or expose the base table for writes.");
        }
    }

    protected override async Task<DataTable> FillSchemaForTableAsync(string entityName, string schemaName, string tableName, CancellationToken cancellationToken)
    {
        using OracleConnection connection = CreateConnection();
        connection.ConnectionString = ConnectionString;
        await connection.OpenAsync(cancellationToken);
        List<string> supportedColumns = new();
        Dictionary<string, string> skippedColumns = new(SkippedColumnNameComparer);
        using (OracleCommand catalog = connection.CreateCommand())
        {
            catalog.BindByName = true;
            // Match SELECT * visibility: user-declared invisible columns have no COLUMN_ID.
            catalog.CommandText = "SELECT COLUMN_NAME, DATA_TYPE FROM ALL_TAB_COLUMNS " +
                "WHERE OWNER = :owner AND TABLE_NAME = :table_name AND COLUMN_ID IS NOT NULL ORDER BY COLUMN_ID";
            catalog.Parameters.Add("owner", OracleDbType.Varchar2).Value = schemaName;
            catalog.Parameters.Add("table_name", OracleDbType.Varchar2).Value = tableName;
            using DbDataReader reader = await catalog.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string name = reader.GetString(0);
                string type = reader.GetString(1);
                if (IsSupportedColumnType(type))
                {
                    supportedColumns.Add(SqlQueryBuilder.QuoteIdentifier(name));
                }
                else
                {
                    skippedColumns.Add(name, type);
                }
            }
        }

        if (supportedColumns.Count == 0 && skippedColumns.Count > 0)
        {
            // Do not send an empty projection to Oracle; report the shared initialization error.
            RegisterSkippedColumns(entityName, schemaName, tableName, new DataTable(), skippedColumns, Array.Empty<string>());
        }

        string? schemaQuery = skippedColumns.Count == 0 ? null
            : $"SELECT {string.Join(", ", supportedColumns)} FROM {GetTableNameWithSchemaPrefix(schemaName, tableName)}";
        // The adapter opens its own connection. Release this slot first so Max Pool Size=1 works.
        await connection.CloseAsync();
        DataTable table = await base.FillSchemaForTableAsync(entityName, schemaName, tableName, cancellationToken, schemaQuery);
        List<string> keyNames = new();
        if (table.PrimaryKey.Length == 0 || skippedColumns.Count > 0)
        {
            // The Oracle adapter can omit keys on BY DEFAULT ON NULL identities.
            // Recover the actual key, in constraint order, from the catalog.
            await connection.OpenAsync(cancellationToken);
            using OracleCommand command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = "SELECT col.COLUMN_NAME FROM ALL_CONSTRAINTS con " +
                "JOIN ALL_CONS_COLUMNS col ON col.OWNER = con.OWNER AND col.CONSTRAINT_NAME = con.CONSTRAINT_NAME " +
                "WHERE con.OWNER = :owner AND con.TABLE_NAME = :table_name AND con.CONSTRAINT_TYPE = 'P' ORDER BY col.POSITION";
            command.Parameters.Add("owner", OracleDbType.Varchar2).Value = schemaName;
            command.Parameters.Add("table_name", OracleDbType.Varchar2).Value = tableName;
            using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                keyNames.Add(reader.GetString(0));
            }

            // A partially projected composite key must never become a smaller, non-unique key.
            HashSet<string> availableColumns = table.Columns.Cast<DataColumn>().Select(column => column.ColumnName).ToHashSet(StringComparer.Ordinal);
            if (keyNames.Count > 0 && keyNames.All(availableColumns.Contains))
            {
                table.PrimaryKey = keyNames.Select(name => table.Columns[name]!).ToArray();
            }
            else if (keyNames.Count > 0)
            {
                table.PrimaryKey = Array.Empty<DataColumn>();
            }
        }

        if (skippedColumns.Count > 0)
        {
            RegisterSkippedColumns(entityName, schemaName, tableName, table, skippedColumns,
                keyNames.Where(skippedColumns.ContainsKey).ToArray());
        }

        return table;
    }

    protected override void PopulateColumnDefinitionWithHasDefaultAndDbType(SourceDefinition sourceDefinition, DataTable allColumnsInTable)
    {
        // Shared source definitions use case-insensitive lookup. A skipped quoted "PAYLOAD"
        // must not overwrite metadata for a supported quoted "payload" column.
        using DataTable supportedColumns = allColumnsInTable.Clone();
        foreach (DataRow row in allColumnsInTable.Rows)
        {
            if (IsSupportedColumnType((string)row["DATA_TYPE"]))
            {
                supportedColumns.ImportRow(row);
            }
        }

        base.PopulateColumnDefinitionWithHasDefaultAndDbType(sourceDefinition, supportedColumns);
        foreach (DataRow row in supportedColumns.Rows)
        {
            if (sourceDefinition.Columns.TryGetValue((string)row["COLUMN_NAME"], out ColumnDefinition? column))
            {
                string dataType = (string)row["DATA_TYPE"];
                column.DatabaseTypeName = dataType;
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

    internal static bool IsSupportedColumnType(string sqlType) => NormalizeColumnType(sqlType) is
        "NUMBER" or "DECIMAL" or "NUMERIC" or "FLOAT" or "BINARY_FLOAT" or "BINARY_DOUBLE" or
        "DATE" or "TIMESTAMP" or "TIMESTAMP WITH LOCAL TIME ZONE" or "TIMESTAMP WITH TIME ZONE" or
        "RAW" or "BLOB" or "CHAR" or "NCHAR" or "VARCHAR2" or "NVARCHAR2" or "CLOB" or "NCLOB";

    private static string NormalizeColumnType(string sqlType) => Regex.Replace(sqlType.ToUpperInvariant(), @"\(\d+\)", string.Empty);

    public override Type SqlToCLRType(string sqlType) => NormalizeColumnType(sqlType) switch
    {
        "NUMBER" or "DECIMAL" or "NUMERIC" or "FLOAT" => typeof(decimal),
        "BINARY_FLOAT" => typeof(float),
        "BINARY_DOUBLE" => typeof(double),
        "DATE" or "TIMESTAMP" or "TIMESTAMP WITH LOCAL TIME ZONE" => typeof(DateTime),
        "TIMESTAMP WITH TIME ZONE" => typeof(DateTimeOffset),
        "RAW" or "BLOB" => typeof(byte[]),
        "CHAR" or "NCHAR" or "VARCHAR2" or "NVARCHAR2" or "CLOB" or "NCLOB" => typeof(string),
        _ => throw new NotSupportedException($"Oracle type '{sqlType}' is not supported.")
    };
}
