// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Transactions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Product;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Core.Resolvers;

/// <summary>
/// Executes Oracle SQL and PL/SQL using named binds and a REF CURSOR for mutation results.
/// </summary>
public class OracleQueryExecutor : QueryExecutor<OracleConnection>
{
    private readonly ConcurrentDictionary<(string Transaction, string Source), Lazy<TransactionConnection>> _transactionConnections = new();

    protected override bool PreserveConnection => Transaction.Current is not null;

    private TransactionConnection GetTransactionConnection(string dataSourceName)
    {
        Transaction transaction = Transaction.Current!;
        if (transaction.TransactionInformation.Status != TransactionStatus.Active)
        {
            throw new TransactionException("The Oracle transaction is no longer active.");
        }

        var key = (transaction.TransactionInformation.LocalIdentifier, dataSourceName);
        TransactionConnection result = _transactionConnections.GetOrAdd(key, unusedKey => new Lazy<TransactionConnection>(() =>
        {
            TransactionConnection state = new();
            transaction.TransactionCompleted += (sender, args) =>
            {
                _transactionConnections.TryRemove(key, out _);
                state.Dispose();
            };
            return state;
        })).Value;
        if (transaction.TransactionInformation.Status != TransactionStatus.Active)
        {
            _transactionConnections.TryRemove(key, out _);
            result.Dispose();
            throw new TransactionException("The Oracle transaction completed while acquiring its connection.");
        }

        return result;
    }

    protected override ConnectionLease AcquireConnection(string dataSourceName)
    {
        if (Transaction.Current is null)
        {
            return base.AcquireConnection(dataSourceName);
        }

        TransactionConnection state = GetTransactionConnection(dataSourceName);
        state.Gate.Wait();
        return LeaseTransactionConnection(state, dataSourceName);
    }

    protected override async ValueTask<ConnectionLease> AcquireConnectionAsync(string dataSourceName, CancellationToken cancellationToken)
    {
        if (Transaction.Current is null)
        {
            return await base.AcquireConnectionAsync(dataSourceName, cancellationToken);
        }

        TransactionConnection state = GetTransactionConnection(dataSourceName);
        await state.Gate.WaitAsync(cancellationToken);
        return LeaseTransactionConnection(state, dataSourceName);
    }

    private ConnectionLease LeaseTransactionConnection(TransactionConnection state, string dataSourceName)
    {
        try
        {
            if (state.Completed || Transaction.Current?.TransactionInformation.Status != TransactionStatus.Active)
            {
                throw new TransactionException("The Oracle transaction has already completed.");
            }

            state.Connection ??= CreateConnection(dataSourceName);
            return new(state.Connection, () => state.Gate.Release());
        }
        catch
        {
            state.Gate.Release();
            throw;
        }
    }

    private sealed class TransactionConnection : IDisposable
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public OracleConnection? Connection { get; set; }
        public bool Completed { get; private set; }

        public void Dispose()
        {
            Gate.Wait();
            try
            {
                if (Completed)
                {
                    return;
                }

                Completed = true;
                Connection?.Dispose();
            }
            finally { Gate.Release(); }
        }
    }

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
    public override OracleConnection CreateConnection(string dataSourceName)
        => ConfigureConnection(base.CreateConnection(dataSourceName));

    /// <summary>
    /// Oracle uses a session module instead of an Application Name connection-string keyword.
    /// Reapply it on every open because closing a pooled connection clears the session tag.
    /// </summary>
    internal static OracleConnection ConfigureConnection(OracleConnection connection)
    {
        connection.StateChange += (sender, change) =>
        {
            if (change.CurrentState == ConnectionState.Open)
            {
                string applicationName = ProductInfo.GetDataApiBuilderUserAgent();
                if (string.IsNullOrWhiteSpace(applicationName))
                {
                    applicationName = ProductInfo.DAB_USER_AGENT;
                }

                // MODULE is limited to 48 bytes. Do not split a multi-byte character.
                Span<byte> buffer = stackalloc byte[48];
                Encoding.UTF8.GetEncoder().Convert(applicationName.AsSpan(), buffer, true,
                    out _, out int bytesUsed, out _);
                connection.ModuleName = Encoding.UTF8.GetString(buffer[..bytesUsed]);
            }
        };
        return connection;
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

            if (value.DbType == DbType.Binary)
            {
                parameter.OracleDbType = value.Length == -1 ? OracleDbType.Blob : OracleDbType.Raw;
                // GraphQL primary-key arguments can reach the executor as base64 strings,
                // while mutations and cursor predicates already contain decoded byte arrays.
                if (value.Value is string base64)
                {
                    parameter.Value = Convert.FromBase64String(base64);
                }
            }

            if (value.DatabaseTypeName is "CLOB" or "NCLOB")
            {
                parameter.OracleDbType = value.DatabaseTypeName == "NCLOB" ? OracleDbType.NClob : OracleDbType.Clob;
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
