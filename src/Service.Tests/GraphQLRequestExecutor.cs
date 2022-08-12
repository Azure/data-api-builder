using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests
{
    internal static class GraphQLRequestExecutor
    {
        public static async Task<JsonElement> PostGraphQLRequestAsync(
            HttpClient client,
            RuntimeConfigProvider configProvider,
            string queryName,
            string query,
            Dictionary<string, object> variables = null,
            string authToken = null,
            string clientRoleHeader = null,
            bool failOnError = true)
        {
            object payload = variables == null ?
                new { query } :
                new
                {
                    query,
                    variables
                };

            string graphQLEndpoint = configProvider
                .GetRuntimeConfiguration()
                .GraphQLGlobalSettings.Path;

            HttpRequestMessage request = new(HttpMethod.Post, graphQLEndpoint)
            {
                Content = JsonContent.Create(payload)
            };

            if (!string.IsNullOrEmpty(authToken))
            {
                request.Headers.Add(AuthenticationConfig.CLIENT_PRINCIPAL_HEADER, authToken);
            }

            if (!string.IsNullOrEmpty(clientRoleHeader))
            {
                request.Headers.Add(AuthorizationResolver.CLIENT_ROLE_HEADER, clientRoleHeader);
            }

            HttpResponseMessage response = await client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            JsonElement graphQLResult = JsonSerializer.Deserialize<JsonElement>(body);

            if (graphQLResult.TryGetProperty("errors", out JsonElement errors))
            {
                if (failOnError)
                {
                    throw new AssertFailedException($"Errors were in the response.{Environment.NewLine}{JsonSerializer.Serialize(errors, new JsonSerializerOptions { WriteIndented = true }).ToString()}");
                }

                // to validate expected errors and error message
                return errors;
            }

            return graphQLResult.GetProperty("data").GetProperty(queryName);
        }
    }
}
