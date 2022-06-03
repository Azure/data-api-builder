using Azure.DataGateway.Config;
using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLUtils;

namespace Azure.DataGateway.Service.GraphQLBuilder.Mutations
{
    public static class CreateMutationBuilder
    {
        public const string INPUT_ARGUMENT_NAME = "item";

        /// <summary>
        /// Generate the GraphQL input type from an object type
        /// </summary>
        /// <param name="inputs">Reference table of all known input types.</param>
        /// <param name="objectTypeDefinitionNode">GraphQL object to generate the input type for.</param>
        /// <param name="name">Name of the GraphQL object type.</param>
        /// <param name="definitions">All named GraphQL items in the schema (objects, enums, scalars, etc.)</param>
        /// <param name="databaseType">Database type to generate input type for.</param>
        /// <param name="entity">Runtime config information.</param>
        /// <returns>A GraphQL input type with all expected fields mapped as GraphQL inputs.</returns>
        private static InputObjectTypeDefinitionNode GenerateCreateInputType(
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            NameNode name,
            IEnumerable<HotChocolate.Language.IHasName> definitions,
            DatabaseType databaseType,
            Entity entity)
        {
            NameNode inputName = GenerateInputTypeName(name.Value, entity);

            if (inputs.ContainsKey(inputName))
            {
                return inputs[inputName];
            }

            IEnumerable<InputValueDefinitionNode> inputFields =
                objectTypeDefinitionNode.Fields
                .Where(f => FieldAllowedOnCreateInput(f, databaseType, definitions))
                .Select(f =>
                {
                    if (!IsBuiltInType(f.Type))
                    {
                        string typeName = RelationshipDirectiveType.Target(f);
                        HotChocolate.Language.IHasName def = definitions.First(d => d.Name.Value == typeName);
                        if (def is ObjectTypeDefinitionNode otdn)
                        {
                            return GetComplexInputType(inputs, definitions, f, typeName, otdn, databaseType, entity);
                        }
                    }

                    return GenerateSimpleInputType(name, f, entity);
                });

            InputObjectTypeDefinitionNode input =
                new(
                    location: null,
                    inputName,
                    new StringValueNode($"Input type for creating {name}"),
                    new List<DirectiveNode>(),
                    inputFields.ToList()
                );

            inputs.Add(input.Name, input);
            return input;
        }

        /// <summary>
        /// This method is used to determine if a field is allowed to be sent from the client in a Create mutation (eg, id field is not settable during create).
        /// </summary>
        /// <param name="field">Field to check</param>
        /// <param name="databaseType">The type of database to generate for</param>
        /// <param name="definitions">The other named types in the schema</param>
        /// <returns>true if the field is allowed, false if it is not.</returns>
        private static bool FieldAllowedOnCreateInput(FieldDefinitionNode field, DatabaseType databaseType, IEnumerable<HotChocolate.Language.IHasName> definitions)
        {
            if (IsBuiltInType(field.Type))
            {
                // Cosmos doesn't have the concept of "auto increment" for the ID field, nor does it have "auto generate"
                // fields like timestap/etc. like SQL, so we're assuming that any built-in type will be user-settable
                // during the create mutation
                return databaseType switch
                {
                    DatabaseType.cosmos => true,
                    _ => !IsAutoGeneratedField(field),
                };
            }

            if (QueryBuilder.IsPaginationType(field.Type.NamedType()))
            {
                return false;
            }

            HotChocolate.Language.IHasName? definition = definitions.FirstOrDefault(d => d.Name.Value == field.Type.NamedType().Name.Value);
            // When creating, you don't need to provide the data for nested models, but you will for other nested types
            if (definition != null && definition is ObjectTypeDefinitionNode objectType && IsModelType(objectType))
            {
                return false;
            }

            return true;
        }

        private static InputValueDefinitionNode GenerateSimpleInputType(NameNode name, FieldDefinitionNode f, Entity entity)
        {
            IValueNode? defaultValue = null;

            if (DefaultValueDirectiveType.TryGetDefaultValue(f, out ObjectValueNode? value))
            {
                defaultValue = value.Fields[0].Value;
            }

            return new(
                location: null,
                f.Name,
                new StringValueNode($"Input for field {f.Name} on type {GenerateInputTypeName(name.Value, entity)}"),
                f.Type,
                defaultValue,
                new List<DirectiveNode>()
            );
        }

