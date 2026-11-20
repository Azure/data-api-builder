// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Net;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Core.Resolvers;

/// <summary>
/// Maps Oracle errors to API errors without exposing database details in production.
/// </summary>
public class OracleDbExceptionParser : DbExceptionParser
{
    public const int POLICY_FAILURE = 20001;
    public const int ROW_NOT_FOUND = 20002;

    public OracleDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider)
    {
    }

    // QueryExecutor applies its bounded retry policy to connection-open failures. Errors from
    // command execution are translated separately, so a lost response does not replay a mutation.
    public override bool IsTransientException(DbException e) => e is OracleException
    {
        Number:
            60 or 8177 or // Deadlock / serialization conflict.
            1033 or 1034 or 1089 or 1090 or // Database starting, unavailable or shutting down.
            3113 or 3114 or 3135 or // Broken connection.
            12170 or 12535 or // Connection timeout.
            12514 or 12516 or 12518 or 12519 or 12520 or 12528 or // Service/handler temporarily unavailable.
            12537 or 12541 or 12543 // Connection closed, listener unavailable or host unreachable.
    };

    public override HttpStatusCode GetHttpStatusCodeForException(DbException e) => e is OracleException oracle
        ? GetStatusCode(oracle.Number)
        : HttpStatusCode.InternalServerError;

    internal static HttpStatusCode GetStatusCode(int number) => number switch
    {
        1 => HttpStatusCode.Conflict,
        1400 or 1407 or 1438 or 1722 or 12899 or 2290 or 2291 or 2292 => HttpStatusCode.BadRequest,
        POLICY_FAILURE => HttpStatusCode.Forbidden,
        ROW_NOT_FOUND => HttpStatusCode.NotFound,
        _ => HttpStatusCode.InternalServerError
    };

    public override DataApiBuilderException.SubStatusCodes GetResultSubStatusCodeForException(DbException e) => e switch
    {
        OracleException { Number: POLICY_FAILURE } => DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure,
        OracleException { Number: ROW_NOT_FOUND } => DataApiBuilderException.SubStatusCodes.ItemNotFound,
        _ => base.GetResultSubStatusCodeForException(e)
    };
}
