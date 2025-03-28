// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Describes the GraphQL settings specific to an entity.
/// </summary>
/// <param name="Singular">The singular type name for the GraphQL object. If none is provided this will be generated by the Entity key.</param>
/// <param name="Plural">The pluralisation of the entity. If none is provided a pluralisation of the Singular property is used.</param>
/// <param name="Enabled">Indicates if GraphQL is enabled for the entity.</param>
/// <param name="Operation">When the entity maps to a stored procedure, this represents the GraphQL operation to use, otherwise it will be null.</param>
/// <seealso cref="https://engdic.org/singular-and-plural-noun-rules-definitions-examples"/>
public record EntityGraphQLOptions(string Singular, string Plural, bool Enabled = true, GraphQLOperation? Operation = null);
