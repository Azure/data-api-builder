// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// Identifies this engine's internal HTTP health probes without inspecting query contents.
    /// The per-session marker is not an authentication credential or a telemetry event field.
    /// An arbitrary caller-supplied marker does not suppress usage.
    /// </summary>
    internal static class EngineTelemetryHealthProbe
    {
        internal const string HEADER_NAME = "X-DAB-Internal-Health-Probe";

        internal static bool IsProbe(HttpContext context, EngineTelemetrySession session)
        {
            StringValues values = context.Request.Headers[HEADER_NAME];
            return session.IsEnabled && session.HealthProbeToken is string token &&
                values.Count == 1 && string.Equals(values[0], token, StringComparison.Ordinal);
        }
    }
}
