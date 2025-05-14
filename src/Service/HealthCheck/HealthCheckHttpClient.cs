// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net.Http;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    /// <summary>
    /// This class is responsible for creating an HttpClient instance
    /// </summary>
    public class HealthCheckHttpClient
    {
        private readonly IHttpClientFactory _httpClientFactory;

        /// <summary>
        /// Constructor for HealthCheckHttpClient
        /// </summary>
        /// <param name="httpClientFactory">The IHttpClientFactory</param>
        public HealthCheckHttpClient(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Creates an HttpClient instance with the specified base address
        /// </summary>
        /// <param name="apiRoute">The API route</param>
        /// <returns>The HttpClient</returns>
        public HttpClient Create(string apiRoute)
        {
            HttpClient client = _httpClientFactory.CreateClient("HealthCheckClient");
            client.BaseAddress = new Uri(apiRoute);
            client.Timeout = TimeSpan.FromSeconds(200);
            return client;
        }
    }

}
