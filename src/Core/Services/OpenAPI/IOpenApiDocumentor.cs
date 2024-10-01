// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// Interface for the service which generates and provides the OpenAPI description document
    /// describing the DAB engine's entity REST endpoint paths.
    /// </summary>
    public interface IOpenApiDocumentor
    {
        /// <summary>
        /// Attempts to return the OpenAPI description document, if generated.
        /// </summary>
        /// <param name="document">String representation of JSON OpenAPI description document.</param>
        /// <returns>True (plus string representation of document), when document exists. False, otherwise.</returns>
        public bool TryGetDocument([NotNullWhen(true)] out string? document);

        /// <summary>
        /// Creates an OpenAPI description document using OpenAPI.NET.
        /// Document compliant with patches of OpenAPI V3.0 spec 3.0.0 and 3.0.1,
        /// aligned with specification support provided by Microsoft.OpenApi.
        /// </summary>
        /// <exception cref="DataApiBuilderException">Raised when document is already generated
        /// or a failure occurs during generation.</exception>
        /// <seealso cref="https://github.com/microsoft/OpenAPI.NET/blob/1.6.3/src/Microsoft.OpenApi/OpenApiSpecVersion.cs"/>
        public void CreateDocument(bool isHotReloadScenario);
    }
}
