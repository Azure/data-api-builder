// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.CustomScalars;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types.NodaTime;
using NodaTime.Text;

namespace Azure.DataApiBuilder.Service.Services
{
    /// <summary>
    /// This helper class provides the various resolvers and middlewares used
    /// during query execution.
    /// </summary>
    public sealed class ExecutionHelper
    {
        internal readonly IQueryEngineFactory _queryEngineFactory;
        internal readonly IMutationEngineFactory _mutationEngineFactory;
        internal readonly RuntimeConfigProvider _runtimeConfigProvider;

        private const string PURE_RESOLVER_CONTEXT_SUFFIX = "_PURE_RESOLVER_CTX";

        public ExecutionHelper(
            IQueryEngineFactory queryEngineFactory,
            IMutationEngineFactory mutationEngineFactory,
            RuntimeConfigProvider runtimeConfigProvider)
        {
            _queryEngineFactory = queryEngineFactory;
            _mutationEngineFactory = mutationEngineFactory;
            _runtimeConfigProvider = runtimeConfigProvider;
        }

        /// <summary>
        /// Represents the root query resolver and fetches the initial data from the query engine.
        /// </summary>
        /// <param name="context">
        /// The middleware context.
        /// </param>
        public async ValueTask ExecuteQueryAsync(IMiddlewareContext context)
        {
            string dataSourceName = GraphQLUtils.GetDataSourceNameFromGraphQLContext(context, _runtimeConfigProvider.GetConfig());
            DataSource ds = _runtimeConfigProvider.GetConfig().GetDataSourceFromDataSourceName(dataSourceName);
            IQueryEngine queryEngine = _queryEngineFactory.GetQueryEngine(ds.DatabaseType);

            IDictionary<string, object?> parameters = GetParametersFromContext(context);

            if (context.Selection.Type.IsListType())
            {
                Tuple<IEnumerable<JsonDocument>, IMetadata?> result =
                    await queryEngine.ExecuteListAsync(context, parameters, dataSourceName);

                // this will be run after the query / mutation has completed.
                context.RegisterForCleanup(
                    () =>
                    {
                        foreach (JsonDocument document in result.Item1)
                        {
                            document.Dispose();
                        }
                    });

                context.Result = result.Item1.Select(t => t.RootElement).ToArray();
                SetNewMetadata(context, result.Item2);
            }
            else
            {
                Tuple<JsonDocument?, IMetadata?> result =
                    await queryEngine.ExecuteAsync(context, parameters, dataSourceName);
                SetContextResult(context, result.Item1);
                SetNewMetadata(context, result.Item2);
            }
        }

        /// <summary>
        /// Represents the root mutation resolver and invokes the mutation on the query engine.
        /// </summary>
        /// <param name="context">
        /// The middleware context.
        /// </param>
        public async ValueTask ExecuteMutateAsync(IMiddlewareContext context)
        {
            string dataSourceName = GraphQLUtils.GetDataSourceNameFromGraphQLContext(context, _runtimeConfigProvider.GetConfig());
            DataSource ds = _runtimeConfigProvider.GetConfig().GetDataSourceFromDataSourceName(dataSourceName);
            IQueryEngine queryEngine = _queryEngineFactory.GetQueryEngine(ds.DatabaseType);

            IDictionary<string, object?> parameters = GetParametersFromContext(context);

            // Only Stored-Procedure has ListType as returnType for Mutation
            if (context.Selection.Type.IsListType())
            {
                // Both Query and Mutation execute the same SQL statement for Stored Procedure.
                Tuple<IEnumerable<JsonDocument>, IMetadata?> result =
                    await queryEngine.ExecuteListAsync(context, parameters, dataSourceName);

                // this will be run after the query / mutation has completed.
                context.RegisterForCleanup(
                    () =>
                    {
                        foreach (JsonDocument document in result.Item1)
                        {
                            document.Dispose();
                        }
                    });

                context.Result = result.Item1.Select(t => t.RootElement).ToArray();
                SetNewMetadata(context, result.Item2);
            }
            else
            {
                IMutationEngine mutationEngine = _mutationEngineFactory.GetMutationEngine(ds.DatabaseType);
                Tuple<JsonDocument?, IMetadata?> result =
                    await mutationEngine.ExecuteAsync(context, parameters, dataSourceName);
                SetContextResult(context, result.Item1);
                SetNewMetadata(context, result.Item2);
            }
        }

