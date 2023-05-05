// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace Azure.DataApiBuilder.Service.Services.OpenAPI
{
    /// <summary>
    /// Helper class which returns the endpoint Swagger should use to fetch
    /// the OpenAPI description document to accommodate late bound
    /// and/or custom REST paths defined in the runtime config.
    /// </summary>
    public class SwaggerEndpointMapper : IEnumerable<UrlDescriptor>
    {
        private readonly RuntimeConfigProvider _runtimeConfigurationProvider;

        // Default configured REST endpoint path used when
        // not defined or customized in runtime configuration.
        private const string DEFAULT_REST_PATH = "/api";

        public SwaggerEndpointMapper(RuntimeConfigProvider runtimeConfigurationProvider)
        {
            _runtimeConfigurationProvider = runtimeConfigurationProvider;
        }

        /// <summary>
        /// Returns an enumerator whose value is the route which Swagger should use to
        /// fetch the OpenAPI description document.
        /// Format: /{RESTAPIPATH}/openapi
        /// The yield return statement is used to return each element of a collection one at a time.
        /// When used in a method, it indicates that the method is returning an iterator.
        /// </summary>
        /// <returns>Returns a new instance of IEnumerator that iterates over the URIs in the collection.</returns>
        public IEnumerator<UrlDescriptor> GetEnumerator()
        {
            if (!TryGetRestRouteFromConfig(out string? configuredRestRoute))
            {
                configuredRestRoute = DEFAULT_REST_PATH;
            }

            yield return new UrlDescriptor { Name = "DataApibuilder-OpenAPI-PREVIEW", Url = $"{configuredRestRoute}/{OpenApiDocumentor.OPENAPI_ROUTE}" };
        }

        /// <summary>
        /// Explicit implementation of the IEnumerator interface GetEnumerator method.
        /// (i.e. its implementation can only be called through a reference of type IEnumerator).
        /// </summary>
        /// <returns>Returns a new instance of IEnumerator that iterates over the URIs in the collection.</returns>
        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        /// <summary>
        /// When configuration exists and the REST endpoint is enabled,
        /// return the configured REST endpoint path. 
        /// </summary>
        /// <param name="configuredRestRoute">The configured REST route path</param>
        /// <returns>True when configuredRestRoute is defined, otherwise false.</returns>
        private bool TryGetRestRouteFromConfig([NotNullWhen(true)] out string? configuredRestRoute)
        {
            if (_runtimeConfigurationProvider.TryGetRuntimeConfiguration(out RuntimeConfig? config) &&
                config.RestGlobalSettings.Enabled)
            {
                configuredRestRoute = config.RestGlobalSettings.Path;
                return true;
            }

            configuredRestRoute = null;
            return false;
        }
    }
}
