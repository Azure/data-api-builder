// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.HealthCheck
{
    /// <summary>
    /// HealthCheckConstants is a common place to track all constant values related to health checks.
    /// </summary>
    public static class HealthCheckConstants
    {
        public const string ENDPOINT = "endpoint";
        public const string DATASOURCE = "data-source";
        public const string REST = "rest";
        public const string GRAPHQL = "graphql";
        public const int ERROR_RESPONSE_TIME_MS = -1;
        public const int DEFAULT_THRESHOLD_RESPONSE_TIME_MS = 1000;
        public const int DEFAULT_FIRST_VALUE = 100;
    }
}
