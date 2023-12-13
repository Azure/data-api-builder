// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.CustomScalars;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Resolvers;

namespace Azure.DataApiBuilder.Service.Services
{
    /// <summary>
    /// This helper class provides the various resolvers and middlewares used
    /// during query execution.
    /// </summary>
    internal sealed class ExecutionHelper
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
        /// This resolver extracts the field value form the json object.
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
                    DateTimeType => DateTimeOffset.Parse(fieldValue.GetString()!),
                    DateType => DateTimeOffset.Parse(fieldValue.GetString()!),
                    ByteArrayType => fieldValue.GetBytesFromBase64(),
                    BooleanType => fieldValue.GetBoolean(), // spec
                    UrlType => new Uri(fieldValue.GetString()!),
                    UuidType => fieldValue.GetGuid(),
                    TimeSpanType => TimeSpan.Parse(fieldValue.GetString()!),
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
                IMetadata metadata = GetMetadata(context);
                objectValue = queryEngine.ResolveObject(objectValue, context.Selection.Field, ref metadata);

                // Since the query engine could null the object out we need to check again
                // if it's null.
                if (objectValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    return null;
                }

                SetNewMetadata(context, metadata);
                return objectValue;
            }

            return null;
        }

        public object? ExecuteListField(IPureResolverContext context)
        {
            string dataSourceName = GraphQLUtils.GetDataSourceNameFromGraphQLContext(context, _runtimeConfigProvider.GetConfig());
            DataSource ds = _runtimeConfigProvider.GetConfig().GetDataSourceFromDataSourceName(dataSourceName);
            IQueryEngine queryEngine = _queryEngineFactory.GetQueryEngine(ds.DatabaseType);

            if (TryGetPropertyFromParent(context, out JsonElement listValue) &&
                listValue.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                IMetadata metadata = GetMetadata(context);
                IReadOnlyList<JsonElement> result = queryEngine.ResolveList(listValue, context.Selection.Field, ref metadata);
                SetNewMetadata(context, metadata);
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
                context.Result = result.RootElement;
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
                SupportedHotChocolateTypes.SINGLE_TYPE => ((FloatValueNode)value).ToSingle(),
                SupportedHotChocolateTypes.FLOAT_TYPE => ((FloatValueNode)value).ToDouble(),
                SupportedHotChocolateTypes.DECIMAL_TYPE => ((FloatValueNode)value).ToDecimal(),
                _ => value.Value
            };
        }

        /// <summary>
        /// Extract parameters from the schema and the actual instance (query) of the field
        /// Extracts default parameter values from the schema or null if no default
        /// Overrides default values with actual values of parameters provided
        /// Key: (string) argument field name
        /// Value: (object) argument value
        /// </summary>
        public static IDictionary<string, object?> GetParametersFromSchemaAndQueryFields(
            IObjectField schema,
            FieldNode query,
            IVariableValueCollection variables)
        {
            IDictionary<string, object?> parameters = new Dictionary<string, object?>();

            // Fill the parameters dictionary with the default argument values
            IFieldCollection<IInputField> argumentSchemas = schema.Arguments;

            foreach (IInputField argument in argumentSchemas)
            {
                if (argument.DefaultValue != null)
                {
                    parameters.Add(
                        argument.Name.Value,
                        ExtractValueFromIValueNode(
                            value: argument.DefaultValue,
                            argumentSchema: argument,
                            variables: variables));
                }
            }

            // Overwrite the default values with the passed in arguments
            IReadOnlyList<ArgumentNode> passedArguments = query.Arguments;

            foreach (ArgumentNode argument in passedArguments)
            {
                string argumentName = argument.Name.Value;
                IInputField argumentSchema = argumentSchemas[argumentName];

                if (parameters.ContainsKey(argumentName))
                {
                    parameters[argumentName] =
                        ExtractValueFromIValueNode(
                            value: argument.Value,
                            argumentSchema: argumentSchema,
                            variables: variables);
                }
                else
                {
                    parameters.Add(
                        argumentName,
                        ExtractValueFromIValueNode(
                            value: argument.Value,
                            argumentSchema: argumentSchema,
                            variables: variables));
                }
            }

            return parameters;
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

        private static IDictionary<string, object?> GetParametersFromContext(
            IMiddlewareContext context)
        {
            return GetParametersFromSchemaAndQueryFields(
                context.Selection.Field,
                context.Selection.SyntaxNode,
                context.Variables);
        }

        /// <summary>
        /// Get metadata from context
        /// </summary>
        private static IMetadata GetMetadata(IPureResolverContext context)
        {
            // The pure resolver context has no access to the scoped context data,
            // I will change that for version 14. The following code is a workaround
            // and stores the metadata per root field on the global context.
            string metadataKey = GetMetadataKey(context.Path) + PURE_RESOLVER_CONTEXT_SUFFIX;
            return (IMetadata)context.ContextData[metadataKey]!;
        }

        private static string GetMetadataKey(HotChocolate.Path path)
        {
            HotChocolate.Path current = path;

            if (current.Parent is RootPathSegment or null)
            {
                return ((NamePathSegment)current).Name;
            }

            while (current.Parent is not null)
            {
                current = current.Parent;

                if (current.Parent is RootPathSegment or null)
                {
                    return ((NamePathSegment)current).Name;
                }
            }

            throw new InvalidOperationException("The path is not rooted.");
        }

        /// <summary>
        /// Set new metadata and reset the depth that the metadata has persisted
        /// </summary>
        private static void SetNewMetadata(IPureResolverContext context, IMetadata? metadata)
        {
            string metadataKey = GetMetadataKey(context.Path) + PURE_RESOLVER_CONTEXT_SUFFIX;

            if (!context.ContextData.TryAdd(metadataKey, metadata))
            {
                context.ContextData[metadataKey] = metadata;
            }
        }
    }
}
