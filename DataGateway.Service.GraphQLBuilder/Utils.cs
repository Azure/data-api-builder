using System.Linq;
using HotChocolate.Language;

namespace Azure.DataGateway.Service.GraphQLBuilder
{
    internal static class Utils
    {
        public static bool IsModelType(ObjectTypeDefinitionNode objectTypeDefinitionNode)
        {
            string modelDirectiveName = CustomDirectives.ModelTypeDirectiveName;
            return objectTypeDefinitionNode.Directives.Any(d => d.Name.ToString() == modelDirectiveName);
        }

        public static string FormatNameForField(NameNode name)
        {
            string rawName = name.Value;
            return $"{char.ToLowerInvariant(rawName[0])}{rawName[1..]}";
        }

        public static NameNode Pluralize(NameNode name)
        {
            return new NameNode($"{FormatNameForField(name)}s");
        }

        public static bool IsBuiltInType(ITypeNode typeNode)
        {
            string name = typeNode.NamedType().Name.Value;
            if (name == "String" || name == "Int" || name == "Boolean" || name == "Float" || name == "ID")
            {
                return true;
            }

            return false;
        }

    }
}
