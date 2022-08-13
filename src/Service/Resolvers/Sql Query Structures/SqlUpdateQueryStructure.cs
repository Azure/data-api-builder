using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Resolvers;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    ///<summary>
    /// Wraps all the required data and logic to write a SQL UPDATE query
    ///</summary>
    public class SqlUpdateStructure : BaseSqlQueryStructure
    {
        /// <summary>
        /// Updates to be applied to selected row
        /// </summary>
        public List<Predicate> UpdateOperations { get; }

        /// <summary>
        /// The columns used for OUTPUT
        /// </summary>
        public List<LabelledColumn> OutputColumns { get; }

        public SqlUpdateStructure(
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            IDictionary<string, object?> mutationParams,
            bool isIncrementalUpdate)
        : base(sqlMetadataProvider, entityName)
        {
            UpdateOperations = new();
            OutputColumns = GenerateOutputColumns();
            TableDefinition tableDefinition = GetUnderlyingTableDefinition();

            List<string> primaryKeys = tableDefinition.PrimaryKey;
            List<string> columns = tableDefinition.Columns.Keys.ToList();
            foreach (KeyValuePair<string, object?> param in mutationParams)
            {
                Predicate predicate = CreatePredicateForParam(param);
                // since we have already validated mutationParams we know backing column exists
                SqlMetadataProvider.TryGetBackingColumn(EntityName, param.Key, out string? backingColumn);
                // primary keys used as predicates
                if (primaryKeys.Contains(backingColumn!))
                {
                    Predicates.Add(predicate);
                }
                // use columns to determine values to edit
                else if (columns.Contains(backingColumn!))
                {
                    UpdateOperations.Add(predicate);
                }

                columns.Remove(backingColumn!);
            }

            if (!isIncrementalUpdate)
            {
                AddNullifiedUnspecifiedFields(columns, UpdateOperations, tableDefinition);
            }

            if (UpdateOperations.Count == 0)
            {
                throw new DataApiBuilderException(
                    message: "Update mutation does not update any values",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// This constructor is for GraphQL updates which have UpdateEntityInput item
        /// as one of the mutation params.
        /// </summary>
        public SqlUpdateStructure(
            IMiddlewareContext context,
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            IDictionary<string, object?> mutationParams)
            : base(sqlMetadataProvider, entityName: entityName)
        {
            UpdateOperations = new();
            TableDefinition tableDefinition = GetUnderlyingTableDefinition();
            List<string> columns = tableDefinition.Columns.Keys.ToList();
            OutputColumns = GenerateOutputColumns();
            foreach (KeyValuePair<string, object?> param in mutationParams)
            {
                // primary keys used as predicates
                if (tableDefinition.PrimaryKey.Contains(param.Key))
                {
                    Predicates.Add(CreatePredicateForParam(param));
                }
                else // Unpack the input argument type as columns to update
                if (param.Key == UpdateMutationBuilder.INPUT_ARGUMENT_NAME)
                {
                    IDictionary<string, object?> updateFields =
                        GQLMutArgumentToDictParams(context, UpdateMutationBuilder.INPUT_ARGUMENT_NAME, mutationParams);

                    foreach (KeyValuePair<string, object?> field in updateFields)
                    {
                        if (columns.Contains(field.Key))
                        {
                            UpdateOperations.Add(CreatePredicateForParam(field));
                        }
                    }
                }
            }

            if (UpdateOperations.Count == 0)
            {
                throw new DataApiBuilderException(
                    message: "Update mutation does not update any values",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        private Predicate CreatePredicateForParam(KeyValuePair<string, object?> param)
        {
            TableDefinition tableDefinition = GetUnderlyingTableDefinition();
            Predicate predicate;
            // since we have already validated param we know backing column exists
            SqlMetadataProvider.TryGetBackingColumn(EntityName, param.Key, out string? backingColumn);
            if (param.Value is null && !tableDefinition.Columns[backingColumn!].IsNullable)
            {
                throw new DataApiBuilderException(
                    message: $"Cannot set argument {param.Key} to null.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
            else if (param.Value is null)
            {
                predicate = new(
                    new PredicateOperand(
                        new Column(tableSchema: DatabaseObject.SchemaName, tableName: DatabaseObject.Name, backingColumn!)),
                    PredicateOperation.Equal,
                    new PredicateOperand($"@{MakeParamWithValue(null)}")
                );
            }
            else
            {
                predicate = new(
                    new PredicateOperand(
                        new Column(tableSchema: DatabaseObject.SchemaName, tableName: DatabaseObject.Name, param.Key)),
                    PredicateOperation.Equal,
                    new PredicateOperand($"@{MakeParamWithValue(GetParamAsColumnSystemType(param.Value.ToString()!, param.Key))}"));
            }

            return predicate;
        }
    }
}