        /// <summary>
        /// Represents a pure resolver for a leaf field.
        /// This resolver extracts the field value from the json object.
        /// </summary>
        /// <param name="context">
        /// The pure resolver context.
        /// </param>
        /// <returns>
        /// Returns the runtime field value.
        /// </returns>
        public static object? ExecuteLeafField(IPureResolverContext context)
        {
            // This means this field is a scalar, so we don't need to do
            // anything for it.
            if (TryGetPropertyFromParent(context, out JsonElement fieldValue) &&
                fieldValue.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            {
                // The selection type can be a wrapper type like NonNullType or ListType.
                // To get the most inner type (aka the named type) we use our named type helper.
                INamedType namedType = context.Selection.Field.Type.NamedType();

                // Each scalar in HotChocolate has a runtime type representation.
                // In order to let scalar values flow through the GraphQL type completion
                // efficiently we want the leaf types to match the runtime type.
                // If that is not the case a value will go through the type converter to try to
                // transform it into the runtime type.
                // We also want to ensure here that we do not unnecessarily convert values to
                // strings and then force the conversion to parse them.
                return namedType switch
                {
                    StringType => fieldValue.GetString(), // spec
                    ByteType => fieldValue.GetByte(),
                    ShortType => fieldValue.GetInt16(),
                    IntType => fieldValue.GetInt32(), // spec
                    LongType => fieldValue.GetInt64(),
                    FloatType => fieldValue.GetDouble(), // spec
                    SingleType => fieldValue.GetSingle(),
                    DecimalType => fieldValue.GetDecimal(),
                    DateTimeType => DateTimeOffset.TryParse(fieldValue.GetString()!, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal, out DateTimeOffset date) ? date : null, // for DW when datetime is null it will be in "" (double quotes) due to stringagg parsing and hence we need to ensure parsing is correct.
                    DateType => DateTimeOffset.TryParse(fieldValue.GetString()!, out DateTimeOffset date) ? date : null,
                    LocalTimeType => fieldValue.GetString()!.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : LocalTimePattern.ExtendedIso.Parse(fieldValue.GetString()!).Value,
                    ByteArrayType => fieldValue.GetBytesFromBase64(),
                    BooleanType => fieldValue.GetBoolean(), // spec
                    UrlType => new Uri(fieldValue.GetString()!),
                    UuidType => fieldValue.GetGuid(),
                    TimeSpanType => TimeSpan.Parse(fieldValue.GetString()!),
                    AnyType => fieldValue.ToString(),
                    _ => fieldValue.GetString()
                };
            }

            return null;
        }

        /// <summary>
        /// Represents a pure resolver for an object field.
        /// This resolver extracts another json object from the parent json property.
        /// </summary>
        /// <param name="context">
        /// The pure resolver context.
        /// </param>
        /// <returns>
        /// Returns a new json object.
        /// </returns>
        public object? ExecuteObjectField(IPureResolverContext context)
        {
            string dataSourceName = GraphQLUtils.GetDataSourceNameFromGraphQLContext(context, _runtimeConfigProvider.GetConfig());
            DataSource ds = _runtimeConfigProvider.GetConfig().GetDataSourceFromDataSourceName(dataSourceName);
            IQueryEngine queryEngine = _queryEngineFactory.GetQueryEngine(ds.DatabaseType);

            if (TryGetPropertyFromParent(context, out JsonElement objectValue) &&
                objectValue.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                IMetadata metadata = GetMetadataObjectField(context);
                objectValue = queryEngine.ResolveObject(objectValue, context.Selection.Field, ref metadata);

                // Since the query engine could null the object out we need to check again
                // if it's null.
                if (objectValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    return null;
                }

                SetNewMetadataChildren(context, metadata);
                return objectValue;
            }

            return null;
        }

