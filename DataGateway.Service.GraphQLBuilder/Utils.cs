using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using HotChocolate.Language;

namespace Azure.DataGateway.Service.GraphQLBuilder
{
    internal static class Utils
    {
        public static bool IsModelType(ObjectTypeDefinitionNode objectTypeDefinitionNode)
        {
            string modelDirectiveName = ModelDirectiveType.DirectiveName;
            return objectTypeDefinitionNode.Directives.Any(d => d.Name.ToString() == modelDirectiveName);
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

        public static FieldDefinitionNode FindIdField(ObjectTypeDefinitionNode node)
        {
            return node.Fields.First(f => f.Name.Value == "id");
        }
    }
}
