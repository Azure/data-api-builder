using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.GraphQLBuilder.CustomScalars;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Resolvers;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedTypes;

namespace Azure.DataApiBuilder.Service.Services
{
    /// <summary>
    /// The field resolver middleware that is used by the schema executor to resolve
    /// the queries and mutations
    /// </summary>
    public class ResolverMiddleware
    {
        private static readonly string _contextMetadata = "metadata";
        internal readonly FieldDelegate _next;
        internal readonly IQueryEngine _queryEngine;
        internal readonly IMutationEngine _mutationEngine;

        public ResolverMiddleware(FieldDelegate next,
            IQueryEngine queryEngine,
            IMutationEngine mutationEngine)
        {
            _next = next;
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
        }

        /// <summary>
        /// HotChocolate invokes this method when this ResolverMiddleware is utilized
        /// in the request pipeline.
        /// From this method, the Query and Mutation engines are executed, and the execution
        /// results saved in the IMiddlewareContext's result property.
        /// </summary>
        /// <seealso cref="https://chillicream.com/docs/hotchocolate/execution-engine/field-middleware"/>
        /// <param name="context">HotChocolate middleware context containing request metadata.</param>
        /// <returns>Does not explicitly return data.</returns>
        public async Task InvokeAsync(IMiddlewareContext context)
        {
            JsonElement jsonElement;
            if (context.ContextData.TryGetValue("HttpContext", out object? value))
            {
                if (value is not null)
                {
                    HttpContext httpContext = (HttpContext)value;
                    StringValues clientRoleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
                    context.ContextData.TryAdd(key: AuthorizationResolver.CLIENT_ROLE_HEADER, value: clientRoleHeader);
                }
            }

            if (context.Selection.Field.Coordinate.TypeName.Value == "Mutation")
            {
                IDictionary<string, object?> parameters = GetArgumentsWithVariableValues(context);

                Tuple<JsonDocument, IMetadata> result = await _mutationEngine.ExecuteAsync(context, parameters);
                context.Result = result.Item1;
                SetNewMetadata(context, result.Item2);
            }
            else if (context.Selection.Field.Coordinate.TypeName.Value == "Query")
            {
                IDictionary<string, object?> parameters = GetArgumentsWithVariableValues(context);

                if (context.Selection.Type.IsListType())
                {
                    Tuple<IEnumerable<JsonDocument>, IMetadata> result = await _queryEngine.ExecuteListAsync(context, parameters);
                    context.Result = result.Item1;
                    SetNewMetadata(context, result.Item2);
                }
                else
                {
                    Tuple<JsonDocument, IMetadata> result = await _queryEngine.ExecuteAsync(context, parameters);
                    context.Result = result.Item1;
                    SetNewMetadata(context, result.Item2);
                }
            }
            else if (context.Selection.Field.Type.IsLeafType())
            {
                // This means this field is a scalar, so we don't need to do
                // anything for it.
                if (TryGetPropertyFromParent(context, out jsonElement))
                {
                    context.Result = RepresentsNullValue(jsonElement) ? null : PreParseLeaf(context, jsonElement.ToString());
                }
            }
            else if (IsInnerObject(context))
            {
                // This means it's a field that has another custom type as its
                // type, so there is a full JSON object inside this key. For
                // example such a JSON object could have been created by a
                // One-To-Many join.
                if (TryGetPropertyFromParent(context, out jsonElement))
                {
                    IMetadata metadata = GetMetadata(context);
                    context.Result = _queryEngine.ResolveInnerObject(jsonElement, context.Selection.Field, ref metadata);
                    SetNewMetadata(context, metadata);
                }
            }
            else if (context.Selection.Type.IsListType())
            {
                // This means the field is a list and HotChocolate requires
                // that to be returned as a List of JsonDocuments. For example
                // such a JSON list could have been created by a One-To-Many
                // join.
                if (TryGetPropertyFromParent(context, out jsonElement))
                {
                    IMetadata metadata = GetMetadata(context);
                    context.Result = _queryEngine.ResolveListType(jsonElement, context.Selection.Field, ref metadata);
                    SetNewMetadata(context, metadata);
                }
            }

            await _next(context);
        }

        /// <summary>
        /// Preparse a string extracted from the json result representing a leaf.
        /// This is helpful in cases when HotChocolate's internal resolvers cannot appropriately
        /// parse the result so we preparse the result so it can be appropriately handled by HotChocolate
        /// later
        /// </summary>
        /// <remarks>
        /// e.g. "1" despite being a valid byte value is parsed improperly by HotChocolate so we preparse it
        /// to an actual byte value then feed the result to HotChocolate
        /// </remarks>
        private static object PreParseLeaf(IMiddlewareContext context, string leafJson)
        {
            return context.Selection.Field.Type switch
            {
                ByteType => byte.Parse(leafJson),
                SingleType => Single.Parse(leafJson),
                DateTimeType => DateTimeOffset.Parse(leafJson),
                ByteArrayType => Convert.FromBase64String(leafJson),
                _ => leafJson
            };
        }