        /// <summary>
        /// The ListField pure resolver is executed when processing "list" fields.
        /// For example, when executing the query { myEntity { items { entityField1 } } }
        /// this pure resolver will be executed when processing the field "items" because
        /// it will contain the "list" of results.
        /// </summary>
        /// <param name="context">PureResolver context provided by HC middleware.</param>
        /// <returns>The resolved list, a JSON array, returned as type 'object?'.</returns>
        /// <remarks>Return type is 'object?' instead of a 'List of JsonElements' because when this function returns JsonElement,
        /// the HC12 engine doesn't know how to handle the JsonElement and results in requests failing at runtime.</remarks>
        public object? ExecuteListField(IPureResolverContext context)
        {
            string dataSourceName = GraphQLUtils.GetDataSourceNameFromGraphQLContext(context, _runtimeConfigProvider.GetConfig());
            DataSource ds = _runtimeConfigProvider.GetConfig().GetDataSourceFromDataSourceName(dataSourceName);
            IQueryEngine queryEngine = _queryEngineFactory.GetQueryEngine(ds.DatabaseType);

            if (TryGetPropertyFromParent(context, out JsonElement listValue) &&
                listValue.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                IMetadata? metadata = GetMetadata(context);
                object result = queryEngine.ResolveList(listValue, context.Selection.Field, ref metadata);
                SetNewMetadataChildren(context, metadata);
                return result;
            }

            return null;
        }

        /// <summary>
        /// Set the context's result and dispose properly. If result is not null
        /// clone root and dispose, otherwise set to null.
        /// </summary>
        /// <param name="context">Context to store result.</param>
        /// <param name="result">Result to store in context.</param>
        private static void SetContextResult(IMiddlewareContext context, JsonDocument? result)
        {
            if (result is not null)
            {
                context.RegisterForCleanup(() => result.Dispose());
                // The disposal could occur before we were finished using the value from the jsondocument,
                // thus needing to ensure copying the root element. Hence, we clone the root element.
                context.Result = result.RootElement.Clone();
            }
            else
            {
                context.Result = null;
            }
        }

        private static bool TryGetPropertyFromParent(
            IPureResolverContext context,
            out JsonElement propertyValue)
        {
            JsonElement parent = context.Parent<JsonElement>();

            if (parent.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                propertyValue = default;
                return false;
            }
            else if (context.Path is NamePathSegment namePathSegment && namePathSegment.Parent is NamePathSegment parentSegment && parentSegment.Name.Value == QueryBuilder.GROUP_BY_AGGREGATE_FIELD_NAME &&
                parentSegment.Parent?.Parent is NamePathSegment grandParentSegment && grandParentSegment.Name.Value.StartsWith(QueryBuilder.GROUP_BY_FIELD_NAME, StringComparison.OrdinalIgnoreCase))
            {
                // verify that current selection is part of a groupby query and within that an aggregation and then get the key which would be the operation name or its alias (eg: max, max_price etc)
                string propertyName = namePathSegment.Name.Value;
                return parent.TryGetProperty(propertyName, out propertyValue);
            }

            return parent.TryGetProperty(context.Selection.Field.Name.Value, out propertyValue);
        }

        /// <summary>
        /// Extracts the value from an IValueNode. That includes extracting the value of the variable
        /// if the IValueNode is a variable and extracting the correct type from the IValueNode
        /// </summary>
        /// <param name="value">the IValueNode from which to extract the value</param>
        /// <param name="argumentSchema">describes the schema of the argument that the IValueNode represents</param>
        /// <param name="variables">the request context variable values needed to resolve value nodes represented as variables</param>
        public static object? ExtractValueFromIValueNode(
            IValueNode value,
            IInputField argumentSchema,
            IVariableValueCollection variables)
        {
            // extract value from the variable if the IValueNode is a variable
            if (value.Kind == SyntaxKind.Variable)
            {
                string variableName = ((VariableNode)value).Name.Value;
                IValueNode? variableValue = variables.GetVariable<IValueNode>(variableName);

                if (variableValue is null)
                {
                    return null;
                }

                return ExtractValueFromIValueNode(variableValue, argumentSchema, variables);
            }

            if (value is NullValueNode)
            {
                return null;
            }

            return argumentSchema.Type.TypeName().Value switch
            {
                SupportedHotChocolateTypes.BYTE_TYPE => ((IntValueNode)value).ToByte(),
                SupportedHotChocolateTypes.SHORT_TYPE => ((IntValueNode)value).ToInt16(),
                SupportedHotChocolateTypes.INT_TYPE => ((IntValueNode)value).ToInt32(),
                SupportedHotChocolateTypes.LONG_TYPE => ((IntValueNode)value).ToInt64(),
                SupportedHotChocolateTypes.SINGLE_TYPE => value is IntValueNode intValueNode ? intValueNode.ToSingle() : ((FloatValueNode)value).ToSingle(),
                SupportedHotChocolateTypes.FLOAT_TYPE => value is IntValueNode intValueNode ? intValueNode.ToDouble() : ((FloatValueNode)value).ToDouble(),
                SupportedHotChocolateTypes.DECIMAL_TYPE => value is IntValueNode intValueNode ? intValueNode.ToDecimal() : ((FloatValueNode)value).ToDecimal(),
                SupportedHotChocolateTypes.UUID_TYPE => Guid.TryParse(value.Value!.ToString(), out Guid guidValue) ? guidValue : value.Value,
                _ => value.Value
            };
        }

