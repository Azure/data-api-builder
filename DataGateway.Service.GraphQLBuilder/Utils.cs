using System.Text.RegularExpressions;
using HotChocolate.Language;

namespace Azure.DataGateway.Service.GraphQLBuilder
{
    internal static class Utils
    {
        private static string[] SanitizeGraphQLName(string name)
        {
            if (!new Regex("^[a-zA-Z].*").Match(name).Success)
            {
                // strip an illegal first character
                name = name[1..];
            }

            name = new Regex("[^a-zA-Z0-9_\\s]").Replace(name, "");

            string[] nameSegments = name.Split(' ');
            return nameSegments;
        }

        public static string FormatNameForObject(string name)
        {
            string[] nameSegments = SanitizeGraphQLName(name);

            return string.Join("", nameSegments.Select(n => $"{char.ToUpperInvariant(n[0])}{n[1..]}"));
        }

        public static string FormatNameForField(string name)
        {
            string[] nameSegments = SanitizeGraphQLName(name);

            return string.Join("", nameSegments.Select((n, i) => $"{(i == 0 ? char.ToLowerInvariant(n[0]) : char.ToUpperInvariant(n[0]))}{n[1..]}"));
        }

        public static NameNode Pluralize(string name)
        {
            return new NameNode($"{FormatNameForField(name)}s");
        }
    }
}
