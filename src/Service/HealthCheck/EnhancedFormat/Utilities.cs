// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    public static class Utilities
    {
        public static string BaseUrl = "http://localhost:5000";
        public static string Kind_Object = "OBJECT";
        public static string Kind_Scalar = "SCALAR";
        public static string Kind_NonNull = "NON_NULL";
        public const string JSON_CONTENT_TYPE = "application/json";
        public static string SqlServerMoniker = "sqlserver";
        public static string CreateHttpGraphQLSchemaQuery()
        {
            var payload = new
            {
                operationName = "IntrospectionQuery",
                query = "query IntrospectionQuery {\n  __schema {\n    queryType {\n      name\n    }\n    mutationType {\n      name\n    }\n    subscriptionType {\n      name\n    }\n    types {\n      ...FullType\n    }\n    directives {\n      name\n      description\n      isRepeatable\n      args {\n        ...InputValue\n      }\n      locations\n    }\n  }\n}\n\nfragment FullType on __Type {\n  kind\n  name\n  description\n  specifiedByURL\n  oneOf\n  fields(includeDeprecated: true) {\n    name\n    description\n    args {\n      ...InputValue\n    }\n    type {\n      ...TypeRef\n    }\n    isDeprecated\n    deprecationReason\n  }\n  inputFields {\n    ...InputValue\n  }\n  interfaces {\n    ...TypeRef\n  }\n  enumValues(includeDeprecated: true) {\n    name\n    description\n    isDeprecated\n    deprecationReason\n  }\n  possibleTypes {\n    ...TypeRef\n  }\n}\n\nfragment InputValue on __InputValue {\n  name\n  description\n  type {\n    ...TypeRef\n  }\n  defaultValue\n}\n\nfragment TypeRef on __Type {\n  kind\n  name\n  ofType {\n    kind\n    name\n    ofType {\n      kind\n      name\n      ofType {\n        kind\n        name\n        ofType {\n          kind\n          name\n          ofType {\n            kind\n            name\n          }\n        }\n      }\n    }\n  }\n}"
            };

            // Serialize the payload to a JSON string
            string jsonPayload = JsonSerializer.Serialize(payload);
            return jsonPayload;
        }

        public static string CreateHttpGraphQLQuery(string entityName, int First, List<string> columnNames)
        {
            var payload = new
            {
                //{"query":"{publishers(first:4) {items {id name} }}"}
                query = $"{{{entityName.ToLowerInvariant()}(first: {First}) {{items {{ {string.Join(" ", columnNames)} }}}}}}"
            };

            // Serialize the payload to a JSON string
            string jsonPayload = JsonSerializer.Serialize(payload);
            return jsonPayload;
        }

        public static string CreateHttpRestQuery(string entityName, int First)
        {
            // Create the payload for the REST HTTP request.
            // "/EntityName?$first=4"
            return $"/{entityName}?$first={First}";
        }

        public static string GetServiceRoute(string route, string UriSuffix)
        {
            // The RuntimeConfigProvider enforces the expectation that the configured REST and GraphQL path starts with a
            // forward slash '/'. This is to ensure that the path is always relative to the base URL.
            if (UriSuffix == string.Empty)
            {
                return string.Empty;
            }

            return $"{route}{UriSuffix.ToLowerInvariant()}";
        }
    }
}
