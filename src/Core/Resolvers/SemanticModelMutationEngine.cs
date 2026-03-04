// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Read-only mutation engine stub for semantic models.
    /// All mutation operations are unsupported.
    /// </summary>
    public class SemanticModelMutationEngine : IMutationEngine
    {
        private const string NOT_SUPPORTED_MESSAGE =
            "Semantic models are read-only. Mutation operations are not supported.";

        /// <inheritdoc />
        public Task<Tuple<JsonDocument?, IMetadata?>> ExecuteAsync(
            IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            string dataSourceName)
        {
            throw new NotSupportedException(NOT_SUPPORTED_MESSAGE);
        }

        /// <inheritdoc />
        public Task<IActionResult?> ExecuteAsync(RestRequestContext context)
        {
            throw new NotSupportedException(NOT_SUPPORTED_MESSAGE);
        }

        /// <inheritdoc />
        public Task<IActionResult?> ExecuteAsync(
            StoredProcedureRequestContext context,
            string dataSourceName)
        {
            throw new NotSupportedException(NOT_SUPPORTED_MESSAGE);
        }

        /// <inheritdoc />
        public void AuthorizeMutation(
            IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            string entityName,
            EntityActionOperation mutationOperation)
        {
            throw new NotSupportedException(NOT_SUPPORTED_MESSAGE);
        }
    }
}