        /// <summary>
        /// First: Creates parameters using the GraphQL schema's ObjectTypeDefinition metadata
        /// and metadata from the request's (query) field.
        /// Then: Creates parameters from schema argument fields when they have default values.
        /// Lastly: Gets the user provided argument values from the query to either:
        /// 1. Overwrite the parameter value if it exists in the collectedParameters dictionary
        /// or
        /// 2. Adds the parameter/parameter value to the dictionary.
        /// </summary>
        /// <returns>
        /// Dictionary of parameters
        /// Key: (string) argument field name
        /// Value: (object) argument value
        /// </returns>
        public static IDictionary<string, object?> GetParametersFromSchemaAndQueryFields(
            IObjectField schema,
            FieldNode query,
            IVariableValueCollection variables)
        {
            IDictionary<string, object?> collectedParameters = new Dictionary<string, object?>();

            // Fill the parameters dictionary with the default argument values
            IFieldCollection<IInputField> schemaArguments = schema.Arguments;

            // Example 'argumentSchemas' IInputField objects of type 'HotChocolate.Types.Argument':
            // These are all default arguments defined in the schema for queries.
            // {first:int}
            // {after:String}
            // {filter:entityFilterInput}
            // {orderBy:entityOrderByInput}
            // The values in schemaArguments will have default values when the backing
            // entity is a stored procedure with runtime config defined default parameter values.
            foreach (IInputField argument in schemaArguments)
            {
                if (argument.DefaultValue != null)
                {
                    collectedParameters.Add(
                        argument.Name.Value,
                        ExtractValueFromIValueNode(
                            value: argument.DefaultValue,
                            argumentSchema: argument,
                            variables: variables));
                }
            }

            // Overwrite the default values with the passed in arguments
            // Example: { myEntity(first: $first, orderBy: {entityField: ASC) { items { entityField } } }
            // User supplied $first filter variable overwrites the default value of 'first'.
            // User supplied 'orderBy' filter overwrites the default value of 'orderBy'.
            IReadOnlyList<ArgumentNode> passedArguments = query.Arguments;

            foreach (ArgumentNode argument in passedArguments)
            {
                string argumentName = argument.Name.Value;
                IInputField argumentSchema = schemaArguments[argumentName];

                object? nodeValue = ExtractValueFromIValueNode(
                            value: argument.Value,
                            argumentSchema: argumentSchema,
                            variables: variables);

                if (!collectedParameters.TryAdd(argumentName, nodeValue))
                {
                    collectedParameters[argumentName] = nodeValue;
                }
            }

            return collectedParameters;
        }

        /// <summary>
        /// InnerMostType is innermost type of the passed Graph QL type.
        /// This strips all modifiers, such as List and Non-Null.
        /// So the following GraphQL types would all have the underlyingType Book:
        /// - Book
        /// - [Book]
        /// - Book!
        /// - [Book]!
        /// - [Book!]!
        /// </summary>
        internal static IType InnerMostType(IType type)
        {
            if (type.ToString() == type.InnerType().ToString())
            {
                return type;
            }

            return InnerMostType(type.InnerType());
        }

