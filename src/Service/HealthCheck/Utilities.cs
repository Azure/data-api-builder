// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    public static class Utilities
    {
        public const string JSON_CONTENT_TYPE = "application/json";

        public static string GetDatSourceQuery(DatabaseType dbType)
        {
            switch (dbType)
            {
                case DatabaseType.MySQL:
                    return "SELECT 1";
                case DatabaseType.PostgreSQL:
                    return "SELECT 1";
                case DatabaseType.MSSQL:
                    return "SELECT 1";
                case DatabaseType.CosmosDB_NoSQL:
                    return "SELECT VALUE 1";
                case DatabaseType.CosmosDB_PostgreSQL:
                    return "SELECT VALUE 1";
                case DatabaseType.DWSQL:
                    return "SELECT 1";
                default:
                    return string.Empty;
            }
        }

        public static string CreateHttpGraphQLQuery(string entityName, List<string> columnNames, int first)
        {
            var payload = new
            {
                //{"query":"{publishers(first:4) {items {id name} }}"}
                query = $"{{{entityName} (first: {first}) {{items {{ {string.Join(" ", columnNames)} }}}}}}"
            };

            // Serialize the payload to a JSON string
            string jsonPayload = JsonSerializer.Serialize(payload);
            return jsonPayload;
        }

        public static string CreateHttpRestQuery(string entityName, int first)
        {
            // Create the payload for the REST HTTP request.
            // "EntityName?$first=4"
            return $"/{entityName}?$first={first}";
        }
    }
}
