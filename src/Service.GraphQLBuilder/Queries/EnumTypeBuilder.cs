// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Service.GraphQLBuilder.Sql;
using HotChocolate.Language;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Queries
{
    public static class EnumTypeBuilder
    {
        private static readonly string _numericAggregateFieldsSuffix = "NumericAggregateFields";
        private static readonly string _scalarFieldsSuffix = "ScalarFields";

        /// <summary>
        /// Returns the numeric aggregate fields enum name for the given entity name.
        /// </summary>
        /// <param name="entityName">input entity name.</param>
        /// <returns>{entityName}NumericAggregateFields</returns>
        public static string GenerateNumericAggregateFieldsEnumName(string entityName)
        {
            return $"{entityName}{_numericAggregateFieldsSuffix}";
        }

        /// <summary>
        /// Returns the scalar fields enum name for the given entity name.
        /// </summary>
        /// <param name="entityName">input entity name.</param>
        /// <returns>{entityName}ScalarFields</returns>
        public static string GenerateScalarFieldsEnumName(string entityName)
        {
            return $"{entityName}{_scalarFieldsSuffix}";
        }

        /// <summary>
        /// Generates an enum type for fields of the given object type based on a field selector.
        /// </summary>
        /// <param name="node">The object type definition node.</param>
        /// <param name="enumTypes">Dictionary to store the generated enum types.</param>
        /// <param name="fieldSelector">Function to select which fields to include in the enum.</param>
        /// <param name="enumNameSuffix">Suffix to append to the enum type name.</param>
        /// <param name="description">Description for the generated enum type.</param>
        /// <returns>True if the enum type was generated, false otherwise.</returns>
        public static bool TryGenerateEnum(
            ObjectTypeDefinitionNode node,
            IDictionary<string, EnumTypeDefinitionNode> enumTypes,
            Func<FieldDefinitionNode, bool> fieldSelector,
            string enumNameSuffix,
            string description)
        {
            List<FieldDefinitionNode> selectedFields = node.Fields.Where(fieldSelector).ToList();

            if (selectedFields.Any())
            {
                string enumTypeName = $"{node.Name.Value}{enumNameSuffix}";

                EnumTypeDefinitionNode enumType = new(
                    location: null,
                    name: new NameNode(enumTypeName),
                    description: new StringValueNode(string.Format(description, node.Name.Value)),
                    directives: new List<DirectiveNode>(),
                    values: selectedFields
                        .Select(f => new EnumValueDefinitionNode(
                            location: null,
                            name: f.Name,
                            description: null,
                            directives: new List<DirectiveNode>()
                        )).ToList()
                );

                enumTypes.Add(enumTypeName, enumType);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Generates an enum type containing numeric fields for aggregation.
        /// </summary>
        public static bool GenerateAggregationNumericEnumForObjectType(
            ObjectTypeDefinitionNode node,
            IDictionary<string, EnumTypeDefinitionNode> enumTypes)
        {
            return TryGenerateEnum(
                node,
                enumTypes,
                f => SchemaConverter.IsNumericField(f.Type),
                _numericAggregateFieldsSuffix,
                "Fields available for aggregation in {0}"
            );
        }

        /// <summary>
        /// Generates an enum type containing all scalar fields.
        /// </summary>
        public static bool GenerateScalarFieldsEnumForObjectType(
            ObjectTypeDefinitionNode node,
            IDictionary<string, EnumTypeDefinitionNode> enumTypes)
        {
            return TryGenerateEnum(
                node,
                enumTypes,
                f => GraphQLUtils.IsBuiltInType(f.Type),
                _scalarFieldsSuffix,
                "Scalar fields available in {0}"
            );
        }
    }
}