        public static InputObjectType InputObjectTypeFromIInputField(IInputField field)
        {
            return (InputObjectType)(InnerMostType(field.Type));
        }

        /// <summary>
        /// Creates a dictionary of parameters and associated values from
        /// the GraphQL request's MiddlewareContext from arguments provided
        /// in the request. e.g. first, after, filter, orderBy, and stored procedure
        /// parameters.
        /// </summary>
        /// <param name="context">GraphQL HotChocolate MiddlewareContext</param>
        /// <returns>Dictionary of parameters and their values.</returns>
        private static IDictionary<string, object?> GetParametersFromContext(
            IMiddlewareContext context)
        {
            return GetParametersFromSchemaAndQueryFields(
                context.Selection.Field,
                context.Selection.SyntaxNode,
                context.Variables);
        }

        /// <summary>
        /// Get metadata from HotChocolate's GraphQL request MiddlewareContext.
        /// The metadata key is the root field name + _PURE_RESOLVER_CTX + :: + PathDepth.
        /// CosmosDB does not utilize pagination metadata. So this function will return null
        /// when executing GraphQl queries against CosmosDB.
        /// </summary>
        private static IMetadata? GetMetadata(IPureResolverContext context)
        {
            if (context.Selection.ResponseName == QueryBuilder.PAGINATION_FIELD_NAME && context.Path.Parent is not null)
            {
                // entering this block means that:
                // context.Selection.ResponseName: items
                // context.Path: /entityA/items (Depth: 1)
                // context.Path.Parent: /entityA (Depth: 0)
                // The parent's metadata will be stored in ContextData with a depth of context.Path minus 1. -> "::0"
                // The resolved metadata key is entityA_PURE_RESOLVER_CTX and is appended with "::0"
                // Another case would be:
                // context.Path: /books/items[0]/authors/items
                // context.Path.Parent: /books/items[0]/authors
                // The nuance here is that HC counts the depth when the path is expanded as
                // /books/items/items[idx]/authors -> Depth: 3 (0-indexed) which maps to the
                // pagination metadata for the "authors/items" subquery.
                string paginationObjectParentName = GetMetadataKey(context.Path) + "::" + context.Path.Parent.Depth;
                return (IMetadata?)context.ContextData[paginationObjectParentName];
            }

            // This section would be reached when processing a Cosmos query of the form:
            // { planet_by_pk (id: $id, _partitionKeyValue: $partitionKeyValue) { tags } }
            // where nested entities like the entity 'tags' are not nested within an "items" field
            // like for SQL databases.
            string metadataKey = GetMetadataKey(context.Path) + "::" + context.Path.Depth;

            if (context.ContextData.TryGetValue(key: metadataKey, out object? paginationMetadata) && paginationMetadata is not null)
            {
                return (IMetadata)paginationMetadata;
            }
            else
            {
                // CosmosDB database type does not utilize pagination metadata.
                return PaginationMetadata.MakeEmptyPaginationMetadata();
            }
        }

        /// <summary>
        /// Get the pagination metadata object for the field represented by the
        /// pure resolver context.
        /// e.g. when Context.Path is "/books/items[0]/authors", this function gets
        /// the pagination metadata for authors, which is stored in the global middleware
        /// context under key: "books_PURE_RESOLVER_CTX::1", where "books" is the parent object
        /// and depth of "1" implicitly represents the path "/books/items". When "/books/items"
        /// is processed by the pure resolver, the available pagination metadata maps to the object
        /// type that enumerated in "items"
        /// </summary>
        /// <param name="context">Pure resolver context</param>
        /// <returns>Pagination metadata</returns>
        private static IMetadata GetMetadataObjectField(IPureResolverContext context)
        {
            // Depth Levels:  / 0   /  1  /   2    /   3
            // Example Path: /books/items/items[0]/publishers
            // Depth of 1 should have key in context.ContextData
            // Depth of 2 will not have context.ContextData entry because non-Indexed path element is the path that is cached.
            // PaginationMetadata for items will be consistent across each subitem. So we can use the same metadata for each subitem.
            // An indexer path segment is a segment that looks like -> items[n]
            if (context.Path.Parent is IndexerPathSegment)
            {
                // When context.Path is "/books/items[0]/authors"
                // Parent -> "/books/items[0]"
                // Parent -> "/books/items" -> Depth of this path is used to create the key to get
                // paginationmetadata from context.ContextData
                // The PaginationMetadata fetched has subquery metadata for "authors" from path "/books/items/authors"
                string objectParentName = GetMetadataKey(context.Path) + "::" + context.Path.Parent!.Parent!.Depth;
                return (IMetadata)context.ContextData[objectParentName]!;
            }
            else if (context.Path.Parent is not null && ((NamePathSegment)context.Path.Parent).Name != PURE_RESOLVER_CONTEXT_SUFFIX)
            {
                // This check handles when the current selection is a relationship field because in that case,
                // there will be no context data entry.
                // e.g. metadata for index 4 will not exist. only 3. 
                // Depth: /  0   / 1  /   2    /   3      /   4
                // Path:  /books/items/items[0]/publishers/books
                string objectParentName = GetMetadataKey(context.Path) + "::" + context.Path.Parent!.Depth;
                return (IMetadata)context.ContextData[objectParentName]!;
            }

            string metadataKey = GetMetadataKey(context.Path) + "::" + context.Path.Depth;
            return (IMetadata)context.ContextData[metadataKey]!;
        }

