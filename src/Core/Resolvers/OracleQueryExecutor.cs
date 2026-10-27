// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Text;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Core.Resolvers;

/// <summary>
/// Executes Oracle SQL and PL/SQL using named binds and a REF CURSOR for mutation results.
/// </summary>
public class OracleQueryExecutor : QueryExecutor<OracleConnection>
{
    public OracleQueryExecutor(
        RuntimeConfigProvider runtimeConfigProvider,
        DbExceptionParser dbExceptionParser,
        ILogger<IQueryExecutor> logger,
        IHttpContextAccessor httpContextAccessor,
        HotReloadEventHandler<HotReloadEventArgs>? handler = null)
        : base(dbExceptionParser, logger, runtimeConfigProvider, httpContextAccessor, handler)
    {
        foreach ((string name, DataSource source) in runtimeConfigProvider.GetConfig().GetDataSourceNamesToDataSourcesIterator())
        {
            if (source.DatabaseType is DatabaseType.Oracle)
            {
                ConnectionStringBuilders.Add(name, new OracleConnectionStringBuilder(source.ConnectionString));
            }
        }
    }

    /// <inheritdoc />
    public override DbCommand PrepareDbCommand(
        OracleConnection conn,
        string sqltext,
        IDictionary<string, DbConnectionParam>? parameters,
        HttpContext? httpContext,
        string dataSourceName)
    {
        OracleCommand command = conn.CreateCommand();
        command.BindByName = true;
        command.CommandText = TranslateParameters(sqltext, out HashSet<string> binds);
        foreach ((string key, DbConnectionParam value) in parameters ?? Enumerable.Empty<KeyValuePair<string, DbConnectionParam>>())
        {
            string name = key.TrimStart('@', ':');
            // Policy parsing can allocate parameters that are not needed by this operation.
            if (!binds.Contains(name))
            {
                continue;
            }

            OracleParameter parameter = new() { ParameterName = name, Value = value.Value ?? DBNull.Value };
            if (value.DbType is DbType dbType)
            {
                parameter.DbType = dbType;
            }

            command.Parameters.Add(parameter);
        }

        if (sqltext.StartsWith(OracleQueryBuilder.MUTATION_PREFIX, StringComparison.Ordinal))
        {
            command.Parameters.Add(OracleQueryBuilder.RESULT_PARAMETER, OracleDbType.RefCursor, ParameterDirection.Output);
        }

        return command;
    }

    /// <summary>
    /// Converts DAB's parameter markers without changing quoted identifiers, literals or comments.
    /// Oracle alternative quoted literals (q'[text]') are preserved as well.
    /// </summary>
    internal static string TranslateParameters(string sql, out HashSet<string> binds)
    {
        binds = new(StringComparer.OrdinalIgnoreCase);
        StringBuilder result = new();
        for (int i = 0; i < sql.Length;)
        {
            int start = i;
            if (i + 2 < sql.Length && (sql[i] is 'q' or 'Q') && sql[i + 1] == '\'')
            {
                char close = sql[i + 2] switch { '[' => ']', '{' => '}', '(' => ')', '<' => '>', var delimiter => delimiter };
                i += 3;
                while (i + 1 < sql.Length && !(sql[i] == close && sql[i + 1] == '\''))
                {
                    i++;
                }

                i = Math.Min(i + 2, sql.Length);
            }
            else if (sql[i] is '\'' or '"')
            {
                char quote = sql[i++];
                while (i < sql.Length)
                {
                    if (sql[i++] == quote)
                    {
                        if (i < sql.Length && sql[i] == quote)
                        {
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            else if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }
            }
            else if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
            {
                int end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? sql.Length : end + 2;
            }
            else if ((sql[i] is '@' or ':') && i + 1 < sql.Length && (char.IsLetter(sql[i + 1]) || sql[i + 1] == '_'))
            {
                i++;
                int nameStart = i;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_'))
                {
                    i++;
                }

                binds.Add(sql[nameStart..i]);
                result.Append(':').Append(sql, nameStart, i - nameStart);
                continue;
            }
            else
            {
                i++;
            }

            result.Append(sql, start, i - start);
        }

        return result.ToString();
    }

    /// <inheritdoc />
    public override async Task<DbResultSet> GetMultipleResultSetsIfAnyAsync(DbDataReader dbDataReader, List<string>? args = null)
    {
        DbResultSet result = await ExtractResultSetFromDbDataReaderAsync(dbDataReader);
        foreach (DbResultSetRow row in result.Rows)
        {
            if (row.Columns.Remove(OracleQueryBuilder.UPDATE_INDICATOR, out object? updated))
            {
                result.ResultProperties[SqlMutationEngine.IS_UPDATE_RESULT_SET] = Convert.ToInt32(updated) == 1;
            }
        }

        return result;
    }
}
