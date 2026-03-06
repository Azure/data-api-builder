// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLUtils;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Queries
{
    public static class InputTypeBuilder
    {
        public static void GenerateInputTypesForObjectType(ObjectTypeDefinitionNode node, IDictionary<string, InputObjectTypeDefinitionNode> inputTypes)
        {
            GenerateOrderByInputTypeForObjectType(node, inputTypes);
            GenerateFilterInputTypeForObjectType(node, inputTypes);
        }

        public static void GenerateFilterInputTypeForObjectType(
            ObjectTypeDefinitionNode node,
            IDictionary<string, InputObjectTypeDefinitionNode> inputTypes
        )
        {
            List<InputValueDefinitionNode> inputFields = GenerateFilterInputFieldsForBuiltInFields(node, inputTypes);
            
            // Only create Filter input type if there are actual filterable fields
            // Types with only nested objects shouldn't have Filter inputs (just "and"/"or" would be circular/useless)
            if (inputFields.Count == 0)
            {
                return;
            }
            
            string filterInputName = GenerateObjectInputFilterName(node);
            GenerateFilterInputTypeFromInputFields(inputTypes, inputFields, filterInputName, $"Filter input for {node.Name} GraphQL type");
        }

        internal static void GenerateOrderByInputTypeForObjectType(ObjectTypeDefinitionNode node, IDictionary<string, InputObjectTypeDefinitionNode> inputTypes)
        {
            List<InputValueDefinitionNode> inputFields = GenerateOrderByInputFieldsForBuiltInFields(node);
            
            // Only create OrderBy input type if there are actual orderable fields (scalars)
            // Types with only nested objects (like Accents, Palette) shouldn't have OrderBy inputs
            if (inputFields.Count == 0)
            {
                return;
            }
            
            string orderByInputName = GenerateObjectInputOrderByName(node);

            // OrderBy does not include "and" and "or" input types so we add only the orderByInputName here.
            // Check if the input type already exists to avoid duplicate key errors when using multiple data sources
            if (!inputTypes.ContainsKey(orderByInputName))
            {
                inputTypes.Add(
                    orderByInputName,
                    new(
                        location: null,
                        new NameNode(orderByInputName),
                        new StringValueNode($"Order by input for {node.Name} GraphQL type"),
                        new List<DirectiveNode>(),
                        inputFields
                        )
                    );
            }
        }

        private static List<InputValueDefinitionNode> GenerateOrderByInputFieldsForBuiltInFields(ObjectTypeDefinitionNode node)
        {
            List<InputValueDefinitionNode> inputFields = new();
            foreach (FieldDefinitionNode field in node.Fields)
            {
                if (IsBuiltInType(field.Type))
                {
                    inputFields.Add(
                        new(
                            location: null,
                            field.Name,
                            new StringValueNode($"Order by options for {field.Name}"),
                            new NamedTypeNode(OrderByType.EnumName),
                            defaultValue: null,
                            new List<DirectiveNode>())
                        );
                }
            }

            return inputFields;
        }

        private static void GenerateFilterInputTypeFromInputFields(
            IDictionary<string, InputObjectTypeDefinitionNode> inputTypes,
            List<InputValueDefinitionNode> inputFields,
            string inputTypeName,
            string inputTypeDescription)
        {
            inputFields.Add(
                new(
                    location: null,
                    new("and"),
                    new("Conditions to be treated as AND operations"),
                    new ListTypeNode(new NamedTypeNode(inputTypeName)),
                    defaultValue: null,
                    new List<DirectiveNode>()));

            inputFields.Add(
                new(
                    location: null,
                    new("or"),
                    new("Conditions to be treated as OR operations"),
                    new ListTypeNode(new NamedTypeNode(inputTypeName)),
                    defaultValue: null,
                    new List<DirectiveNode>()));

            // Check if the input type already exists to avoid duplicate key errors when using multiple data sources
            if (!inputTypes.ContainsKey(inputTypeName))
            {
                inputTypes.Add(
                    inputTypeName,
                    new(
                        location: null,
                        new NameNode(inputTypeName),
                        new StringValueNode(inputTypeDescription),
                        new List<DirectiveNode>(),
                        inputFields
                    )
                );
            }
        }

        private static List<InputValueDefinitionNode> GenerateFilterInputFieldsForBuiltInFields(
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            IDictionary<string, InputObjectTypeDefinitionNode> inputTypes)
        {
            List<InputValueDefinitionNode> inputFields = new();
            foreach (FieldDefinitionNode field in objectTypeDefinitionNode.Fields)
            {
                string fieldTypeName = field.Type.NamedType().Name.Value;
                bool isListType = field.Type is ListTypeNode || 
                                  (field.Type is NonNullTypeNode nonNull && nonNull.Type is ListTypeNode);
                
                if (IsBuiltInType(field.Type))
                {
                    InputObjectTypeDefinitionNode inputType;
                    string inputTypeName;
                    
                    if (isListType)
                    {
                        inputTypeName = $"{fieldTypeName}List";
                        if (!inputTypes.ContainsKey(inputTypeName))
                        {
                            inputTypes.Add(inputTypeName, StandardQueryInputs.GetListFilterTypeByScalar(fieldTypeName));
                        }

                        inputType = inputTypes[inputTypeName];
                    }
                    else
                    {
                        if (!inputTypes.ContainsKey(fieldTypeName))
                        {
                            inputTypes.Add(fieldTypeName, StandardQueryInputs.GetFilterTypeByScalar(fieldTypeName));
                        }

                        inputType = inputTypes[fieldTypeName];
                    }

                    inputFields.Add(
                        new(
                            location: null,
                            field.Name,
                            new StringValueNode($"Filter options for {field.Name}"),
                            new NamedTypeNode(inputType.Name.Value),
                            defaultValue: null,
                            new List<DirectiveNode>())
                        );
                }
                else
                {
                    string targetEntityName = RelationshipDirectiveType.Target(field);

                    DirectiveNode? relationshipDirective =
                        RelationshipDirectiveType.GetDirective(field);
                    List<DirectiveNode> directives = new();
                    if (relationshipDirective is not null)
                    {
                        directives.Add(relationshipDirective);
                    }

                    inputFields.Add(
                        new(
                            location: null,
                            field.Name,
                            new StringValueNode($"Filter options for {field.Name}"),
                            new NamedTypeNode(GenerateObjectInputFilterName(targetEntityName)),
                            defaultValue: null,
                            directives: directives));
                }
            }

            return inputFields;
        }

        internal static string GenerateObjectInputOrderByName(string name)
        {
            return $"{name}OrderByInput";
        }

        private static string GenerateObjectInputOrderByName(ObjectTypeDefinitionNode node)
        {
            return GenerateObjectInputOrderByName(node.Name.Value);
        }

        private static string GenerateObjectInputFilterName(ObjectTypeDefinitionNode node)
        {
            return GenerateObjectInputFilterName(node.Name.Value);
        }

        public static string GenerateObjectInputFilterName(string name)
        {
            return $"{name}FilterInput";
        }
    }
}
