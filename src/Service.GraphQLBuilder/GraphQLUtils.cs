// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;

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
        public const string DB_OPERATION_RESULT_TYPE = "DbOperationResult";
        public const string DB_OPERATION_RESULT_FIELD_NAME = "result";

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
            HashSet<string> builtInTypes = new()
            {
                "ID", // Required for CosmosDB
                UUID_TYPE,
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
                BYTEARRAY_TYPE,
                LOCALTIME_TYPE
            };
            string name = typeNode.NamedType().Name.Value;
            return builtInTypes.Contains(name);
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

            if (databaseType is DatabaseType.CosmosDB_NoSQL)
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
                               statusCode: HttpStatusCode.ServiceUnavailable);
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
        /// <returns>True when name resolution succeeded, false otherwise.</returns>
        public static bool TryExtractGraphQLFieldModelName(IDirectiveCollection fieldDirectives, [NotNullWhen(true)] out string? modelName)
        {
            foreach (Directive dir in fieldDirectives)
            {
                if (dir.Name.Value == ModelDirectiveType.DirectiveName)
                {
                    ModelDirectiveType modelDirectiveType = dir.ToObject<ModelDirectiveType>();

                    if (modelDirectiveType.Name.HasValue)
                    {
                        modelName = dir.GetArgument<string>(ModelDirectiveType.ModelNameArgument).ToString();
                        return modelName is not null;
                    }

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

        /// <summary>
        /// Generates the datasource name from the GraphQL context.
        /// </summary>
        /// <param name="context">Middleware context.</param>
        /// <returns>Datasource name used to execute request.</returns>
        public static string GetDataSourceNameFromGraphQLContext(IPureResolverContext context, RuntimeConfig runtimeConfig)
        {
            string rootNode = context.Selection.Field.Coordinate.TypeName.Value;
            string dataSourceName;

            if (string.Equals(rootNode, "mutation", StringComparison.OrdinalIgnoreCase) || string.Equals(rootNode, "query", StringComparison.OrdinalIgnoreCase))
            {
                // we are at the root query node - need to determine return type and store on context.
                // Output type below would be the graphql object return type - Books,BooksConnectionObject.
                string entityName = GetEntityNameFromContext(context);

                dataSourceName = runtimeConfig.GetDataSourceNameFromEntityName(entityName);

                // Store dataSourceName on context for later use.
                context.ContextData.TryAdd(GenerateDataSourceNameKeyFromPath(context), dataSourceName);
            }
            else
            {
                // Derive node from path - e.g. /books/{id} - node would be books.
                // for this queryNode path we have stored the datasourceName needed to retrieve query and mutation engine of inner objects
                object? obj = context.ContextData[GenerateDataSourceNameKeyFromPath(context)];

                if (obj is null)
                {
                    throw new DataApiBuilderException(
                        message: $"Unable to determine datasource name for operation.",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.GraphQLMapping);
                }

                dataSourceName = obj.ToString()!;
            }

            return dataSourceName;
        }

        /// <summary>
        /// Get entity name from context object.
        /// </summary>
        public static string GetEntityNameFromContext(IPureResolverContext context)
        {
            IOutputType type = context.Selection.Field.Type;
            string graphQLTypeName = type.TypeName();
            string entityName = graphQLTypeName;

            if (graphQLTypeName is DB_OPERATION_RESULT_TYPE)
            {
                // CUD for a mutation whose result set we do not have. Get Entity name mutation field directive.
                if (GraphQLUtils.TryExtractGraphQLFieldModelName(context.Selection.Field.Directives, out string? modelName))
                {
                    entityName = modelName;
                }
            }
            else
            {
                // for rest of scenarios get entity name from output object type.
                ObjectType underlyingFieldType;
                underlyingFieldType = GraphQLUtils.UnderlyingGraphQLEntityType(type);
                // Example: CustomersConnectionObject - for get all scenarios.
                if (QueryBuilder.IsPaginationType(underlyingFieldType))
                {
                    IObjectField subField = GraphQLUtils.UnderlyingGraphQLEntityType(context.Selection.Field.Type).Fields[QueryBuilder.PAGINATION_FIELD_NAME];
                    type = subField.Type;
                    underlyingFieldType = GraphQLUtils.UnderlyingGraphQLEntityType(type);
                    entityName = underlyingFieldType.Name;
                }

                // if name on schema is different from name in config.
                // Due to possibility of rename functionality, entityName on runtimeConfig could be different from exposed schema name.
                if (GraphQLUtils.TryExtractGraphQLFieldModelName(underlyingFieldType.Directives, out string? modelName))
                {
                    entityName = modelName;
                }
            }

            return entityName;
        }

        private static string GenerateDataSourceNameKeyFromPath(IPureResolverContext context)
        {
            return $"{context.Path.ToList()[0]}";
        }
    }
}
