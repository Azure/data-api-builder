// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel.GraphQL;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    public class HttpUtilities
    {
        private readonly ILogger? _logger;

        /// <summary>
        /// HttpUtility constructor.
        /// </summary>
        /// <param name="logger">Logger</param>
        public HttpUtilities(ILogger<HttpUtilities>? logger)
        {
            _logger = logger;
        }

        public bool ExecuteDbQuery(string query, string connectionString)
        {
            bool isSuccess = false;
            // Execute the query on DB and return the response time.
            using (SqlConnection connection = new(connectionString))
            {
                try
                {
                    SqlCommand command = new(query, connection);
                    connection.Open();
                    SqlDataReader reader = command.ExecuteReader();
                    LogTrace("The query executed successfully.");
                    isSuccess = true;
                    reader.Close();
                }
                catch (Exception ex)
                {
                    LogTrace($"An exception occurred while executing the query: {ex.Message}");
                    isSuccess = false;
                }
            }

            return isSuccess;
        }

        public async Task<bool> ExecuteEntityGraphQLQueryAsync(string UriSuffix, string EntityName, string TableName, int First)
        {
            bool isSuccess = false;
            try
            {
                // Base URL of the API that handles SQL operations
                string ApiRoute = Utilities.GetServiceRoute(Utilities.BaseUrl, UriSuffix);
                if (ApiRoute == string.Empty)
                {
                    LogTrace("The API route is not available, hence HealthEndpoint is not available.");
                    return isSuccess;
                }

                List<string> columnNames = new();
                // Create an instance of HttpClient
                using (HttpClient client = CreateClient(ApiRoute))
                {
                    // Send a POST request to the API
                    string jsonPayload = Utilities.CreateHttpGraphQLSchemaQuery();
                    HttpContent content = new StringContent(jsonPayload, Encoding.UTF8, Utilities.JSON_CONTENT_TYPE);

                    HttpResponseMessage response = client.PostAsync(ApiRoute, content).Result;
                    if (response.IsSuccessStatusCode)
                    {
                        // Read the response content as a string
                        string responseContent = await response.Content.ReadAsStringAsync();
                        if (responseContent != null)
                        {
                            GraphQLSchemaMode? graphQLSchema = JsonSerializer.Deserialize<GraphQLSchemaMode>(responseContent);
                            if (graphQLSchema != null && graphQLSchema.Data != null && graphQLSchema.Data.Schema != null && graphQLSchema.Data.Schema.Types != null)
                            {
                                foreach (Types type in graphQLSchema.Data.Schema.Types)
                                {
                                    string kindName = type.Kind;
                                    if (kindName.Equals(Utilities.Kind_Object)
                                    && type.Name.Equals(EntityName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        foreach (Field field in type.Fields)
                                        {
                                            string fieldName = field.Name;
                                            if (!fieldName.Equals(string.Empty)
                                            && ((field?.Type?.Kind.Equals(Utilities.Kind_Scalar) ?? false)
                                                || ((field?.Type?.Kind.Equals(Utilities.Kind_NonNull) ?? false) && (field?.Type?.OfType?.Kind.Equals(Utilities.Kind_Scalar) ?? false))))
                                            {
                                                columnNames.Add(fieldName);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        LogTrace("Request failed with status: " + response.StatusCode);
                    }
                }

                if (columnNames.Any())
                {
                    using (HttpClient client = CreateClient(ApiRoute))
                    {
                        string jsonPayload = Utilities.CreateHttpGraphQLQuery(TableName, First, columnNames);
                        HttpContent content = new StringContent(jsonPayload, Encoding.UTF8, Utilities.JSON_CONTENT_TYPE);

                        HttpResponseMessage response = client.PostAsync(ApiRoute, content).Result;
                        if (response.IsSuccessStatusCode)
                        {
                            LogTrace("The HealthEndpoint query executed successfully.");
                            isSuccess = true;
                        }
                    }
                }

                return isSuccess;
            }
            catch (Exception ex)
            {
                LogTrace($"An exception occurred while executing the query: {ex.Message}");
                return isSuccess;
            }
        }

        public bool ExecuteEntityRestQuery(string UriSuffix, string EntityName, int First)
        {
            bool isSuccess = false;
            try
            {
                // Base URL of the API that handles SQL operations
                string ApiRoute = Utilities.GetServiceRoute(Utilities.BaseUrl, UriSuffix);
                if (ApiRoute == string.Empty)
                {
                    LogTrace("The API route is not available, hence HealthEndpoint is not available.");
                    return isSuccess;
                }

                // Create an instance of HttpClient
                using (HttpClient client = CreateClient(ApiRoute))
                {
                    // Send a GET request to the API
                    ApiRoute = $"{ApiRoute}{Utilities.CreateHttpRestQuery(EntityName, First)}";
                    Console.WriteLine($"------------------------{ApiRoute}");
                    HttpResponseMessage response = client.GetAsync(ApiRoute).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        LogTrace("The HealthEndpoint query executed successfully.");
                        isSuccess = true;
                    }
                }

                return isSuccess;
            }
            catch (Exception ex)
            {
                LogTrace($"An exception occurred while executing the query: {ex.Message}");
                return isSuccess;
            }
        }

        /// <summary>
        /// Creates a <see cref="HttpClient" /> for processing HTTP requests/responses with the test server.
        /// </summary>
        public HttpClient CreateClient(string ApiRoute)
        {
            return new HttpClient()
            {
                // Set the base URL for the client
                BaseAddress = new Uri(ApiRoute),
                DefaultRequestHeaders =
                {
                    Accept = { new MediaTypeWithQualityHeaderValue(Utilities.JSON_CONTENT_TYPE) }
                },
                Timeout = TimeSpan.FromSeconds(200),
            };
        }

        // <summary>
        /// Logs a trace message if a logger is present and the logger is enabled for trace events.
        /// </summary>
        /// <param name="message">Message to emit.</param>
        private void LogTrace(string message)
        {
            if (_logger is not null && _logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace(message);
            }
            else
            {
                Console.WriteLine(message);
            }
        }
    }
}
