// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    /// <summary>
    /// HttpUtilities creates and executes the HTTP requests for HealthEndpoint.
    /// </summary>
    public class HttpUtilities
    {
        private readonly ILogger? _logger;
        private string _apiRoute;
        private IMetadataProviderFactory _metadataProviderFactory;
        private RuntimeConfigProvider _runtimeConfigProvider;

        /// <summary>
        /// HttpUtility constructor.
        /// </summary>
        /// <param name="logger">Logger</param>
        /// <param name="metadataProviderFactory">MetadataProviderFactory</param>
        /// <param name="runtimeConfigProvider">RuntimeConfigProvider</param>
        public HttpUtilities(
            ILogger<HttpUtilities>? logger,
            IMetadataProviderFactory metadataProviderFactory,
            RuntimeConfigProvider runtimeConfigProvider)
        {
            _logger = logger;
            _apiRoute = string.Empty;
            _metadataProviderFactory = metadataProviderFactory;
            _runtimeConfigProvider = runtimeConfigProvider;
        }

        /// <summary>
        /// Fetches the Http Base URI from the Context information to execute the REST and GraphQL queries.
        /// </summary>
        /// <param name="httpContext">HttpContext</param>
        public void ConfigureApiRoute(HttpContext httpContext)
        {
            if (httpContext == null || httpContext.Request == null)
            {
                throw new ArgumentNullException(nameof(httpContext));
            }

            // Extract base URL: scheme + host + port (if present)
            _apiRoute = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        }

        // Executes the DB query by establishing a connection to the DB.
        public string? ExecuteDbQuery(string query, string connectionString)
        {
            string? errorMessage = null;
            // Execute the query on DB and return the response time.
            using (SqlConnection connection = new(connectionString))
            {
                try
                {
                    SqlCommand command = new(query, connection);
                    connection.Open();
                    SqlDataReader reader = command.ExecuteReader();
                    LogTrace("The health check query for datasource executed successfully.");
                    reader.Close();
                }
                catch (Exception ex)
                {
                    LogTrace($"An exception occurred while executing the health check query: {ex.Message}");
                    errorMessage = ex.Message;
                }
            }

            return errorMessage;
        }

        // Executes the REST query by sending a GET request to the API.
        public string? ExecuteRestQuery(string restUriSuffix, string entityName, int first)
        {
            string? errorMessage = null;
            try
            {
                // Base URL of the API that handles SQL operations
                string apiRoute = Utilities.GetServiceRoute(_apiRoute, restUriSuffix);
                if (apiRoute == string.Empty)
                {
                    LogTrace("The API route is not available, hence HealthEndpoint is not available.");
                    return errorMessage;
                }

                // Create an instance of HttpClient
                using (HttpClient client = CreateClient(apiRoute))
                {
                    // Send a GET request to the API
                    apiRoute = $"{apiRoute}{Utilities.CreateHttpRestQuery(entityName, first)}";
                    HttpResponseMessage response = client.GetAsync(apiRoute).Result;
                    if (response.IsSuccessStatusCode)
                    {
                        LogTrace($"The REST HealthEndpoint query executed successfully with code {response.IsSuccessStatusCode}.");
                    }
                }

                return errorMessage;
            }
            catch (Exception ex)
            {
                LogTrace($"An exception occurred while executing the health check rest query: {ex.Message}");
                return ex.Message;
            }
        }

        // Executes the GraphQL query by sending a POST request to the API.
        // Internally calls the metadata provider to fetch the column names to create the graphql payload.
        public string? ExecuteGraphQLQuery(string graphqlUriSuffix, string entityName, Entity entity)
        {
            string? errorMessage = null;
            // Base URL of the API that handles SQL operations
            string apiRoute = Utilities.GetServiceRoute(_apiRoute, graphqlUriSuffix);
            if (apiRoute == string.Empty)
            {
                LogTrace("The API route is not available, hence HealthEndpoint is not available.");
                return errorMessage;
            }

            try
            {
                string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);

                // Fetch Column Names from Metadata Provider
                ISqlMetadataProvider sqlMetadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
                DatabaseObject dbObject = sqlMetadataProvider.EntityToDatabaseObject[entityName];
                List<string> columnNames = dbObject.SourceDefinition.Columns.Keys.ToList();
                string databaseObjectName = entity.Source.Object;

                // In case any primitive column names are present, execute the query
                if (columnNames.Any() && entity?.Health != null)
                {
                    using (HttpClient client = CreateClient(apiRoute))
                    {
                        string jsonPayload = Utilities.CreateHttpGraphQLQuery(databaseObjectName, columnNames, entity.EntityFirst);
                        HttpContent content = new StringContent(jsonPayload, Encoding.UTF8, Utilities.JSON_CONTENT_TYPE);
                        HttpResponseMessage response = client.PostAsync(apiRoute, content).Result;
                        if (response.IsSuccessStatusCode)
                        {
                            LogTrace("The GraphQL HealthEndpoint query executed successfully.");
                        }
                    }
                }

                return errorMessage;
            }
            catch (Exception ex)
            {
                LogTrace($"An exception occurred while executing the Graphql health check query: {ex.Message}");
                return ex.Message;
            }
        }

        /// <summary>
        /// Creates a <see cref="HttpClient" /> for processing HTTP requests/responses with the test server.
        /// </summary>
        public HttpClient CreateClient(string apiRoute)
        {
            return new HttpClient()
            {
                // Set the base URL for the client
                BaseAddress = new Uri(apiRoute),
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
