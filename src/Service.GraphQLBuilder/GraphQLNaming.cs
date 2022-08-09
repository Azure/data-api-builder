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
        private static readonly Regex _graphQLNameStart = new("^[a-zA-Z].*");

        // Letters, numbers and _ are only valid in names, so strip all that aren't.
        // Although we'll leave whitespace in so that downstream consumers can still
        // enforce their casing requirements
        private static readonly Regex _graphQLValidSymbols = new("[^a-zA-Z0-9_\\s]");

        /// <summary>
        /// Enforces the GraphQL naming restrictions on <paramref name="name"/>.
        /// </summary>
        /// <param name="name">String the enforce naming rules on</param>
        /// <seealso cref="https://spec.graphql.org/October2021/#Name"/>
        /// <returns>A name that complies with the GraphQL name rules</returns>
        private static string[] SanitizeGraphQLName(string name)
        {
            if (!_graphQLNameStart.Match(name).Success)
            {
                // strip an illegal first character
                name = name[1..];
            }

            name = _graphQLValidSymbols.Replace(name, "");

            string[] nameSegments = name.Split(' ');
            return nameSegments;
        }

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
