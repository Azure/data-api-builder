// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
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
        private HttpClient _httpClient;
        private IMetadataProviderFactory _metadataProviderFactory;
        private RuntimeConfigProvider _runtimeConfigProvider;

        /// <summary>
        /// HttpUtility constructor.
        /// </summary>
        /// <param name="logger">Logger</param>
        /// <param name="metadataProviderFactory">MetadataProviderFactory</param>
        /// <param name="runtimeConfigProvider">RuntimeConfigProvider</param>
        /// <param name="httpClientFactory">HttpClientFactory</param>
        public HttpUtilities(
            ILogger<HttpUtilities> logger,
            IMetadataProviderFactory metadataProviderFactory,
            RuntimeConfigProvider runtimeConfigProvider,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _metadataProviderFactory = metadataProviderFactory;
            _runtimeConfigProvider = runtimeConfigProvider;
            _httpClient = httpClientFactory.CreateClient("ContextConfiguredHealthCheckClient");
        }

        // Executes the DB query by establishing a connection to the DB.
        public async Task<string?> ExecuteDbQueryAsync(string query, string connectionString)
        {
            string? errorMessage = null;
            // Execute the query on DB and return the response time.
            using (SqlConnection connection = new(connectionString))
            {
                try
                {
                    SqlCommand command = new(query, connection);
                    connection.Open();
                    SqlDataReader reader = await command.ExecuteReaderAsync();
                    _logger.LogTrace("The health check query for datasource executed successfully.");
                    reader.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"An exception occurred while executing the health check query: {ex.Message}");
                    errorMessage = ex.Message;
                }
            }

            return errorMessage;
        }

        // Executes the REST query by sending a GET request to the API.
        public async Task<string?> ExecuteRestQueryAsync(string restUriSuffix, string entityName, int first, string incomingRoleHeader, string incomingRoleToken)
        {
            string? errorMessage = null;
            try
            {
                // Base URL of the API that handles SQL operations
                string apiRoute = $"{restUriSuffix}{Utilities.CreateHttpRestQuery(entityName, first)}";
                if (string.IsNullOrEmpty(apiRoute))
                {
                    _logger.LogError("The API route is not available, hence HealthEndpoint is not available.");
                    return errorMessage;
                }

                if (!Program.CheckSanityOfUrl($"{_httpClient.BaseAddress}{restUriSuffix}"))
                {
                    _logger.LogError("Blocked outbound request due to invalid or unsafe URI.");
                    return "Blocked outbound request due to invalid or unsafe URI.";
                }

                HttpRequestMessage message = new(method: HttpMethod.Get, requestUri: apiRoute);
                if (!string.IsNullOrEmpty(incomingRoleToken))
                {
                    message.Headers.Add(AuthenticationOptions.CLIENT_PRINCIPAL_HEADER, incomingRoleToken);
                }

                if (!string.IsNullOrEmpty(incomingRoleHeader))
                {
                    message.Headers.Add(AuthorizationResolver.CLIENT_ROLE_HEADER, incomingRoleHeader);
                }

                HttpResponseMessage response = await _httpClient.SendAsync(message);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogTrace($"The REST HealthEndpoint query executed successfully with code {response.IsSuccessStatusCode}.");
                }
                else
                {
                    errorMessage = $"The REST HealthEndpoint query failed with code: {response.StatusCode}.";
                }

                return errorMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError($"An exception occurred while executing the health check REST query: {ex.Message}");
                return ex.Message;
            }
        }

        // Executes the GraphQL query by sending a POST request to the API.
        // Internally calls the metadata provider to fetch the column names to create the graphql payload.
        public async Task<string?> ExecuteGraphQLQueryAsync(string graphqlUriSuffix, string entityName, Entity entity, string incomingRoleHeader, string incomingRoleToken)
        {
            string? errorMessage = null;

            if (!Program.CheckSanityOfUrl($"{_httpClient.BaseAddress}{graphqlUriSuffix}"))
            {
                _logger.LogError("Blocked outbound request due to invalid or unsafe URI.");
                return "Blocked outbound request due to invalid or unsafe URI.";
            }

            try
            {
                string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);

                // Fetch Column Names from Metadata Provider
                ISqlMetadataProvider sqlMetadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
                DatabaseObject dbObject = sqlMetadataProvider.GetDatabaseObjectByKey(entityName);
                List<string> columnNames = dbObject.SourceDefinition.Columns.Keys.ToList();

                // In case of GraphQL API, use the plural value specified in [entity.graphql.type.plural].
                // Further, we need to camel case this plural value to match the GraphQL object name.                  
                string graphqlObjectName = GraphQLNaming.GenerateListQueryName(entityName, entity);

                // In case any primitive column names are present, execute the query
                if (columnNames.Any())
                {
                    string jsonPayload = Utilities.CreateHttpGraphQLQuery(graphqlObjectName, columnNames, entity.EntityFirst);
                    HttpContent content = new StringContent(jsonPayload, Encoding.UTF8, Utilities.JSON_CONTENT_TYPE);

                    HttpRequestMessage message = new(method: HttpMethod.Post, requestUri: graphqlUriSuffix)
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

                    HttpResponseMessage response = await _httpClient.SendAsync(message);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogTrace("The GraphQL HealthEndpoint query executed successfully.");
                    }
                    else
                    {
                        errorMessage = $"The GraphQL HealthEndpoint query failed with code: {response.StatusCode}.";
                    }
                }

                return errorMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError($"An exception occurred while executing the GraphQL health check query: {ex.Message}");
                return ex.Message;
            }
        }
    }
}
