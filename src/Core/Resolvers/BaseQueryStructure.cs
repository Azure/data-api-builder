// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    public class BaseQueryStructure
    {
        /// <summary>
        /// The Entity name associated with this query as appears in the config file.
        /// </summary>
        public string EntityName { get; set; }

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
        public Dictionary<string, DbConnectionParam> Parameters { get; set; }

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

            // Default the alias to the empty string since this base constructor
            // is called for requests other than Find operations. We only use
            // SourceAlias for Find, so we leave empty here and then populate
            // in the Find specific contractor.
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
        /// <param name="paramName"> The name of the parameter - backing column name for table/views or parameter name for stored procedures.</param>
        public virtual string MakeDbConnectionParam(object? value, string? paramName = null)
        {
            string encodedParamName = GetEncodedParamName(Counter.Next());
            if (!string.IsNullOrEmpty(paramName))
            {
                Parameters.Add(encodedParamName,
                    new(value,
                        dbType: GetUnderlyingSourceDefinition().GetDbTypeForParam(paramName),
                        sqlDbType: GetUnderlyingSourceDefinition().GetSqlDbTypeForParam(paramName)));
            }
            else
            {
                Parameters.Add(encodedParamName, new(value));
            }

            return encodedParamName;
        }

        /// <summary>
        /// Helper method to create encoded parameter name.
        /// </summary>
        /// <param name="counterValue">The counter value used as a suffix in the encoded parameter name.</param>
        /// <returns>Encoded parameter name.</returns>
        public static string GetEncodedParamName(ulong counterValue)
        {
            return $"{PARAM_NAME_PREFIX}param{counterValue}";
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
