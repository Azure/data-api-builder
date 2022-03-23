using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.Resolvers
{

    /// <summary>
    /// Holds shared properties and methods among
    /// Sql*QueryStructure classes
    /// </summary>
    public abstract class BaseSqlQueryStructure
    {
        /// <summary>
        /// The name of the main table to be queried.
        /// </summary>
        public string TableName { get; protected set; }
        /// <summary>
        /// The alias of the main table to be queried.
        /// </summary>
        public string TableAlias { get; protected set; }
        /// <summary>
        /// The columns which the query selects
        /// </summary>
        public List<LabelledColumn> Columns { get; }
        /// <summary>
        /// Predicates that should filter the result set of the query.
        /// </summary>
        public List<Predicate> Predicates { get; }
        /// <summary>
        /// FilterPredicates is a string that represents the filter portion of our query
        /// in the WHERE Clause. This is generated specifically from the $filter portion
        /// of the query string.
        /// </summary>
        public string? FilterPredicates { get; set; }
        /// <summary>
        /// Parameters values required to execute the query.
        /// </summary>
        public Dictionary<string, object?> Parameters { get; set; }
        /// <summary>
        /// Counter.Next() can be used to get a unique integer within this
        /// query, which can be used to create unique aliases, parameters or
        /// other identifiers.
        /// </summary>
        public IncrementingInteger Counter { get; }

        protected IMetadataStoreProvider MetadataStoreProvider { get; }

        public BaseSqlQueryStructure(IMetadataStoreProvider metadataStore,
            IncrementingInteger? counter = null, string tableName = "")
        {
            Columns = new();
            Predicates = new();
            Parameters = new();
            MetadataStoreProvider = metadataStore;
            TableName = tableName;
            Counter = counter ?? new IncrementingInteger();

            // Default the alias to the table name
            TableAlias = tableName;
        }

        /// <summary>
        /// For UPDATE (OVERWRITE) operation
        /// Adds result of (TableDefinition.Columns minus MutationFields) to UpdateOperations with null values
        /// There will not be any columns leftover that are PK, since they are handled in request validation.
        /// </summary>
        /// <param name="leftoverSchemaColumns"></param>
        /// <param name="updateOperations">List of Predicates representing UpdateOperations.</param>
        /// <param name="tableDefinition">The definition for the table.</param>
        public void AddNullifiedUnspecifiedFields(List<string> leftoverSchemaColumns, List<Predicate> updateOperations, TableDefinition tableDefinition)
        {
            //result of adding (TableDefinition.Columns - MutationFields) to UpdateOperations
            foreach (string leftoverColumn in leftoverSchemaColumns)
            {
                // If the left over column is autogenerated or has a default,
                // then no need to add it with a null value.
                if (tableDefinition.Columns[leftoverColumn].IsAutoGenerated
                    || tableDefinition.Columns[leftoverColumn].HasDefault)
                {
                    continue;
                }

                if (tableDefinition.Columns[leftoverColumn].IsNullable)
                {
                    Predicate predicate = new(
                        new PredicateOperand(new Column(null, leftoverColumn)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"@{MakeParamWithValue(null)}")
                    );

                    updateOperations.Add(predicate);
                }
                else
                {
                    // Non Nullable columns must have a value defined in request unless
                    // the table schema configures the column with a default value.
                    throw new DataGatewayException(
                        message: "Request must define values for all nullable and non-default valued columns.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
                }

            }
        }

        /// <summary>
        /// Get column type from table underlying the query strucutre
        /// </summary>
        public ColumnType GetColumnType(string columnName)
        {
            if (GetTableDefinition().Columns.TryGetValue(columnName, out ColumnDefinition? column))
            {
                return column.Type;
            }
            else
            {
                throw new ArgumentException($"{columnName} is not a valid column of {TableName}");
            }
        }

        /// <summary>
        /// Returns the TableDefinition for the the table of this query.
        /// </summary>
        protected TableDefinition GetTableDefinition()
        {
            return MetadataStoreProvider.GetTableDefinition(TableName);
        }

        /// <summary>
        /// Get primary key as list of string
        /// </summary>
        public List<string> PrimaryKey()
        {
            return GetTableDefinition().PrimaryKey;
        }

        /// <summary>
        /// get all columns of the table
        /// </summary>
        public List<string> AllColumns()
        {
            return GetTableDefinition().Columns.Select(col => col.Key).ToList();
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

        ///<summary>
        /// Gets the value of the parameter cast as the type of the column this parameter is associated with
        ///</summary>
        /// <exception cref="ArgumentException">columnName is not a valid column of table or param
        /// does not have a valid value type</exception>
        protected object GetParamAsColumnSystemType(string param, string columnName)
        {
            ColumnType type = GetColumnType(columnName);
            Type systemType = ColumnDefinition.ResolveColumnTypeToSystemType(type);

            try
            {
                switch (systemType.Name)
                {
                    case "String":
                        return param;
                    case "Int64":
                        return long.Parse(param);
                    default:
                        // should never happen due to the config being validated for correct types
                        throw new NotSupportedException($"{type} is not supported");
                }
            }
            catch (Exception e)
            {
                if (e is FormatException ||
                    e is ArgumentNullException ||
                    e is OverflowException)
                {
                    throw new ArgumentException($"Parameter \"{param}\" cannot be resolved as column \"{columnName}\" with type \"{type}\".");
                }

                throw;
            }
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
        /// UnderlyingType is the type main GraphQL type that is described by
        /// this type. This strips all modifiers, such as List and Non-Null.
        /// So the following GraphQL types would all have the underlyingType Book:
        /// - Book
        /// - [Book]
        /// - Book!
        /// - [Book]!
        /// - [Book!]!
        /// </summary>
        internal static ObjectType UnderlyingType(IType type)
        {
            ObjectType? underlyingType = type as ObjectType;
            if (underlyingType != null)
            {
                return underlyingType;
            }

            return UnderlyingType(type.InnerType());
        }

        /// <summary>
        /// Extracts the *Connection.items schema field from the *Connection schema field
        /// </summary>
        internal static IObjectField ExtractItemsSchemaField(IObjectField connectionSchemaField)
        {
            return UnderlyingType(connectionSchemaField.Type).Fields["items"];
        }

    }
}
