// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace Azure.DataApiBuilder.Core.Services.OpenAPI
{
    /// <summary>
    /// Helper class which returns the endpoint Swagger should use to fetch
    /// the OpenAPI description document to accommodate late bound
    /// and/or custom REST paths defined in the runtime config.
    /// </summary>
    public class SwaggerEndpointMapper : IEnumerable<UrlDescriptor>
    {
        private readonly RuntimeConfigProvider? _runtimeConfigProvider;

        /// <summary>
        /// Constructor to setup required services
        /// </summary>
        /// <param name="runtimeConfigProvider">RuntimeConfigProvider contains the reference to the
        /// configured REST path. Will be empty during late bound config, so returns default REST path for SwaggerUI.</param>
        public SwaggerEndpointMapper(RuntimeConfigProvider? runtimeConfigProvider)
        {
            _runtimeConfigProvider = runtimeConfigProvider;
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
            RuntimeConfig? config = _runtimeConfigProvider?.GetConfig();
            string configuredRestPath = config?.RestPath ?? RestRuntimeOptions.DEFAULT_PATH;
            yield return new UrlDescriptor { Name = "DataApibuilder-OpenAPI-PREVIEW", Url = $"{configuredRestPath}/{OpenApiDocumentor.OPENAPI_ROUTE}" };
        }

        /// <summary>
        /// Explicit implementation of the IEnumerator interface GetEnumerator method.
        /// (i.e. its implementation can only be called through a reference of type IEnumerator).
        /// </summary>
        /// <returns>Returns a new instance of IEnumerator that iterates over the URIs in the collection.</returns>
        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
