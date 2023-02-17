// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
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
            Parameters.Add(paramName, value);
            return paramName;
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
        /// Extracts the *Connection.items query field from the *Connection query field
        /// </summary>
        /// <returns> The query field or null if **Conneciton.items is not requested in the query</returns>
        internal static FieldNode? ExtractItemsQueryField(FieldNode connectionQueryField)
        {
            FieldNode? itemsField = null;
            foreach (ISelectionNode node in connectionQueryField.SelectionSet!.Selections)
            {
                FieldNode field = (FieldNode)node;
                string fieldName = field.Name.Value;

                if (fieldName == QueryBuilder.PAGINATION_FIELD_NAME)
                {
                    itemsField = field;
                    break;
                }
            }

            return itemsField;
        }

        /// <summary>
        /// Extracts the *Connection.items schema field from the *Connection schema field
        /// </summary>
        internal static IObjectField ExtractItemsSchemaField(IObjectField connectionSchemaField)
        {
            return GraphQLUtils.UnderlyingGraphQLEntityType(connectionSchemaField.Type).Fields[QueryBuilder.PAGINATION_FIELD_NAME];
        }
    }
}
