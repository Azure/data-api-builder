using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using HotChocolate.Language;
using Humanizer;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder
{
    public static class GraphQLNaming
    {
        // Name must start with an upper or lowercase letter
        // Matches to this regular expression are names with valid prefix.
        private static readonly Regex _graphQLNameStart = new("^[a-zA-Z].*");

        // Regex used to identify strings that do not have the defined GraphQL characters.
        // Letters, numbers and _ are only valid in names, so strip all that aren't.
        // Although we'll leave whitespace in so that downstream consumers can still
        // enforce their casing requirements
        private static readonly Regex _graphQLValidSymbols = new("[^a-zA-Z0-9_]");

        /// <summary>
        /// Enforces the GraphQL naming restrictions on <paramref name="name"/>.
        /// Completely removes invalid characters from the input parameter: name.
        /// Splits up the name into segments where *space* is the splitting token.
        /// </summary>
        /// <param name="name">String the enforce naming rules on</param>
        /// <seealso cref="https://spec.graphql.org/October2021/#Name"/>
        /// <returns>nameSegments, where each indice is a part of the name that complies with the GraphQL name rules.</returns>
        public static string[] SanitizeGraphQLName(string name)
        {
            if (ViolatesNamePrefixRequirements(name))
            {
                // strip an illegal first character
                name = name[1..];
            }

            name = _graphQLValidSymbols.Replace(name, "");

            string[] nameSegments = name.Split(' ');
            return nameSegments;
        }

        /// <summary>
        /// Checks whether name has invalid characters at the start of the name provided.
        /// - GraphQL specification requires that a name start with an upper or lowercase letter.
        /// </summary>
        /// <param name="name">Name to be checked.</param>
        /// <seealso cref="https://spec.graphql.org/October2021/#Name"/>
        /// <returns>True if the provided name violates requirements.</returns>
        public static bool ViolatesNamePrefixRequirements(string name)
        {
            return !_graphQLNameStart.Match(name).Success;
        }

        /// <summary>
        /// Checks whether name has invalid characters.
        /// - GraphQL specification requires that a name does not contain anything other than
        /// upper or lowercase letters or numbers.
        /// </summary>
        /// <param name="name">Name to be checked.</param>
        /// <seealso cref="https://spec.graphql.org/October2021/#Name"/>
        /// <returns>True if the provided name violates requirements.</returns>
        public static bool ViolatesNameRequirements(string name)
        {
            return _graphQLValidSymbols.Match(name).Success;
        }

        /// <summary>
        /// Attempts to deserialize and get the SingularPlural GraphQL naming config
        /// of an Entity from the Runtime Configuration.
        /// </summary>
        public static string GetDefinedSingularName(string name, Entity configEntity)
        {
            if (TryGetConfiguredGraphQLName(configEntity, out string? graphQLName) &&
                !string.IsNullOrEmpty(graphQLName))
            {
                name = graphQLName;
            }
            else if (TryGetSingularPluralConfiguration(configEntity, out SingularPlural? singularPluralConfig) &&
                !string.IsNullOrEmpty(singularPluralConfig.Singular))
            {
                name = singularPluralConfig.Singular;
            }

            return name;
        }

        /// <summary>
        /// Attempts to deserialize and get the SingularPlural GraphQL naming config
        /// of an Entity from the Runtime Configuration.
        /// </summary>
        /// <param name="configEntity">Entity to fetch GraphQL naming, if set.</param>
        /// <param name="singularPluralConfig">Entity's configured GraphQL singular/plural naming.</param>
        /// <returns>True if configuration found, false otherwise.</returns>
        public static bool TryGetSingularPluralConfiguration(Entity configEntity, [NotNullWhen(true)] out SingularPlural? singularPluralConfig)
        {
            if (configEntity.GraphQL is not null && configEntity.GraphQL is GraphQLEntitySettings graphQLEntitySettings)
            {
                if (graphQLEntitySettings is not null && graphQLEntitySettings.Type is SingularPlural singularPlural)
                {
                    if (singularPlural is not null)
                    {
                        singularPluralConfig = singularPlural;
                        return true;
                    }
                }
            }

            singularPluralConfig = null;
            return false;
        }

        public static bool TryGetConfiguredGraphQLName(Entity configEntity, [NotNullWhen(true)] out string? graphQLName)
        {
            if (configEntity.GraphQL is not null && configEntity.GraphQL is GraphQLEntitySettings graphQLEntitySettings)
            {
                if (graphQLEntitySettings is not null && graphQLEntitySettings.Type is string typeEntityName)
                {
                    graphQLName = typeEntityName;
                    return true;
                }
            }

            graphQLName = null;
            return false;
        }

        /// <summary>
        /// Format fields generated by the runtime aligning with
        /// GraphQL best practices.
        /// </summary>
        /// <param name="name"></param>
        /// <seealso cref="https://github.com/hendrikniemann/graphql-style-guide#fields"/>
        /// <returns></returns>
        public static string FormatNameForField(string name)
        {
            string[] nameSegments = SanitizeGraphQLName(name);

            return string.Join("", nameSegments.Select((n, i) => $"{(i == 0 ? char.ToLowerInvariant(n[0]) : char.ToUpperInvariant(n[0]))}{n[1..]}"));
        }

        public static string FormatNameForField(NameNode name)
        {
            return FormatNameForField(name.Value);
        }

        /// <summary>
        /// Helper to pluralize the value of a NameNode HotChocolate schema object
        /// </summary>
        /// <param name="name">HotChocolate schema object type NameNode</param>
        /// <param name="configEntity">Entity definition from runtime configuration.</param>
        /// <returns></returns>
        public static NameNode Pluralize(NameNode name, Entity configEntity)
        {
            return Pluralize(name.Value, configEntity);
        }

        /// <summary>
        /// Helper to pluralize the passed in string with the plural name defined
        /// for the entity in the runtime configuration.
        /// If the plural name is not defined, use the singularName.Pluralize() value
        /// and if that does not exist, use the top-level entity name value, pluralized.
        /// </summary>
        /// <param name="name">string representing a name to pluralize</param>
        /// <param name="configEntity">Entity definition from runtime configuration.</param>
        /// <returns></returns>
        public static NameNode Pluralize(string name, Entity configEntity)
        {
            if (TryGetConfiguredGraphQLName(configEntity, out string? graphQLName) &&
                !string.IsNullOrEmpty(graphQLName))
            {
                return new NameNode(graphQLName.Pluralize());
            }
            else if (TryGetSingularPluralConfiguration(configEntity, out SingularPlural? namingRules) &&
                !string.IsNullOrEmpty(namingRules.Plural))
            {
                return new NameNode(namingRules.Plural);
            }

            return new NameNode(name.Pluralize());
        }

        /// <summary>
        /// Given an object type definition i.e. type EntityName @model(name:TopLevelEntityName)
        /// Get the value assigned to the model directive which is the top-level entity name.
        /// If no model directive exists, the name set on the object type definition is returned.
        /// </summary>
        /// <param name="node">Object type definition</param>
        /// <returns>string representing the top-level entity name defined in runtime configuration.</returns>
        public static string ObjectTypeToEntityName(ObjectTypeDefinitionNode node)
        {
            DirectiveNode modelDirective = node.Directives.First(d => d.Name.Value == ModelDirectiveType.DirectiveName);

            return modelDirective.Arguments.Count == 1 ? (string)(modelDirective.Arguments[0].Value.Value ?? node.Name.Value) : node.Name.Value;
        }
    }
}
