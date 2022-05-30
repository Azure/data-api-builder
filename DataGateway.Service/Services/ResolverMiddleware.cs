using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.GraphQLBuilder.CustomScalars;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// The resolver middleware that is used by the schema executor to resolve
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

        public async Task InvokeAsync(IMiddlewareContext context)
        {
            JsonElement jsonElement;

            if (context.Selection.Field.Coordinate.TypeName.Value == "Mutation")
            {
                IDictionary<string, object?> parameters = GetParametersFromContext(context);

                Tuple<JsonDocument, IMetadata> result = await _mutationEngine.ExecuteAsync(context, parameters);
                context.Result = result.Item1;
                SetNewMetadata(context, result.Item2);
            }
            else if (context.Selection.Field.Coordinate.TypeName.Value == "Query")
            {
                IDictionary<string, object?> parameters = GetParametersFromContext(context);

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
        /// e.g. "1" despite being a valid byte value is parsed properly by HotChocolate so we preparse it
        /// to an actual byte value then feed the result to HotChocolate
        /// </remarks>
        private static object PreParseLeaf(IMiddlewareContext context, string leafJson)
        {
            return context.Selection.Field.Type switch
            {
                ByteType => byte.Parse(leafJson),
                SingleType => Single.Parse(leafJson),
                DateTimeType => DateTime.Parse(leafJson),
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
        /// Extract the value from a IValueNode
        /// Note that if the node type is Variable, the parameter variables needs to be specified
        /// as well in order to extract the value.
        /// </summary>
        public static object? ArgumentValue(IValueNode value, IVariableValueCollection? variables = null)
        {
            return value.Kind switch
            {
                SyntaxKind.IntValue => ((IntValueNode)value).ToInt32(),
                SyntaxKind.Variable => variables?.GetVariable<object>(((VariableNode)value).Value),
                _ => value.Value
            };
        }

        /// <summary>
        /// Extract parameters from the schema and the actual instance (query) of the field
        /// Extracts defualt parameter values from the schema or null if no default
        /// Overrides default values with actual values of parameters provided
        /// </summary>
        public static IDictionary<string, object?> GetParametersFromSchemaAndQueryFields(IObjectField schema, FieldNode query, IVariableValueCollection variables)
        {
            IDictionary<string, object?> parameters = new Dictionary<string, object?>();

            // Fill the parameters dictionary with the default argument values
            IFieldCollection<IInputField> availableArguments = schema.Arguments;
            foreach (IInputField argument in availableArguments)
            {
                if (argument.DefaultValue != null)
                {
                    parameters.Add(argument.Name.Value, ArgumentValue(argument.DefaultValue, variables));
                }
            }

            // Overwrite the default values with the passed in arguments
            IReadOnlyList<ArgumentNode> passedArguments = query.Arguments;
            foreach (ArgumentNode argument in passedArguments)
            {
                if (parameters.ContainsKey(argument.Name.Value))
                {
                    parameters[argument.Name.Value] = ArgumentValue(argument.Value, variables);
                }
                else
                {
                    parameters.Add(argument.Name.Value, ArgumentValue(argument.Value, variables));
                }
            }

            return parameters;
        }

        protected static IDictionary<string, object?> GetParametersFromContext(IMiddlewareContext context)
        {
            return GetParametersFromSchemaAndQueryFields(context.Selection.Field, context.Selection.SyntaxNode, context.Variables);
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
