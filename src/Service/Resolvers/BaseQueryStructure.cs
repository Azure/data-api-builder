using System.Collections.Generic;
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
        /// The Entity associated with this query.
        /// </summary>
        public string EntityName { get; protected set; }

        /// <summary>
        /// The alias of the main entity to be queried.
        /// </summary>
        public virtual string SourceAlias { get; set; }

        protected ISqlMetadataProvider MetadataProvider { get; }

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

        public GQLFilterParser GraphQLFilterParser { get; protected set; }

        public BaseQueryStructure(
            ISqlMetadataProvider metadataProvider,
            GQLFilterParser gQLFilterParser,
            string entityName,
            IncrementingInteger? counter = null)
        {
            Columns = new();
            Predicates = new();
            Parameters = new();
            Counter = counter ?? new IncrementingInteger();
            MetadataProvider = metadataProvider;
            GraphQLFilterParser = gQLFilterParser;

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
                // This is the cosmos db metadata scenario
                // where the table name is the Source Alias i.e. container alias
                DatabaseObject = new DatabaseTable(string.Empty, SourceAlias);
            }
        }

        /// <summary>
        ///  Add parameter to Parameters and return the name associated with it
        /// </summary>
        /// <param name="value">Value to be assigned to parameter, which can be null for nullable columns.</param>
        public string MakeParamWithValue(object? value)
        {
            string paramName = $"param{Counter.Next()}";
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