        public static bool RepresentsNullValue(JsonElement element)
        {
            return string.IsNullOrEmpty(element.ToString()) && element.GetRawText() == "null";
        }

        protected static bool TryGetPropertyFromParent(IMiddlewareContext context, out JsonElement jsonElement)
        {
            JsonDocument result = context.Parent<JsonDocument>();
            if (result == null)
            {
                jsonElement = default;
                return false;
            }

            return result.RootElement.TryGetProperty(context.Selection.Field.Name.Value, out jsonElement);
        }

        protected static bool IsInnerObject(IMiddlewareContext context)
        {
            return context.Selection.Field.Type.IsObjectType() && context.Parent<JsonDocument>() != default;
        }

        /// <summary>
        /// Extracts the value from an IValueNode. That includes extracting the value of the variable
        /// if the IValueNode is a variable and extracting the correct type from the IValueNode
        /// </summary>
        /// <param name="value">the IValueNode from which to extract the value</param>
        /// <param name="argumentSchema">describes the schema of the argument that the IValueNode represents</param>
        /// <param name="variables">the request context variable values needed to resolve value nodes represented as variables</param>
        public static object? ExtractValueFromIValueNode(IValueNode value, IInputField argumentSchema, IVariableValueCollection variables)
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

            if (value is ObjectValueNode obj)
            {
                return obj.Fields.ToDictionary(field => field.Name.Value, field => ExtractValueFromIValueNode(field.Value, argumentSchema, variables));
            }

            return argumentSchema.Type.TypeName().Value switch
            {
                BYTE_TYPE => ((IntValueNode)value).ToByte(),
                SHORT_TYPE => ((IntValueNode)value).ToInt16(),
                INT_TYPE => ((IntValueNode)value).ToInt32(),
                LONG_TYPE => ((IntValueNode)value).ToInt64(),
                SINGLE_TYPE => ((FloatValueNode)value).ToSingle(),
                FLOAT_TYPE => ((FloatValueNode)value).ToDouble(),
                DECIMAL_TYPE => ((FloatValueNode)value).ToDecimal(),
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
        public static IDictionary<string, object?> GetParametersFromSchemaAndQueryFields(IObjectField schema, FieldNode query, IVariableValueCollection variables)
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
            return (InputObjectType)InnerMostType(field.Type);
        }

        private static IDictionary<string, object?> GetArgumentsWithVariableValues(IMiddlewareContext context)
        {
            IArgumentMap args = ((ISelection)context.Selection).Arguments;

            if (!args.TryCoerceArguments(context, out IReadOnlyDictionary<NameString, ArgumentValue>? coercedArgs))
            {
                throw new InvalidOperationException("Unable to parse the variables into arguments for the current request");
            }

            return coercedArgs.ToDictionary(item => item.Key.Value, item => ExtractRawValue(item.Value.ValueLiteral));
        }

        private static object? ExtractRawValue(IValueNode? value)
        {
            return value switch
            {
                null => null,
                NullValueNode => null,
                IntValueNode i => IntValueNodeToNumber(i),
                StringValueNode s => s.Value,
                BooleanValueNode b => b.Value,
                FloatValueNode f => f.Value,
                EnumValueNode e => e.Value,
                ListValueNode list => list.Items.Select(ExtractRawValue).ToList(),
                ObjectValueNode obj => obj.Fields.ToDictionary(field => field.Name.Value, field => ExtractRawValue(field.Value)),
                _ => value
            };
        }

        private static object IntValueNodeToNumber(IntValueNode i)
        {
            static bool TryGet(Func<IntValueNode, object> f, IntValueNode i, out object? o)
            {
                try
                {
                    o = f(i);
                    return true;
                }
                catch (InvalidFormatException)
                {
                    o = null;
                    return false;
                }
            }

            bool success =
               TryGet(i => i.ToInt32(), i, out object? ret) ||
               TryGet(i => i.ToInt64(), i, out ret) ||
               TryGet(i => i.ToInt16(), i, out ret) ||
               TryGet(i => i.ToByte(), i, out ret) ||
               TryGet(i => i.ToDecimal(), i, out ret) ||
               TryGet(i => i.ToDouble(), i, out ret) ||
               TryGet(i => i.ToSingle(), i, out ret);

            ret ??= i.Value;

            return ret;
        }

        /// <summary>
        /// Get metadata from context
        /// </summary>
        private static IMetadata GetMetadata(IMiddlewareContext context)
        {
            return (IMetadata)context.ScopedContextData[_contextMetadata]!;
        }

        /// <summary>
        /// Set new metadata and reset the depth that the metadata has persisted
        /// </summary>
        private static void SetNewMetadata(IMiddlewareContext context, IMetadata metadata)
        {
            context.ScopedContextData = context.ScopedContextData.SetItem(_contextMetadata, metadata);
        }
    }
}
