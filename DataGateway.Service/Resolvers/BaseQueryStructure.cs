using System.Collections.Generic;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using Azure.DataGateway.Service.Models;
using HotChocolate.Language;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.Resolvers
{
    public class BaseQueryStructure
    {
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

        public BaseQueryStructure(
            IncrementingInteger? counter = null)
        {
            Columns = new();
            Predicates = new();
            Parameters = new();
            Counter = counter ?? new IncrementingInteger();
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

                if (fieldName == "items")
                {
                    itemsField = field;
                    break;
                }
            }

            return itemsField;
        }

        /// <summary>
        /// UnderlyingGraphQLEntityType is the type main GraphQL type that is described by
        /// this type. This strips all modifiers, such as List and Non-Null.
        /// So the following GraphQL types would all have the underlyingType Book:
        /// - Book
        /// - [Book]
        /// - Book!
        /// - [Book]!
        /// - [Book!]!
        /// </summary>
        internal static ObjectType UnderlyingGraphQLEntityType(IType type)
        {
            if (type is ObjectType underlyingType)
            {
                return underlyingType;
            }

            return UnderlyingGraphQLEntityType(type.InnerType());
        }

        /// <summary>
        /// Extracts the *Connection.items schema field from the *Connection schema field
        /// </summary>
        internal static IObjectField ExtractItemsSchemaField(IObjectField connectionSchemaField)
        {
            return UnderlyingGraphQLEntityType(connectionSchemaField.Type).Fields[QueryBuilder.PAGINATION_FIELD_NAME];
        }
    }
}
