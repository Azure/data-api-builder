// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// Centralized helper for surfacing the auto-embed indicator in cross-layer
    /// parameter descriptions (REST OpenAPI, GraphQL schema, MCP tools). The indicator
    /// tells callers that DAB will convert their text input to an embedding vector
    /// before passing it to the stored procedure.
    /// </summary>
    public static class AutoEmbedDescription
    {
        /// <summary>
        /// Suffix indicating that a parameter is auto-embedded. Includes a leading
        /// space so callers can append directly to an existing description without
        /// inserting their own separator.
        /// </summary>
        /// <remarks>
        /// Internal (not private) so the matching test in Service.Tests can compose
        /// expected values via the existing
        /// <c>InternalsVisibleTo("Azure.DataApiBuilder.Service.Tests")</c>
        /// declared on the Config assembly. Not part of the public API.
        /// </remarks>
        internal const string INDICATOR_SUFFIX = " (auto-embed: DAB converts this value to an embedding before execution)";

        /// <summary>
        /// Appends <see cref="INDICATOR_SUFFIX"/> to <paramref name="baseDescription"/> when
        /// <paramref name="isAutoEmbed"/> is true. Returns the base unchanged
        /// otherwise, preserving null-ness for callers that distinguish null
        /// from empty (e.g., OpenAPI schema descriptions where null is omitted
        /// from the output and empty string is included).
        /// </summary>
        public static string? Append(string? baseDescription, bool isAutoEmbed) =>
            isAutoEmbed ? (baseDescription ?? string.Empty) + INDICATOR_SUFFIX : baseDescription;
    }
}
