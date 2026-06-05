// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Models;

public static class SemanticSearchConstants
{
    public const string REST_SEARCH_QUERY_PARAM = "$semantic_search";
    public const string REST_THRESHOLD_QUERY_PARAM = "$semantic_threshold";
    public const string REST_DISTANCE_FIELD = "semantic_distance";

    public const string GRAPHQL_SEARCH_ARGUMENT = "semanticSearch";
    public const string GRAPHQL_THRESHOLD_ARGUMENT = "semanticThreshold";
    public const string GRAPHQL_DISTANCE_FIELD = "semanticDistance";
}
