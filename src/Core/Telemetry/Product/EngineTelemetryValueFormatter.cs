// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>Schema-v1 value spellings, independent of CLR enum names and current culture.</summary>
    internal static class EngineTelemetryValueFormatter
    {
        internal static string Milliseconds(TimeSpan value)
            => Math.Max(0, (long)value.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);

        internal static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

        internal static string UptimeBucket(TimeSpan elapsed) => elapsed.TotalMinutes switch
        {
            < 1 => "under_1m",
            < 60 => "1m_1h",
            < 360 => "1h_6h",
            < 1440 => "6h_1d",
            < 10080 => "1d_7d",
            _ => "7d_plus"
        };

        internal static string Wire<T>(T value) where T : struct, Enum => (object)value switch
        {
            EngineTelemetryApi.Rest => "rest",
            EngineTelemetryApi.GraphQL => "graph_ql",
            EngineTelemetryApi.Mcp => "mcp",
            EngineTelemetryTransport.Http => "http",
            EngineTelemetryTransport.Stdio => "stdio",
            EngineTelemetryTransport.InProcess => "in_process",
            EngineTelemetryRole.Anonymous => "anonymous",
            EngineTelemetryRole.Authenticated => "authenticated",
            EngineTelemetryRole.Custom => "custom",
            EngineTelemetryOperation.Read => "read",
            EngineTelemetryOperation.Write => "write",
            EngineTelemetryOperation.Execute => "execute",
            EngineTelemetryProvider.MsSql => "ms_sql",
            EngineTelemetryProvider.DwSql => "dw_sql",
            EngineTelemetryProvider.PostgreSql => "postgre_sql",
            EngineTelemetryProvider.MySql => "my_sql",
            EngineTelemetryProvider.CosmosDb => "cosmos_db",
            EngineTelemetryObject.Table => "table",
            EngineTelemetryObject.View => "view",
            EngineTelemetryObject.StoredProcedure => "stored_procedure",
            EngineTelemetryObject.Document => "document",
            EngineTelemetryCacheLayer.Level1 => "level1",
            EngineTelemetryCacheLayer.Level2 => "level2",
            EngineTelemetryCacheResult.Hit => "hit",
            EngineTelemetryCacheResult.Miss => "miss",
            EngineTelemetryOutcome.Success => "success",
            EngineTelemetryOutcome.Failure => "failure",
            EngineTelemetryOutcome.PartialFailure => "partial_failure",
            EngineTelemetryOutcome.Canceled => "canceled",
            EngineTelemetryMeasurement.Request => "request",
            EngineTelemetryMeasurement.Operation => "operation",
            EngineTelemetryMeasurement.DatabaseAttempt => "database_attempt",
            EngineTelemetryMeasurement.CacheLookup => "cache_lookup",
            EngineTelemetryMeasurement.Embedding => "embedding",
            EngineTelemetryMeasurement.HttpOutcome => "http_outcome",
            EngineTelemetryHttpStatus.Informational => "informational",
            EngineTelemetryHttpStatus.Success => "success",
            EngineTelemetryHttpStatus.Redirect => "redirect",
            EngineTelemetryHttpStatus.ClientError => "client_error",
            EngineTelemetryHttpStatus.ServerError => "server_error",
            _ => "unknown"
        };
    }
}
