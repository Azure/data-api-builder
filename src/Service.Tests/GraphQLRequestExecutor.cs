// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;

namespace Azure.DataApiBuilder.Service.Tests
{
    internal static class GraphQLRequestExecutor
    {
        /// <summary>
        /// Executes a GraphQL request for a single query node
        /// </summary>
        /// <param name="client">http client.</param>
        /// <param name="configProvider">configProvider</param>
        /// <param name="queryName">queryName.</param>
        /// <param name="query">query</param>
        /// <param name="variables">variables</param>
        /// <param name="authToken">authToken</param>
        /// <param name="clientRoleHeader">clientRoleHeader</param>
        /// <returns>JsonResult</returns>
        public static async Task<JsonElement> PostGraphQLRequestAsync(
            HttpClient client,
            RuntimeConfigProvider configProvider,
            string queryName,
            string query,
            Dictionary<string, object> variables = null,
            string authToken = null,
            string clientRoleHeader = null)
        {
            object payload = variables == null ?
                new { query } :
                new
                {
                    query,
                    variables
                };

            string graphQLEndpoint = configProvider.GetConfig().GraphQLPath;

            HttpRequestMessage request = new(HttpMethod.Post, graphQLEndpoint)
            {
                Content = JsonContent.Create(payload)
            };

            if (!string.IsNullOrEmpty(authToken))
            {
                request.Headers.Add(AuthenticationOptions.CLIENT_PRINCIPAL_HEADER, authToken);
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
                // to validate expected errors and error message
                return errors;
            }

            return graphQLResult.GetProperty("data").GetProperty(queryName);
        }
    }
}