        private static string GetMetadataKey(HotChocolate.Path path)
        {
            HotChocolate.Path currentPath = path;

            if (currentPath.Parent is RootPathSegment or null)
            {
                // current: "/entity/items -> "items"
                return ((NamePathSegment)currentPath).Name + PURE_RESOLVER_CONTEXT_SUFFIX;
            }

            // If execution reaches this point, the state of currentPath looks something
            // like the following where there exists a Parent path element:
            // "/entity/items -> current.Parent: "entity"
            return GetMetadataKey(path: currentPath.Parent);
        }

        /// <summary>
        /// Resolves the name of the root object of a selection set to
        /// use as the beginning of a key used to index pagination metadata in the
        /// global HC middleware context.
        /// </summary>
        /// <param name="rootSelection">Root object field of query.</param>
        /// <returns>"rootObjectName_PURE_RESOLVER_CTX"</returns>
        private static string GetMetadataKey(IFieldSelection rootSelection)
        {
            return rootSelection.ResponseName + PURE_RESOLVER_CONTEXT_SUFFIX;
        }

        /// <summary>
        /// Persist new metadata with a key denoting the depth of the current path.
        /// The pagination metadata persisted here correlates to the top-level object type
        /// denoted in the request.
        /// e.g. books_PURE_RESOLVER_CTX::0 for:
        /// context.Path -> /books depth(0)
        /// context.Selection -> books { items {id, title}}
        /// </summary>
        private static void SetNewMetadata(IPureResolverContext context, IMetadata? metadata)
        {
            string metadataKey = GetMetadataKey(context.Selection) + "::" + context.Path.Depth;
            context.ContextData.Add(metadataKey, metadata);
        }

        /// <summary>
        /// Stores the pagination metadata in the global context.ContextData accessible to
        /// all pure resolvers for query fields referencing nested entities.
        /// </summary>
        /// <param name="context">Pure resolver context</param>
        /// <param name="metadata">Pagination metadata</param>
        private static void SetNewMetadataChildren(IPureResolverContext context, IMetadata? metadata)
        {
            // When context.Path is /entity/items the metadata key is "entity"
            // The context key will use the depth of "items" so that the provided
            // pagination metadata (which holds the subquery metadata for "/entity/items/nestedEntity")
            // can be stored for future access when the "/entity/items/nestedEntity" pure resolver executes.
            // When context.Path takes the form: "/entity/items[index]/nestedEntity" HC counts the depth as
            // if the path took the form: "/entity/items/items[index]/nestedEntity" -> Depth of "nestedEntity"
            // is 3 because depth is 0-indexed.
            string contextKey = GetMetadataKey(context.Path) + "::" + context.Path.Depth;

            // It's okay to overwrite the context when we are visiting a different item in items e.g. books/items/items[1]/publishers since
            // context for books/items/items[0]/publishers processing is done and that context isn't needed anymore.
            if (!context.ContextData.TryAdd(contextKey, metadata))
            {
                context.ContextData[contextKey] = metadata;
            }
        }
    }
}
