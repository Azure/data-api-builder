// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Language;
using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    public class BaseQueryStructure
    {
        /// <summary>
        /// The Entity name associated with this query as appears in the config file.
        /// </summary>
        public string EntityName { get; protected set; }

        /// <summary>
        /// The alias of the entity as used in the generated query.
        /// </summary>
        public virtual string SourceAlias { get; set; }

        /// <summary>
        /// The metadata provider of the respective database.
        /// </summary>
        public ISqlMetadataProvider MetadataProvider { get; }

        /// <summary>
        /// The DatabaseObject associated with the entity, represents the
        /// databse object to be queried.
        /// </summary>
        public DatabaseObject DatabaseObject { get; protected set; } = null!;

        /// <summary>
        /// The columns which the query selects
        /// </summary>
        public List<LabelledColumn> Columns { get; }

        /// <summary>
        /// Counter.Next() can be used to get a unique integer within this
        /// query, which can be used to create unique aliases, parameters or
        /// other identifiers.
        /// </summary>
        public IncrementingInteger Counter { get; }

        /// <summary>
        /// Parameters values required to execute the query.
        /// </summary>
        public Dictionary<string, object?> Parameters { get; set; }

        /// <summary>
        /// Predicates that should filter the result set of the query.
        /// </summary>
        public List<Predicate> Predicates { get; }

        /// <summary>
        /// Used for parsing graphql filter arguments.
        /// </summary>
        public GQLFilterParser GraphQLFilterParser { get; protected set; }

        /// <summary>
        /// Authorization Resolver used within SqlQueryStructure to get and apply
        /// authorization policies to requests.
        /// </summary>
        public IAuthorizationResolver AuthorizationResolver { get; }

        public const string PARAM_NAME_PREFIX = "@";

        public BaseQueryStructure(
            ISqlMetadataProvider metadataProvider,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            List<Predicate>? predicates = null,
            string entityName = "",
            IncrementingInteger? counter = null)
        {
            Columns = new();
            Parameters = new();
            Predicates = predicates ?? new();
            Counter = counter ?? new IncrementingInteger();
            MetadataProvider = metadataProvider;
            GraphQLFilterParser = gQLFilterParser;
            AuthorizationResolver = authorizationResolver;

            // Default the alias to the empty string since this base construtor
            // is called for requests other than Find operations. We only use
            // SourceAlias for Find, so we leave empty here and then populate
            // in the Find specific contructor.
            SourceAlias = string.Empty;

            if (!string.IsNullOrEmpty(entityName))
            {
                EntityName = entityName;
                DatabaseObject = MetadataProvider.EntityToDatabaseObject[entityName];
            }
            else
            {
                EntityName = string.Empty;
                DatabaseObject = new DatabaseTable(schemaName: string.Empty, tableName: string.Empty);
            }
        }

        /// <summary>
        ///  Add parameter to Parameters and return the name associated with it
        /// </summary>
        /// <param name="value">Value to be assigned to parameter, which can be null for nullable columns.</param>
        public string MakeParamWithValue(object? value)
        {
            string paramName = $"{PARAM_NAME_PREFIX}param{Counter.Next()}";
            // Don't add the OUTPUT keyword to the name.
            Parameters.Add(paramName, value);
            return value is SqlExecuteParameter sqlExecuteParameter && sqlExecuteParameter.IsOutput
                ? paramName + " OUTPUT"
                : paramName;
        }

        /// <summary>
        /// Creates a unique table alias.
        /// </summary>
        public string CreateTableAlias()
        {
            return $"table{Counter.Next()}";
        }

        /// <summary>
        /// Returns the SourceDefinitionDefinition for the entity(table/view) of this query.
        /// </summary>
        public SourceDefinition GetUnderlyingSourceDefinition()
        {
            return MetadataProvider.GetSourceDefinition(EntityName);
        }

        /// <summary>
        /// Finds a FieldNode with a specified name in the SelectionSet of the given rootNode.
        /// </summary>
        /// <param name="rootNode">The FieldNode containing the SelectionSet to search within.</param>
        /// <param name="fieldName">The name of the FieldNode to search for.</param>
        /// <returns>The FieldNode with the specified name if found, otherwise null.</returns>
        internal static FieldNode? FindFieldNodeByName(FieldNode rootNode, string fieldName)
        {
            return rootNode.SelectionSet!.Selections
                .OfType<FieldNode>()
                .FirstOrDefault(field => field.Name.Value == fieldName);
        }

        /// <summary>
        /// Retrieves a specified field from the underlying GraphQL entity type of the given connectionSchemaField.
        /// </summary>
        /// <param name="connectionSchemaField">The IObjectField representing the *Connection schema field.</param>
        /// <param name="fieldName">The name of the field to be retrieved from the underlying GraphQL entity type.</param>
        /// <returns>The specified IObjectField from the underlying GraphQL entity type of the connectionSchemaField.</returns>
        internal static IObjectField GetFieldFromUnderlyingEntityType(IObjectField connectionSchemaField, string fieldName)
        {
            return GraphQLUtils.UnderlyingGraphQLEntityType(connectionSchemaField.Type).Fields[fieldName];
        }
    }
}
