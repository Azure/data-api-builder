// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.GraphQLBuilder.CustomScalars;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Resolvers;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedTypes;

namespace Azure.DataApiBuilder.Service.Services
{
    /// <summary>
    /// The field resolver middleware that is used by the schema executor to resolve
    /// the queries and mutations
    /// </summary>
    internal sealed class ExecutionHelper
    {
        internal readonly IQueryEngine _queryEngine;
        internal readonly IMutationEngine _mutationEngine;

        public ExecutionHelper(
            IQueryEngine queryEngine,
            IMutationEngine mutationEngine)
        {
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
        }

        public async ValueTask ExecuteQueryAsync(IMiddlewareContext context)
        {
            IDictionary<string, object?> parameters = GetParametersFromContext(context);

            if (context.Selection.Type.IsListType())
            {
                Tuple<IEnumerable<JsonDocument>, IMetadata?> result =
                    await _queryEngine.ExecuteListAsync(context, parameters);
                
                // this will be run after the query / mutation has completed. 
                context.RegisterForCleanup(() =>
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
                    await _queryEngine.ExecuteAsync(context, parameters);
                SetContextResult(context, result.Item1);
                SetNewMetadata(context, result.Item2);
            }
        }
        
        public async ValueTask ExecuteMutateAsync(IMiddlewareContext context)
        {
            if (context.Selection.Field.Coordinate.TypeName.Value == "Mutation")
            {
                IDictionary<string, object?> parameters = GetParametersFromContext(context);

                // Only Stored-Procedure has ListType as returnType for Mutation
                if (context.Selection.Type.IsListType())
                {
                    // Both Query and Mutation execute the same SQL statement for Stored Procedure.
                    Tuple<IEnumerable<JsonDocument>, IMetadata?> result =
                        await _queryEngine.ExecuteListAsync(context, parameters);
                    
                    // this will be run after the query / mutation has completed. 
                    context.RegisterForCleanup(() =>
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
                        await _mutationEngine.ExecuteAsync(context, parameters);
                    SetContextResult(context, result.Item1);
                    SetNewMetadata(context, result.Item2);
                }
            }
        }
        
        public static object? ExecuteLeafField(IPureResolverContext context)
        {
            // This means this field is a scalar, so we don't need to do
            // anything for it.
            if (TryGetPropertyFromParent(context, out JsonElement jsonElement))
            {
                return jsonElement.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) 
                    ? PreParseLeaf(context, jsonElement.ToString()) 
                    : null;
            }

            return null;
        }

        public object? ExecuteObjectField(IPureResolverContext context)
        {
            // This means it's a field that has another custom type as its
            // type, so there is a full JSON object inside this key. For
            // example such a JSON object could have been created by a
            // One-To-Many join.
            if (TryGetPropertyFromParent(context, out JsonElement propertyValue))
            {
                IMetadata metadata = GetMetadata(context);
                propertyValue = _queryEngine.ResolveInnerObject(
                    propertyValue,
                    context.Selection.Field,
                    ref metadata);

                if (propertyValue.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
                {
                    return propertyValue;
                }
            }
            
            return null;
        }
        
        public object? ExecuteListField(IPureResolverContext context)
        {
            // This means the field is a list and HotChocolate requires
            // that to be returned as a List of JsonDocuments. For example
            // such a JSON list could have been created by a One-To-Many
            // join.
            if (TryGetPropertyFromParent(context, out JsonElement propertyValue))
            {
                IMetadata metadata = GetMetadata(context);
                object? result = _queryEngine.ResolveListType(
                    propertyValue,
                    context.Selection.Field,
                    ref metadata);
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
        private static object PreParseLeaf(IPureResolverContext context, string leafJson)
        {
            IType leafType = context.Selection.Field.Type is NonNullType
                ? context.Selection.Field.Type.NullableType()
                : context.Selection.Field.Type;
            return leafType switch
            {
                ByteType => byte.Parse(leafJson),
                SingleType => Single.Parse(leafJson),
                DateTimeType => DateTimeOffset.Parse(leafJson),
                ByteArrayType => Convert.FromBase64String(leafJson),
                _ => leafJson
            };
        }

        protected static bool TryGetPropertyFromParent(
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

        protected static bool IsInnerObject(IMiddlewareContext context)
        {
            return context.Selection.Field.Type.IsObjectType() &&
                context.Parent<JsonElement?>() is not null;
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

        protected static IDictionary<string, object?> GetParametersFromContext(
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
            // The pure resolver context has not access to the scoped context data,
            // I will change that for version 14. The following code is a workaround
            // and store the metadata per root field on the global context.
            return (IMetadata)context.ContextData[GetMetadataKey(context.Path)]!;
        }

        private static string GetMetadataKey(Path path)
        {
            Path current = path;
            
            if(current.Parent is RootPathSegment)
            {
                return ((NamePathSegment)current).Name;
            }
            
            while(current.Parent is not null)
            {
                current = current.Parent;
                
                if(current.Parent is RootPathSegment)
                {
                    return ((NamePathSegment)current).Name;
                }   
            }
            
            throw new InvalidOperationException("The path is not rooted.");
        }

        private static string GetMetadataKey(IFieldSelection rootSelection)
        {
            return rootSelection.ResponseName;
        }

        /// <summary>
        /// Set new metadata and reset the depth that the metadata has persisted
        /// </summary>
        private static void SetNewMetadata(IPureResolverContext context, IMetadata? metadata)
        {
            context.ContextData.Add(GetMetadataKey(context.Selection), metadata);
        }
    }
}