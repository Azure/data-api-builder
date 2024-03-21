// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Core.Resolvers
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
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            IDictionary<string, object?> mutationParams,
            HttpContext httpContext,
            bool isIncrementalUpdate)
        : base(
              metadataProvider: sqlMetadataProvider,
              authorizationResolver: authorizationResolver,
              gQLFilterParser: gQLFilterParser,
              entityName: entityName,
              httpContext: httpContext,
              operationType: EntityActionOperation.Update)
        {
            UpdateOperations = new();
            OutputColumns = GenerateOutputColumns();
            SourceDefinition sourceDefinition = GetUnderlyingSourceDefinition();

            List<string> primaryKeys = sourceDefinition.PrimaryKey;
            List<string> columns = sourceDefinition.Columns.Keys.ToList();
            foreach (KeyValuePair<string, object?> param in mutationParams)
            {
                Predicate predicate = CreatePredicateForParam(param);
                // since we have already validated mutationParams we know backing column exists
                MetadataProvider.TryGetBackingColumn(EntityName, param.Key, out string? backingColumn);
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
                AddNullifiedUnspecifiedFields(columns, UpdateOperations, sourceDefinition);
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
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            IDictionary<string, object?> mutationParams,
            HttpContext httpContext)
            : base(
                  metadataProvider: sqlMetadataProvider,
                  authorizationResolver: authorizationResolver,
                  gQLFilterParser: gQLFilterParser,
                  entityName: entityName,
                  httpContext: httpContext,
                  operationType: EntityActionOperation.Update)
        {
            UpdateOperations = new();
            SourceDefinition sourceDefinition = GetUnderlyingSourceDefinition();
            List<string> columns = sourceDefinition.Columns.Keys.ToList();
            OutputColumns = GenerateOutputColumns();
            foreach (KeyValuePair<string, object?> param in mutationParams)
            {
                // primary keys used as predicates
                string pkBackingColumn = param.Key;
                if (sqlMetadataProvider.TryGetBackingColumn(entityName, param.Key, out string? name) && !string.IsNullOrWhiteSpace(name))
                {
                    pkBackingColumn = name;
                }

                if (sourceDefinition.PrimaryKey.Contains(pkBackingColumn))
                {
                    Predicates.Add(CreatePredicateForParam(new KeyValuePair<string, object?>(pkBackingColumn, param.Value)));
                }
                else // Unpack the input argument type as columns to update
                if (param.Key == UpdateMutationBuilder.INPUT_ARGUMENT_NAME)
                {
                    IDictionary<string, object?> updateFields =
                        GQLMutArgumentToDictParams(context, UpdateMutationBuilder.INPUT_ARGUMENT_NAME, mutationParams);

                    foreach (KeyValuePair<string, object?> field in updateFields)
                    {
                        string fieldBackingColumn = field.Key;
                        if (sqlMetadataProvider.TryGetBackingColumn(entityName, field.Key, out string? resolvedBackingColumn)
                            && !string.IsNullOrWhiteSpace(resolvedBackingColumn))
                        {
                            fieldBackingColumn = resolvedBackingColumn;
                        }

                        if (columns.Contains(fieldBackingColumn))
                        {
                            UpdateOperations.Add(CreatePredicateForParam(new KeyValuePair<string, object?>(key: fieldBackingColumn, field.Value)));
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
            SourceDefinition sourceDefinition = GetUnderlyingSourceDefinition();
            Predicate predicate;
            // since we have already validated param we know backing column exists
            MetadataProvider.TryGetBackingColumn(EntityName, param.Key, out string? backingColumn);
            if (backingColumn is null)
            {
                // If param.Key was not present in the ExposedToBackingColumnMap then provided param.Key is already the backing column
                backingColumn = param.Key;
            }

            if (param.Value is null && !sourceDefinition.Columns[backingColumn].IsNullable)
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
                        new Column(tableSchema: DatabaseObject.SchemaName, tableName: DatabaseObject.Name, backingColumn)),
                    PredicateOperation.Equal,
                    new PredicateOperand($"{MakeDbConnectionParam(null, backingColumn)}")
                );
            }
            else
            {
                predicate = new(
                    new PredicateOperand(
                        new Column(tableSchema: DatabaseObject.SchemaName, tableName: DatabaseObject.Name, param.Key)),
                    PredicateOperation.Equal,
                    new PredicateOperand($"{MakeDbConnectionParam(GetParamAsSystemType(param.Value.ToString()!, param.Key, GetColumnSystemType(param.Key)), param.Key)}"));
            }

            return predicate;
        }
    }
}
