using System.Diagnostics.CodeAnalysis;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedTypes;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder
{
    public static class GraphQLUtils
    {
        public const string DEFAULT_PRIMARY_KEY_NAME = "id";
        public const string DEFAULT_PARTITION_KEY_NAME = "_partitionKeyValue";
        public const string AUTHORIZE_DIRECTIVE = "authorize";
        public const string AUTHORIZE_DIRECTIVE_ARGUMENT_ROLES = "roles";
        public const string OBJECT_TYPE_MUTATION = "mutation";
        public const string OBJECT_TYPE_QUERY = "query";
        public const string SYSTEM_ROLE_ANONYMOUS = "anonymous";

        public static bool IsModelType(ObjectTypeDefinitionNode objectTypeDefinitionNode)
        {
            string modelDirectiveName = ModelDirectiveType.DirectiveName;
            return objectTypeDefinitionNode.Directives.Any(d => d.Name.ToString() == modelDirectiveName);
        }

        public static bool IsModelType(ObjectType objectType)
        {
            string modelDirectiveName = ModelDirectiveType.DirectiveName;
            return objectType.Directives.Any(d => d.Name.ToString() == modelDirectiveName);
        }

        public static bool IsBuiltInType(ITypeNode typeNode)
        {
            HashSet<string> inBuiltTypes = new()
            {
                "ID",
                BYTE_TYPE,
                SHORT_TYPE,
                INT_TYPE,
                LONG_TYPE,
                SINGLE_TYPE,
                FLOAT_TYPE,
                DECIMAL_TYPE,
                STRING_TYPE,
                BOOLEAN_TYPE,
                DATETIME_TYPE,
                BYTEARRAY_TYPE
            };
            string name = typeNode.NamedType().Name.Value;
            return inBuiltTypes.Contains(name);
        }

        /// <summary>
        /// Find all the primary keys for a given object node
        /// using the information available in the directives.
        /// If no directives present, default to a field named "id" as the primary key.
        /// If even that doesn't exist, throw an exception in initialization.
        /// </summary>
        public static List<FieldDefinitionNode> FindPrimaryKeyFields(ObjectTypeDefinitionNode node, DatabaseType databaseType)
        {
            List<FieldDefinitionNode> fieldDefinitionNodes = new();

            if (databaseType == DatabaseType.cosmos)
            {
                fieldDefinitionNodes.Add(
                    new FieldDefinitionNode(
                        location: null,
                        new NameNode(DEFAULT_PRIMARY_KEY_NAME),
                        new StringValueNode("Id value to provide to identify a cosmos db record"),
                        new List<InputValueDefinitionNode>(),
                        new IdType().ToTypeNode(),
                        new List<DirectiveNode>()));

                fieldDefinitionNodes.Add(
                    new FieldDefinitionNode(
                        location: null,
                        new NameNode(DEFAULT_PARTITION_KEY_NAME),
                        new StringValueNode("Partition key value to provide to identify a cosmos db record"),
                        new List<InputValueDefinitionNode>(),
                        new StringType().ToTypeNode(),
                        new List<DirectiveNode>()));
            }
            else
            {
                fieldDefinitionNodes = new(node.Fields.Where(f => f.Directives.Any(d => d.Name.Value == PrimaryKeyDirectiveType.DirectiveName)));

                // By convention we look for a `@primaryKey` directive, if that didn't exist
                // fallback to using an expected field name on the GraphQL object
                if (fieldDefinitionNodes.Count == 0)
                {
                    FieldDefinitionNode? fieldDefinitionNode =
                        node.Fields.FirstOrDefault(f => f.Name.Value == DEFAULT_PRIMARY_KEY_NAME);
                    if (fieldDefinitionNode is not null)
                    {
                        fieldDefinitionNodes.Add(fieldDefinitionNode);
                    }
                    else
                    {
                        // Nothing explicitly defined nor could we find anything using our conventions, fail out
                        throw new DataApiBuilderException(
                               message: "No primary key defined and conventions couldn't locate a fallback",
                               subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization,
                               statusCode: System.Net.HttpStatusCode.ServiceUnavailable);
                    }

                }
            }

            return fieldDefinitionNodes;
        }

        /// <summary>
        /// Checks if a field is auto generated by the database using the directives of the field definition.
        /// </summary>
        /// <param name="field">Field definition to check.</param>
        /// <returns><c>true</c> if it is auto generated, <c>false</c> if it is not.</returns>
        public static bool IsAutoGeneratedField(FieldDefinitionNode field)
        {
            return field.Directives.Any(d => d.Name.Value == AutoGeneratedDirectiveType.DirectiveName);
        }

        /// <summary>
        /// Creates a HotChocolate/GraphQL Authorize directive with a list of roles (if any) provided.
        /// Typically used to lock down Object/Field types to users who are members of the roles allowed.
        /// Will not create such a directive if one of the roles is the system role: anonymous
        /// since for that case, we don't want to lock down the field on which this directive is intended to be
        /// added.
        /// </summary>
        /// <param name="roles">Collection of roles to set on the directive </param>
        /// <param name="authorizeDirective">DirectiveNode set such that: @authorize(roles: ["role1", ..., "roleN"])
        /// where none of role1,..roleN is anonymous. Otherwise, set to null.</param>
        /// <returns>True if set to a new DirectiveNode, false otherwise. </returns>
        public static bool CreateAuthorizationDirectiveIfNecessary(
            IEnumerable<string>? roles,
            out DirectiveNode? authorizeDirective)
        {
            // Any roles passed in will be added to the authorize directive for this field
            // taking the form: @authorize(roles: [“role1”, ..., “roleN”])
            // If the 'anonymous' role is present in the role list, no @authorize directive will be added
            // because HotChocolate requires an authenticated user when the authorize directive is evaluated.
            if (roles is not null &&
                roles.Count() > 0 &&
                !roles.Contains(SYSTEM_ROLE_ANONYMOUS))
            {
                List<IValueNode> roleList = new();
                foreach (string rolename in roles)
                {
                    roleList.Add(new StringValueNode(rolename));
                }

                ListValueNode roleListNode = new(items: roleList);
                authorizeDirective =
                    new(name: AUTHORIZE_DIRECTIVE,
                        new ArgumentNode(name: AUTHORIZE_DIRECTIVE_ARGUMENT_ROLES, roleListNode));
                return true;
            }
            else
            {
                authorizeDirective = null;
                return false;
            }
        }

        /// <summary>
        /// Get the model name (EntityName) defined on the object type definition.
        /// </summary>
        /// <param name="fieldDirectives">Collection of directives on GraphQL field.</param>
        /// <param name="modelName">Value of @model directive, if present.</param>
        /// <returns></returns>
        public static bool TryExtractGraphQLFieldModelName(IDirectiveCollection fieldDirectives, [NotNullWhen(true)] out string? modelName)
        {
            foreach (Directive dir in fieldDirectives)
            {
                if (dir.Name.Value == ModelDirectiveType.DirectiveName)
                {
                    dir.ToObject<ModelDirectiveType>();
                    modelName = dir.GetArgument<string>(ModelDirectiveType.ModelNameArgument).ToString();

                    return modelName is not null;
                }
            }

            modelName = null;
            return false;
        }

        /// <summary>
        /// UnderlyingGraphQLEntityType is the main GraphQL type that is described by
        /// this type. This strips all modifiers, such as List and Non-Null.
        /// So the following GraphQL types would all have the underlyingType Book:
        /// - Book
        /// - [Book]
        /// - Book!
        /// - [Book]!
        /// - [Book!]!
        /// </summary>
        public static ObjectType UnderlyingGraphQLEntityType(IType type)
        {
            if (type is ObjectType underlyingType)
            {
                return underlyingType;
            }

            return UnderlyingGraphQLEntityType(type.InnerType());
        }
    }
}
