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

        public static NameNode Pluralize(NameNode name)
        {
            return new NameNode($"{name}s");
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
