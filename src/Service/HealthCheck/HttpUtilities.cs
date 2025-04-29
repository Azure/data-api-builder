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
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Humanizer;
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
        private readonly ILogger<HttpUtilities> _logger;
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
            ILogger<HttpUtilities> logger,
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
        public string? ExecuteRestQuery(string restUriSuffix, string entityName, int first, string incomingRoleHeader, string incomingRoleToken)
        {
            string? errorMessage = null;
            try
            {
                // Base URL of the API that handles SQL operations
                string apiRoute = Utilities.GetServiceRoute(_apiRoute, restUriSuffix);
                if (string.IsNullOrEmpty(apiRoute))
                {
                    LogTrace("The API route is not available, hence HealthEndpoint is not available.");
                    return errorMessage;
                }

                if (!Program.CheckSanityOfUrl(apiRoute))
                {
                    LogTrace("Blocked outbound request due to invalid or unsafe URI.");
                    return "Blocked outbound request due to invalid or unsafe URI.";
                }

                // Create an instance of HttpClient
                using (HttpClient client = CreateClient(apiRoute))
                {
                    // Send a GET request to the API
                    apiRoute = $"{apiRoute}{Utilities.CreateHttpRestQuery(entityName, first)}";
                    HttpRequestMessage message = new(method: HttpMethod.Get, requestUri: apiRoute);
                    if (!string.IsNullOrEmpty(incomingRoleToken))
                    {
                        message.Headers.Add(AuthenticationOptions.CLIENT_PRINCIPAL_HEADER, incomingRoleToken);
                    }

                    if (!string.IsNullOrEmpty(incomingRoleHeader))
                    {
                        message.Headers.Add(AuthorizationResolver.CLIENT_ROLE_HEADER, incomingRoleHeader);
                    }

                    HttpResponseMessage response = client.SendAsync(message).Result;
                    if (response.IsSuccessStatusCode)
                    {
                        LogTrace($"The REST HealthEndpoint query executed successfully with code {response.IsSuccessStatusCode}.");
                    }
                    else
                    {
                        errorMessage = $"The REST HealthEndpoint query failed with code: {response.StatusCode}.";
                    }
                }

                return errorMessage;
            }
            catch (Exception ex)
            {
                LogTrace($"An exception occurred while executing the health check REST query: {ex.Message}");
                return ex.Message;
            }
        }

        // Executes the GraphQL query by sending a POST request to the API.
        // Internally calls the metadata provider to fetch the column names to create the graphql payload.
        public string? ExecuteGraphQLQuery(string graphqlUriSuffix, string entityName, Entity entity, string incomingRoleHeader, string incomingRoleToken)
        {
            string? errorMessage = null;
            // Base URL of the API that handles SQL operations
            string apiRoute = Utilities.GetServiceRoute(_apiRoute, graphqlUriSuffix);
            if (string.IsNullOrEmpty(apiRoute))
            {
                LogTrace("The API route is not available, hence HealthEndpoint is not available.");
                return errorMessage;
            }

            if (!Program.CheckSanityOfUrl(apiRoute))
            {
                LogTrace("Blocked outbound request due to invalid or unsafe URI.");
                return "Blocked outbound request due to invalid or unsafe URI.";
            }

            try
            {
                string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);

                // Fetch Column Names from Metadata Provider
                ISqlMetadataProvider sqlMetadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
                DatabaseObject dbObject = sqlMetadataProvider.EntityToDatabaseObject[entityName];
                List<string> columnNames = dbObject.SourceDefinition.Columns.Keys.ToList();

                // In case of GraphQL API, use the plural value specified in [entity.graphql.type.plural].
                // Further, we need to camel case this plural value to match the GraphQL object name.                  
                string graphqlObjectName = LowerFirstLetter(entity.GraphQL.Plural.Pascalize());

                // In case any primitive column names are present, execute the query
                if (columnNames.Any())
                {
                    using (HttpClient client = CreateClient(apiRoute))
                    {
                        string jsonPayload = Utilities.CreateHttpGraphQLQuery(graphqlObjectName, columnNames, entity.EntityFirst);
                        HttpContent content = new StringContent(jsonPayload, Encoding.UTF8, Utilities.JSON_CONTENT_TYPE);

                        HttpRequestMessage message = new(method: HttpMethod.Post, requestUri: apiRoute)
                        {
                            Content = content
                        };

                        if (!string.IsNullOrEmpty(incomingRoleToken))
                        {
                            message.Headers.Add(AuthenticationOptions.CLIENT_PRINCIPAL_HEADER, incomingRoleToken);
                        }

                        if (!string.IsNullOrEmpty(incomingRoleHeader))
                        {
                            message.Headers.Add(AuthorizationResolver.CLIENT_ROLE_HEADER, incomingRoleHeader);
                        }

                        HttpResponseMessage response = client.SendAsync(message).Result;

                        if (response.IsSuccessStatusCode)
                        {
                            LogTrace("The GraphQL HealthEndpoint query executed successfully.");
                        }
                        else
                        {
                            errorMessage = $"The GraphQL HealthEndpoint query failed with code: {response.StatusCode}.";
                        }
                    }
                }

                return errorMessage;
            }
            catch (Exception ex)
            {
                LogTrace($"An exception occurred while executing the GraphQL health check query: {ex.Message}");
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

        // Updates the entity key name to camel case for the health check report.
        public static string LowerFirstLetter(string input)
        {
            if (string.IsNullOrEmpty(input) || char.IsLower(input[0]))
            {
                // If the input is null or empty, or if the first character is already lowercase, return the input as is.
                return input;
            }

            return char.ToLower(input[0]) + input.Substring(1);
        }

        // <summary>
        /// Logs a trace message if a logger is present and the logger is enabled for trace events.
        /// </summary>
        /// <param name="message">Message to emit.</param>
        private void LogTrace(string message)
        {
            _logger.LogTrace(message);
        }
    }
}
