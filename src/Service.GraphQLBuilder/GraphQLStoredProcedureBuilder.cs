// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;
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
                        new(
                            location: null,
                            name: new(param),
                            description: new StringValueNode($"parameters for {name.Value} stored-procedure"),
                            type: new NamedTypeNode(defaultGraphQLValue.Item1),
                            defaultValue: defaultGraphQLValue.Item2,
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
    }
}
