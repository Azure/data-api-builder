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

    // A failed connection can have an ambiguous commit outcome. Do not retry it automatically.
    public override bool IsTransientException(DbException e) => e is OracleException { Number: 60 or 8177 };

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
