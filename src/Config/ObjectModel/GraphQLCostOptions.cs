// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Options controlling GraphQL cost analysis (runtime.graphql.cost).
/// Cost analysis estimates the cost of a request before it is executed. When enabled,
/// requests whose estimated cost exceeds the configured maximums are rejected.
/// </summary>
/// <param name="Enabled">
/// Whether cost limits are enforced. Defaults to false: configuring a maximum alone does not enforce it.
/// When cost options are not configured at all, limits are not enforced.
/// </param>
/// <param name="MaxFieldCost">Maximum allowed field cost of a request. Null uses the GraphQL engine default.</param>
/// <param name="MaxTypeCost">Maximum allowed type cost of a request. Null uses the GraphQL engine default.</param>
public record GraphQLCostOptions(bool Enabled = false, int? MaxFieldCost = null, int? MaxTypeCost = null);
