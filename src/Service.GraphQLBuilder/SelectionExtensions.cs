// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder
{
    /// <summary>
    /// Extension methods over Hot Chocolate's <see cref="Selection"/> type.
    /// </summary>
    public static class SelectionExtensions
    {
        /// <summary>
        /// Returns the first <see cref="FieldNode"/> backing the given <paramref name="selection"/>,
        /// failing fast with a targeted <see cref="DataApiBuilderException"/> when no syntax node
        /// is available.
        /// </summary>
        /// <remarks>
        /// Hot Chocolate v16 introduced <c>Selection.SyntaxNodes</c> (a span) to support field-merging
        /// across multiple selection-set occurrences of the same field. Indexing directly with
        /// <c>SyntaxNodes[0]</c> would surface as an <see cref="IndexOutOfRangeException"/> at request
        /// time if the span were ever empty. In practice an executable selection always has at least
        /// one syntax node, so an empty span is an invariant violation rather than a legitimate
        /// "no field" signal — surface it as a clear DAB error.
        /// </remarks>
        public static FieldNode RequireFieldNode(this Selection selection)
        {
            // SyntaxNodes is a ReadOnlySpan<FieldSelectionNode>, so LINQ helpers (e.g. FirstOrDefault)
            // are not available; check IsEmpty before indexing.
            ReadOnlySpan<FieldSelectionNode> syntaxNodes = selection.SyntaxNodes;
            if (syntaxNodes.IsEmpty)
            {
                throw new DataApiBuilderException(
                    message: $"GraphQL selection '{selection.ResponseName}' has no syntax node available.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            return syntaxNodes[0].Node;
        }
    }
}
