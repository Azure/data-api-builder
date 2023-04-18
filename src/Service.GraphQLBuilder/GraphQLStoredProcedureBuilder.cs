// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLUtils;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder
{
    public static class GraphQLStoredProcedureBuilder
    {
        /// <summary>
        /// Adds the stored procedure schema for Query and Mutation types to the provided lists.
        /// </summary>
        /// <param name="name">Name of the stored procedure.</param>
        /// <param name="entity">Entity's runtime config metadata.</param>
        /// <param name="dbObject">Stored procedure database schema metadata.</param>
        /// <param name="definitionNodes">List of definition nodes to be modified.</param>
        /// <param name="fieldDefinitionNodes">List of field definition nodes to be modified.</param>
        /// <param name="rolesAllowed">Role authorization metadata (optional).</param>
        public static void AppendStoredProcedureSchema(
            NameNode name,
            Entity entity,
            DatabaseObject dbObject,
            IList<IDefinitionNode> definitionNodes,
            IList<FieldDefinitionNode> fieldDefinitionNodes,
            IEnumerable<string>? rolesAllowed = null
        )
        {
            StoredProcedureDefinition storedProcedureDefinition = (StoredProcedureDefinition)dbObject.SourceDefinition;
            IEnumerable<KeyValuePair<string, ParameterDefinition>> outputParameters = storedProcedureDefinition.Parameters.Where(kvp => kvp.Value.IsOutput);
            bool hasOutputParameters = outputParameters.Any();
            if (hasOutputParameters)
            {
                definitionNodes.Add(CreateStoredProcedureResultObjectType(name, entity, outputParameters, rolesAllowed));
            }
            fieldDefinitionNodes.Add(CreateStoredProcedureFieldNode(name, entity, dbObject, rolesAllowed, hasOutputParameters));
        }

        /// <summary>
        /// Generates a GraphQL field definition node for a stored procedure.
        /// </summary>
        /// <param name="name">Name of the stored procedure.</param>
        /// <param name="entity">Entity's runtime config metadata.</param>
        /// <param name="dbObject">Stored procedure database schema metadata.</param>
        /// <param name="rolesAllowed">Role authorization metadata (optional).</param>
        /// <param name="generateWithOutputParameters">Flag to indicate if output parameters should be included (optional).</param>
        /// <returns>GraphQL field definition node for the stored procedure.</returns>
        public static FieldDefinitionNode CreateStoredProcedureFieldNode(
            NameNode name,
            Entity entity,
            DatabaseObject dbObject,
            IEnumerable<string>? rolesAllowed = null,
            bool generateWithOutputParameters = false
        )
        {
            List<InputValueDefinitionNode> inputValues = new();
            List<DirectiveNode> fieldDefinitionNodeDirectives = new();

            // StoredProcedureDefinition contains both output result set column and input parameter metadata
            // which are needed because parameter and column names can differ.
            StoredProcedureDefinition spdef = (StoredProcedureDefinition)dbObject.SourceDefinition;

            // Create input value definitions from parameters defined in runtime config. 
            if (entity.Parameters is not null)
            {
                foreach (string param in entity.Parameters.Keys)
                {
                    // Input parameters defined in the runtime config may denote values that may not cast
                    // to the exact value type defined in the database schema.
                    // e.g. Runtime config parameter value set as 1, while database schema denotes value type decimal.
                    // Without database metadata, there is no way to know to cast 1 to a decimal versus an integer.
                    string defaultValueFromConfig = ((JsonElement)entity.Parameters[param]).ToString();
                    Tuple<string, IValueNode> defaultGraphQLValue = ConvertValueToGraphQLType(defaultValueFromConfig, parameterDefinition: spdef.Parameters[param]);

                    inputValues.Add(
                        new(location: null,
                            name: new(param),
                            description: null,
                            type: new NamedTypeNode(defaultGraphQLValue.Item1),
                            defaultValue: defaultGraphQLValue.Item2,
                            directives: new List<DirectiveNode>()
                        )
                    );
                }
            }

            if (CreateAuthorizationDirectiveIfNecessary(
                    rolesAllowed,
                    out DirectiveNode? authorizeDirective
                )
            )
            {
                fieldDefinitionNodeDirectives.Add(authorizeDirective!);
            }

            NonNullTypeNode type = generateWithOutputParameters
                ? new(new NamedTypeNode(GenerateStoredProcedureGraphQLResultObjectName(name.Value, entity)))
                : new(new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(name))));

            return new(location: null,
                name: new(GenerateStoredProcedureGraphQLFieldName(name.Value, entity)),
                description: new($"Execute Stored-Procedure {name.Value} and get results from the database"),
                arguments: inputValues,
                type: type,
                directives: fieldDefinitionNodeDirectives
            );
        }

        /// <summary>
        /// Generates a GraphQL object type definition node for stored procedure results.
        /// </summary>
        /// <param name="name">Name of the stored procedure.</param>
        /// <param name="entity">Entity's runtime config metadata.</param>
        /// <param name="outputParameters">Stored procedure output parameter metadata.</param>
        /// <param name="rolesAllowed">Role authorization metadata (optional).</param>
        /// <returns>GraphQL object type definition node for stored procedure results.</returns>
        public static ObjectTypeDefinitionNode CreateStoredProcedureResultObjectType(
            NameNode name,
            Entity entity,
            IEnumerable<KeyValuePair<string, ParameterDefinition>> outputParameters,
            IEnumerable<string>? rolesAllowed = null
        )
        {
            List<DirectiveNode>? fieldDirectives = new List<DirectiveNode>();
            if (CreateAuthorizationDirectiveIfNecessary(
                    rolesAllowed,
                    out DirectiveNode? authorizeDirective
                )
            )
            {
                fieldDirectives.Add(authorizeDirective!);
            }

            List<FieldDefinitionNode>? executeResultTypeFields = new List<FieldDefinitionNode>() {
                new(location: null,
                    name: new(QueryBuilder.EXECUTE_RESULT_FIELD_NAME),
                    description: new($"The {name} result set from the stored procedure."),
                    arguments: new List<InputValueDefinitionNode>(),
                    type: new NonNullTypeNode(new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(name)))),
                    directives: fieldDirectives
                )
            };

            // If the entity is a Stored Procedure, we need to add any OUTPUT parameter nodes.
            // These are the parameters that are not part of the result set, but are used to return scalar values from the stored procedure.
            foreach ((string parameterName, ParameterDefinition parameter) in outputParameters)
            {
                if (entity.Parameters != null)
                {
                    string defaultValueFromConfig = ((JsonElement)entity.Parameters[parameterName]).ToString();
                    executeResultTypeFields.Add(
                        new(location: null,
                            name: new(parameterName),
                            description: new($"The {parameterName} {parameter.Direction.ToString()} parameter from the stored procedure."),
                            arguments: new List<InputValueDefinitionNode>(),
                            type: new NamedTypeNode(ConvertValueToGraphQLType(defaultValueFromConfig, parameter).Item1),
                            directives: fieldDirectives
                        )
                    );
                }
            }

            string? storedProcedureName = GenerateStoredProcedureGraphQLFieldName(name.Value, entity);
            return new(location: null,
                name: new(GenerateStoredProcedureGraphQLResultObjectName(name.Value, entity)),
                description: new($"Represents the results of the {storedProcedureName} stored procedure execution."),
                directives: new List<DirectiveNode>(),
                interfaces: new List<NamedTypeNode>(),
                fields: executeResultTypeFields
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
            return new(location: null,
                name: new("result"),
                description: new("Contains output of stored-procedure execution"),
                arguments: new List<InputValueDefinitionNode>(),
                type: new StringType().ToTypeNode(),
                directives: new List<DirectiveNode>());
        }
    }
}
