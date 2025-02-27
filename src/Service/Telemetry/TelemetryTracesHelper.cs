// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    public static class TelemetryTracesHelper
    {
        public static readonly ActivitySource DABActivitySource = new("DataApiBuilder");
    }
}
