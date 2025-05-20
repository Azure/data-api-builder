// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.CustomScalars;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Sql;
using HotChocolate.Language;
using HotChocolate.Types;
using NodaTime.Text;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLUtils;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder
{
    public static class GraphQLStoredProcedureBuilder
    {
        /// <summary>
        /// Helper function to create StoredProcedure Schema for GraphQL.
        /// It uses the parameters to build the arguments and returns a list
        /// of the StoredProcedure GraphQL object.
        /// </summary>
        /// <param name="name">Name used for InputValueDefinition name.</param>
        /// <param name="entity">Entity's runtime config metadata.</param>
        /// <param name="dbObject">Stored procedure database schema metadata.</param>
        /// <param name="rolesAllowed">Role authorization metadata.</param>
        /// <returns>Stored procedure mutation field.</returns>
        public static FieldDefinitionNode GenerateStoredProcedureSchema(
            NameNode name,
            Entity entity,
            DatabaseObject dbObject,
            IEnumerable<string>? rolesAllowed = null
            )
        {
            List<InputValueDefinitionNode> inputValues = new();
            List<DirectiveNode> fieldDefinitionNodeDirectives = new();

            // StoredProcedureDefinition contains both output result set column and input parameter metadata
            // which are needed because parameter and column names can differ.
            StoredProcedureDefinition spdef = (StoredProcedureDefinition)dbObject.SourceDefinition;

            // Create input value definitions from parameters defined in stored proc.
            if (spdef is not null)
            {
                foreach ((string param, ParameterDefinition definition) in spdef.Parameters)
                {
                    // Input parameters defined in the runtime config may denote values that may not cast
                    // to the exact value type defined in the database schema.
                    // e.g. Runtime config parameter value set as 1, while database schema denotes value type decimal.
                    // Without database metadata, there is no way to know to cast 1 to a decimal versus an integer.

                    IValueNode? defaultValueNode = null;
                    if (entity.Source.Parameters is not null && entity.Source.Parameters.TryGetValue(param, out object? value))
                    {
                        Tuple<string, IValueNode> defaultGraphQLValue = ConvertValueToGraphQLType(value.ToString()!, parameterDefinition: spdef.Parameters[param]);
                        defaultValueNode = defaultGraphQLValue.Item2;
                    }

                    inputValues.Add(
                        new(
                            location: null,
                            name: new(param),
                            description: new StringValueNode($"parameters for {name.Value} stored-procedure"),
                            type: new NamedTypeNode(SchemaConverter.GetGraphQLTypeFromSystemType(type: definition.SystemType)),
                            defaultValue: defaultValueNode,
                            directives: new List<DirectiveNode>())
                        );
                }
            }

            if (CreateAuthorizationDirectiveIfNecessary(
                    rolesAllowed,
                    out DirectiveNode? authorizeDirective))
            {
                fieldDefinitionNodeDirectives.Add(authorizeDirective!);
            }

            return new(
                location: null,
                new NameNode(GenerateStoredProcedureGraphQLFieldName(name.Value, entity)),
                new StringValueNode($"Execute Stored-Procedure {name.Value} and get results from the database"),
                inputValues,
                new NonNullTypeNode(new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(name)))),
                fieldDefinitionNodeDirectives
            );
        }

        /// <summary>
        /// Takes the result from DB as JsonDocument and formats it in a way that can be filtered by column
        /// name. It parses the Json document into a list of Dictionary with key as result_column_name
        /// with it's corresponding value.
        /// returns an empty list in case of no result
        /// or stored-procedure is trying to read from DB without READ permission.
        /// </summary>
        public static List<JsonDocument> FormatStoredProcedureResultAsJsonList(JsonDocument? jsonDocument)
        {
            if (jsonDocument is null)
            {
                return new List<JsonDocument>();
            }

            List<JsonDocument> resultJson = new();
            List<Dictionary<string, object>> resultList = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonDocument.RootElement.ToString())!;
            foreach (Dictionary<string, object> result in resultList)
            {
                resultJson.Add(JsonDocument.Parse(JsonSerializer.Serialize(result)));
            }

            return resultJson;
        }

        /// <summary>
        /// Create and return a default GraphQL result field for a stored-procedure which doesn't
        /// define a result set and doesn't return any rows.
        /// </summary>
        public static FieldDefinitionNode GetDefaultResultFieldForStoredProcedure()
        {
            return new(
                location: null,
                name: new("result"),
                description: new StringValueNode("Contains output of stored-procedure execution"),
                arguments: new List<InputValueDefinitionNode>(),
                type: new StringType().ToTypeNode(),
                directives: new List<DirectiveNode>());
        }

        /// <summary>
        /// Translates a JSON string or number value defined as a stored procedure's default value
        /// within the runtime configuration to a GraphQL {Type}ValueNode which represents
        /// the associated GraphQL type. The target value type is referenced from the passed in parameterDefinition which
        /// holds database schema metadata.
        /// </summary>
        /// <param name="defaultValueFromConfig">String representation of default value defined in runtime config.</param>
        /// <param name="parameterDefinition">Database schema metadata for stored procedure parameter which include value and value type.</param>
        /// <returns>Tuple where first item is the string representation of a GraphQLType (e.g. "Byte", "Int", "Decimal")
        /// and the second item is the GraphQL {type}ValueNode.</returns>
        /// <exception cref="DataApiBuilderException">Raised when parameter casting fails due to unsupported type.</exception>
        private static Tuple<string, IValueNode> ConvertValueToGraphQLType(string defaultValueFromConfig, ParameterDefinition parameterDefinition)
        {
            string paramValueType = SchemaConverter.GetGraphQLTypeFromSystemType(type: parameterDefinition.SystemType);

            try
            {
                Tuple<string, IValueNode> valueNode = paramValueType switch
                {
                    UUID_TYPE => new(UUID_TYPE, new UuidType().ParseValue(Guid.Parse(defaultValueFromConfig))),
                    BYTE_TYPE => new(BYTE_TYPE, new IntValueNode(byte.Parse(defaultValueFromConfig))),
                    SHORT_TYPE => new(SHORT_TYPE, new IntValueNode(short.Parse(defaultValueFromConfig))),
                    INT_TYPE => new(INT_TYPE, new IntValueNode(int.Parse(defaultValueFromConfig))),
                    LONG_TYPE => new(LONG_TYPE, new IntValueNode(long.Parse(defaultValueFromConfig))),
                    SINGLE_TYPE => new(SINGLE_TYPE, new SingleType().ParseValue(float.Parse(defaultValueFromConfig))),
                    FLOAT_TYPE => new(FLOAT_TYPE, new FloatValueNode(double.Parse(defaultValueFromConfig))),
                    DECIMAL_TYPE => new(DECIMAL_TYPE, new FloatValueNode(decimal.Parse(defaultValueFromConfig))),
                    STRING_TYPE => new(STRING_TYPE, new StringValueNode(defaultValueFromConfig)),
                    BOOLEAN_TYPE => new(BOOLEAN_TYPE, new BooleanValueNode(bool.Parse(defaultValueFromConfig))),
                    DATETIME_TYPE => new(DATETIME_TYPE, new DateTimeType().ParseResult(
                        DateTime.Parse(defaultValueFromConfig, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal))),
                    BYTEARRAY_TYPE => new(BYTEARRAY_TYPE, new ByteArrayType().ParseValue(Convert.FromBase64String(defaultValueFromConfig))),
                    LOCALTIME_TYPE => new(LOCALTIME_TYPE, new HotChocolate.Types.NodaTime.LocalTimeType().ParseResult(LocalTimePattern.ExtendedIso.Parse(defaultValueFromConfig).Value)),
                    _ => throw new NotSupportedException(message: $"The {defaultValueFromConfig} parameter's value type [{paramValueType}] is not supported.")
                };

                return valueNode;
            }
            catch (Exception error)
            {
                throw new DataApiBuilderException(
                        message: $"The parameter value {defaultValueFromConfig} provided in configuration cannot be converted to the type {paramValueType}",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.GraphQLMapping,
                        innerException: error);
            }
        }
    }
}
