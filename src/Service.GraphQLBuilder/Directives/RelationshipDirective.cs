using Azure.DataApiBuilder.Config;
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
                               .Location(DirectiveLocation.FieldDefinition);

            descriptor.Argument("target")
                  .Type<StringType>()
                  .Description("The name of the entity the relationship targets");

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

            return Enum.Parse<Cardinality>((string)arg.Value.Value!);
        }
    }
}
