// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using HotChocolate.Language;
using HotChocolate.Types;
using DirectiveLocation = HotChocolate.Types.DirectiveLocation;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Directives
{
    public class RelationshipDirectiveType : DirectiveType
    {
        public static string DirectiveName { get; } = "relationship";

        protected override void Configure(IDirectiveTypeDescriptor descriptor)
        {
            descriptor.Name(DirectiveName)
                               .Description("A directive to indicate the relationship between two tables")
                               .Location(DirectiveLocation.FieldDefinition | DirectiveLocation.InputFieldDefinition);

            descriptor.Argument("target")
                  .Type<StringType>()
                  .Description("The name of the GraphQL type the relationship targets");

            descriptor.Argument("cardinality")
                  .Type<StringType>()
                  .Description("The relationship cardinality");
        }

        /// <summary>
        /// Gets the target object type name for a field with a relationship directive.
        /// </summary>
        /// <param name="field">The field that has a relationship directive defined.</param>
        /// <returns>The name of the GraphQL object type that the relationship targets. If no relationship is defined, the object type of the field is returned.</returns>
        public static string Target(FieldDefinitionNode field)
        {
            DirectiveNode? directive = field.Directives.FirstOrDefault(d => d.Name.Value == DirectiveName);

            if (directive == null)
            {
                return field.Type.NamedType().Name.Value;
            }

            ArgumentNode arg = directive.Arguments.First(a => a.Name.Value == "target");

            return (string)arg.Value.Value!;
        }

        /// <summary>
        /// Gets the target object type name for an input infield with a relationship directive.
        /// </summary>
        /// <param name="inputField">The input field that is expected to have a relationship directive defined on it.</param>
        /// <returns>The name of the target object if the relationship is found, null otherwise.</returns>
        public static string? GetTarget(IInputValueDefinition inputField)
        {
            Directive? directive = (Directive?)inputField.Directives.FirstOrDefault(DirectiveName);
            return directive?.GetArgumentValue<string>("target");
        }

        /// <summary>
        /// Gets the cardinality of the relationship.
        /// </summary>
        /// <param name="field">The field that has a relationship directive defined.</param>
        /// <returns>Relationship cardinality</returns>
        /// <exception cref="ArgumentException">Thrown if the field does not have a defined relationship.</exception>
        public static Cardinality Cardinality(FieldDefinitionNode field)
        {
            DirectiveNode? directive = field.Directives.FirstOrDefault(d => d.Name.Value == DirectiveName);

            if (directive == null)
            {
                throw new ArgumentException("The specified field does not have a relationship directive defined.");
            }

            ArgumentNode arg = directive.Arguments.First(a => a.Name.Value == "cardinality");

            return EnumExtensions.Deserialize<Cardinality>((string)arg.Value.Value!);
        }

        /// <summary>
        /// Retrieves the relationship directive defined on the given field definition node.
        /// </summary>
        public static DirectiveNode? GetDirective(FieldDefinitionNode field)
        {
            DirectiveNode? directive = field.Directives.FirstOrDefault(d => d.Name.Value == DirectiveName);
            return directive;
        }
    }
}
