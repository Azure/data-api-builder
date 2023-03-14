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
        public static FieldDefinitionNode GenerateStoredProcedureSchema(
            NameNode name,
            Entity entity,
            IEnumerable<string>? rolesAllowed = null)
        {
            List<InputValueDefinitionNode> inputValues = new();
            List<DirectiveNode> fieldDefinitionNodeDirectives = new();

            if (entity.Parameters is not null)
            {
                foreach (string param in entity.Parameters.Keys)
                {
                    Tuple<string, IValueNode> defaultGraphQLValue = GetGraphQLTypeAndNodeTypeFromStringValue(entity.Parameters[param].ToString()!);
                    inputValues.Add(
                        new(
                            location: null,
                            new(param),
                            new StringValueNode($"parameters for {name.Value} stored-procedure"),
                            new NamedTypeNode(defaultGraphQLValue.Item1),
                            defaultValue: defaultGraphQLValue.Item2,
                            new List<DirectiveNode>())
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
        /// Helper method to create a default result field for stored-procedure which does not
        /// return any row.
        /// </summary>
        public static FieldDefinitionNode GetDefaultResultFieldForStoredProcedure()
        {
            return new(
                location: null,
                new("result"),
                description: new StringValueNode("Contains output of stored-procedure execution"),
                new List<InputValueDefinitionNode>(),
                new StringType().ToTypeNode(),
                new List<DirectiveNode>());
        }
    }
}