        /// <summary>
        /// Generates a GraphQL Input Type value for an object type, generally one provided from the database.
        /// </summary>
        /// <param name="inputs">Dictionary of all input types, allowing reuse where possible.</param>
        /// <param name="definitions">All named GraphQL types from the schema (objects, enums, etc.) for referencing.</param>
        /// <param name="field">Field that the input type is being generated for.</param>
        /// <param name="typeName">Name of the input type in the dictionary.</param>
        /// <param name="otdn">The GraphQL object type to create the input type for.</param>
        /// <param name="databaseType">Database type to generate the input type for.</param>
        /// <param name="entity">Runtime configuration information for the current type.</param>
        /// <returns>A GraphQL input type value.</returns>
        private static InputValueDefinitionNode GetComplexInputType(
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            IEnumerable<HotChocolate.Language.IHasName> definitions,
            FieldDefinitionNode field,
            string typeName,
            ObjectTypeDefinitionNode otdn,
            DatabaseType databaseType,
            Entity entity)
        {
            InputObjectTypeDefinitionNode node;
            NameNode inputTypeName = GenerateInputTypeName(typeName, entity);
            if (!inputs.ContainsKey(inputTypeName))
            {
                node = GenerateCreateInputType(inputs, otdn, field.Type.NamedType().Name, definitions, databaseType, entity);
            }
            else
            {
                node = inputs[inputTypeName];
            }

            ITypeNode type = new NamedTypeNode(node.Name);

            // For a type like [Bar!]! we have to first unpack the outer non-null
            if (field.Type.IsNonNullType())
            {
                // The innerType is the raw List, scalar or object type without null settings
                ITypeNode innerType = field.Type.InnerType();

                if (innerType.IsListType())
                {
                    type = GenerateListType(type, innerType);
                }

                // Wrap the input with non-null to match the field definition
                type = new NonNullTypeNode((INullableTypeNode)type);
            }
            else if (field.Type.IsListType())
            {
                type = GenerateListType(type, field.Type);
            }

            return new(
                location: null,
                field.Name,
                new StringValueNode($"Input for field {field.Name} on type {inputTypeName}"),
                type,
                defaultValue: null,
                field.Directives
            );
        }

        private static ITypeNode GenerateListType(ITypeNode type, ITypeNode fieldType)
        {
            // Look at the inner type of the list type, eg: [Bar]'s inner type is Bar
            // and if it's nullable, make the input also nullable
            return fieldType.InnerType().IsNonNullType()
                ? new ListTypeNode(new NonNullTypeNode((INullableTypeNode)type))
                : new ListTypeNode(type);
        }

        private static NameNode GenerateInputTypeName(string typeName, Entity entity)
        {
            return new($"{Operation.Create}{FormatNameForObject(typeName, entity)}Input");
        }

        /// <summary>
        /// Generate the `create` mutation field for the GraphQL mutations for a given Object Definition
        /// </summary>
        /// <param name="name">Name of the GraphQL object to generate the create field for.</param>
        /// <param name="inputs">All known GraphQL input types.</param>
        /// <param name="objectTypeDefinitionNode">The GraphQL object type to generate for.</param>
        /// <param name="root">The GraphQL document root to find GraphQL schema items in.</param>
        /// <param name="databaseType">Type of database we're generating the field for.</param>
        /// <param name="entity">Runtime config information for the type.</param>
        /// <returns>A GraphQL field definition named <c>create*EntityName*</c> to be attached to the Mutations type in the GraphQL schema.</returns>
        public static FieldDefinitionNode Build(
            NameNode name,
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            DocumentNode root,
            DatabaseType databaseType,
            Entity entity)
        {
            InputObjectTypeDefinitionNode input = GenerateCreateInputType(
                inputs,
                objectTypeDefinitionNode,
                name,
                root.Definitions.Where(d => d is HotChocolate.Language.IHasName).Cast<HotChocolate.Language.IHasName>(),
                databaseType,
                entity);

            return new(
                location: null,
                new NameNode($"create{FormatNameForObject(name, entity)}"),
                new StringValueNode($"Creates a new {name}"),
                new List<InputValueDefinitionNode> {
                new InputValueDefinitionNode(
                    location : null,
                    new NameNode(INPUT_ARGUMENT_NAME),
                    new StringValueNode($"Input representing all the fields for creating {name}"),
                    new NonNullTypeNode(new NamedTypeNode(input.Name)),
                    defaultValue: null,
                    new List<DirectiveNode>())
                },
                new NamedTypeNode(FormatNameForObject(name, entity)),
                new List<DirectiveNode>()
            );
        }
    }
}
