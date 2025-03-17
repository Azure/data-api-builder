// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.HealthCheck
{
    public static class HealthCheckConstants
    {
        public static string Endpoint = "endpoint";
        public static string DataSource = "data-source";
        public static string Rest = "rest";
        public static string GraphQL = "graphql";
        public static int ErrorResponseTimeMs = -1;
        public static int DefaultThresholdResponseTimeMs = 1000;
        public static int DefaultFirstValue = 100;
    }
}
