using System.Text.RegularExpressions;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using HotChocolate.Language;
using Humanizer;

namespace Azure.DataGateway.Service.GraphQLBuilder
{
    public static class GraphQLNaming
    {
        // Name must start with an upper or lowercase letter
        private static readonly Regex _graphQLNameStart = new("^[a-zA-Z].*");

        // Regex to match invalid GraphQL characters.
        // Letters, numbers and _ are only valid in names, so strip all that aren't.
        // Although we'll leave whitespace in so that downstream consumers can still
        // enforce their casing requirements
        private static readonly Regex _graphQLInvalidSymbols = new("[^a-zA-Z0-9_\\s]");

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

            name = _graphQLInvalidSymbols.Replace(name, "");

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
        /// - GraphQL specification requires that a name start with an upper or lowercase letter.
        /// </summary>
        /// <param name="name">Name to be checked.</param>
        /// <seealso cref="https://spec.graphql.org/October2021/#Name"/>
        /// <returns>True if the provided name violates requirements.</returns>
        public static bool ViolatesNameRequirements(string name)
        {
            return _graphQLInvalidSymbols.Match(name).Success;
        }

        /// <summary>
        /// Formats the name argument into a GraphQL allowed string by:
        /// - Singularizes name
        /// - Removing disallowed characters
        /// - Capitalizing the first character of each space separated segment of the string
        /// </summary>
        /// <param name="name">Entity name to format.</param>
        /// <param name="configEntity">Contain GraphQL configuration metadata for the entity.</param>
        /// <returns></returns>
        public static string FormatNameForObject(string name, Entity configEntity)
        {
            if (configEntity.GraphQL is SingularPlural namingRules)
            {
                name = string.IsNullOrEmpty(namingRules.Singular) ? name : namingRules.Singular;
            }

            string[] nameSegments = SanitizeGraphQLName(name);

            return string.Join("", nameSegments.Select(n => $"{char.ToUpperInvariant(n[0])}{n[1..]}"));
        }

        public static string FormatNameForObject(NameNode name, Entity configEntity)
        {
            return FormatNameForObject(name.Value, configEntity);
        }

        public static string FormatNameForField(string name)
        {
            string[] nameSegments = SanitizeGraphQLName(name);

            return string.Join("", nameSegments.Select((n, i) => $"{(i == 0 ? char.ToLowerInvariant(n[0]) : char.ToUpperInvariant(n[0]))}{n[1..]}"));
        }

        public static string FormatNameForField(NameNode name)
        {
            return FormatNameForField(name.Value);
        }

        public static NameNode Pluralize(NameNode name, Entity configEntity)
        {
            return Pluralize(name.Value, configEntity);
        }

        public static NameNode Pluralize(string name, Entity configEntity)
        {
            if (configEntity.GraphQL is SingularPlural namingRules)
            {
                if (!string.IsNullOrEmpty(namingRules.Plural))
                {
                    return new NameNode(namingRules.Plural);
                }

                name = string.IsNullOrEmpty(namingRules.Singular) ? name : namingRules.Singular;
            }

            return new NameNode(FormatNameForField(name).Pluralize());
        }

        public static string ObjectTypeToEntityName(ObjectTypeDefinitionNode node)
        {
            DirectiveNode modelDirective = node.Directives.First(d => d.Name.Value == ModelDirectiveType.DirectiveName);

            return modelDirective.Arguments.Count == 1 ? (string)(modelDirective.Arguments[0].Value.Value ?? node.Name.Value) : node.Name.Value;
        }
    }
}
