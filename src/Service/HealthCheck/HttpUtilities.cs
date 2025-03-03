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

        public void ConfigureApiRoute(HttpContext httpContext)
        {
            if (httpContext == null || httpContext.Request == null)
            {
                throw new ArgumentNullException(nameof(httpContext));
            }

            // Extract base URL: scheme + host + port (if present)
            _apiRoute = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";    
        }

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
                    LogTrace("The query executed successfully.");;
                    reader.Close();
                }
                catch (Exception ex)
                {
                    LogTrace($"An exception occurred while executing the query: {ex.Message}");
                    errorMessage = ex.Message;
                }
            }

            return errorMessage;
        }

        public string? ExecuteEntityRestQuery(string UriSuffix, string EntityName, int First)
        {
            string? errorMessage = null;
            try
            {
                // Base URL of the API that handles SQL operations
                string ApiRoute = Utilities.GetServiceRoute(_apiRoute, UriSuffix);
                if (ApiRoute == string.Empty)
                {
                    LogTrace("The API route is not available, hence HealthEndpoint is not available.");
                    return errorMessage;
                }

                // Create an instance of HttpClient
                using (HttpClient client = CreateClient(ApiRoute))
                {
                    // Send a GET request to the API
                    ApiRoute = $"{ApiRoute}{Utilities.CreateHttpRestQuery(EntityName, First)}";
                    HttpResponseMessage response = client.GetAsync(ApiRoute).Result;
                    if (response.IsSuccessStatusCode)
                    {
                        LogTrace($"The HealthEndpoint query executed successfully with code {response.IsSuccessStatusCode}.");
                    }
                }

                return errorMessage;
            }
            catch (Exception ex)
            {
                LogTrace($"An exception occurred while executing the query: {ex.Message}");
                return ex.Message;
            }
        }

        public string? ExecuteEntityGraphQLQueryAsync(string UriSuffix, string entityName, Entity entity)
        {
            string? errorMessage = null;
            // Base URL of the API that handles SQL operations
            string ApiRoute = Utilities.GetServiceRoute(_apiRoute, UriSuffix);
            if (ApiRoute == string.Empty)
            {
                LogTrace("The API route is not available, hence HealthEndpoint is not available.");
                return errorMessage;
            }

            try
            {
                string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);
                ISqlMetadataProvider sqlMetadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
                DatabaseObject dbObject = sqlMetadataProvider.EntityToDatabaseObject[entityName];
                List<string> columnNames = dbObject.SourceDefinition.Columns.Keys.ToList();
                string databaseObjectName = entity.Source.Object;

                if (columnNames.Any() && entity?.Health != null)
                {
                    using (HttpClient client = CreateClient(ApiRoute))
                    {
                        string jsonPayload = Utilities.CreateHttpGraphQLQuery(databaseObjectName, columnNames, entity.Health.First);
                        HttpContent content = new StringContent(jsonPayload, Encoding.UTF8, Utilities.JSON_CONTENT_TYPE);
                        Console.WriteLine($"API Route: {ApiRoute}");
                        Console.WriteLine($"JSON Payload: {jsonPayload}");
                        HttpResponseMessage response = client.PostAsync(ApiRoute, content).Result;
                        if (response.IsSuccessStatusCode)
                        {
                            LogTrace("The HealthEndpoint query executed successfully.");
                        }
                    }
                }

                return errorMessage;
            }
            catch (Exception ex)
            {
                LogTrace($"An exception occurred while executing the query: {ex.Message}");
                return ex.Message;
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