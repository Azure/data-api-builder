// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Globalization;
using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Core.Resolvers
{

    /// <summary>
    /// Holds shared properties and methods among
    /// Sql*QueryStructure classes
    /// </summary>
    public abstract class BaseSqlQueryStructure : BaseQueryStructure
    {

        public Dictionary<string, DbType> ParamToDbTypeMap { get; set; } = new();

        /// <summary>
        /// All tables/views that should be in the FROM clause of the query.
        /// All these objects are linked via an INNER JOIN.
        /// </summary>
        public List<SqlJoinStructure> Joins { get; }

        /// <summary>
        /// FilterPredicates is a string that represents the filter portion of our query
        /// in the WHERE Clause. This is generated specifically from the $filter portion
        /// of the query string.
        /// </summary>
        public string? FilterPredicates { get; set; }

        /// <summary>
        /// Collection of all the fields referenced in the database policy for create action.
        /// The fields referenced in the database policy should be a subset of the fields that are being inserted via the insert statement,
        /// as then only we would be able to make them a part of our SELECT FROM clause from the temporary table.
        /// This will only be populated for POST/PUT/PATCH operations.
        /// </summary>
        public HashSet<string> FieldsReferencedInDbPolicyForCreateAction { get; set; } = new();

        public BaseSqlQueryStructure(
            ISqlMetadataProvider metadataProvider,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            List<Predicate>? predicates = null,
            string entityName = "",
            IncrementingInteger? counter = null,
            HttpContext? httpContext = null,
            EntityActionOperation operationType = EntityActionOperation.None,
            bool isLinkingEntity = false
            )
            : base(metadataProvider, authorizationResolver, gQLFilterParser, predicates, entityName, counter)
        {
            Joins = new();

            // For GraphQL read operation, we are deliberately not passing httpContext to this point
            // and hence it will take its default value i.e. null here.
            // For GraphQL read operation, the database policy predicates are added later in the Sql{*}QueryStructure classes.
            // Linking entities are not configured by the users through the config file.
            // DAB interprets the database metadata for linking tables and creates an Entity objects for them.
            // This is done because linking entity field information are needed for successfully
            // generating the schema when multiple create feature is enabled.
            if (httpContext is not null && !isLinkingEntity)
            {
                AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(
                operationType,
                this,
                httpContext,
                authorizationResolver,
                metadataProvider
                );
            }
        }

        /// <summary>
        /// For UPDATE (OVERWRITE) operation
        /// Adds result of (SourceDefinition.Columns minus MutationFields) to UpdateOperations with null values
        /// There will not be any columns leftover that are PK, since they are handled in request validation.
        /// </summary>
        /// <param name="leftoverSchemaColumns"></param>
        /// <param name="updateOperations">List of Predicates representing UpdateOperations.</param>
        /// <param name="sourceDefinition">The definition for the entity (table/view).</param>
        public void AddNullifiedUnspecifiedFields(
            List<string> leftoverSchemaColumns,
            List<Predicate> updateOperations,
            SourceDefinition sourceDefinition)
        {
            //result of adding (SourceDefinition.Columns - MutationFields) to UpdateOperations
            foreach (string leftoverColumn in leftoverSchemaColumns)
            {
                // If the left over column is a read-only column,
                // then no need to add it with a null value.
                if (sourceDefinition.Columns[leftoverColumn].IsReadOnly)
                {
                    continue;
                }

                else
                {
                    Predicate predicate = new(
                        new PredicateOperand(new Column(tableSchema: DatabaseObject.SchemaName, tableName: DatabaseObject.Name, leftoverColumn)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"{MakeDbConnectionParam(value: null, leftoverColumn)}")
                    );

                    updateOperations.Add(predicate);
                }
            }
        }

        /// <summary>
        /// Get column type from table underlying the query structure
        /// </summary>
        /// <param name="columnName">backing column name</param>
        public Type GetColumnSystemType(string columnName)
        {
            if (GetUnderlyingSourceDefinition().Columns.TryGetValue(columnName, out ColumnDefinition? column))
            {
                return column.SystemType;
            }
            else
            {
                throw new DataApiBuilderException(
                    message: $"{columnName} is not a valid column of {DatabaseObject.Name}",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest
                    );
            }
        }

        /// <summary>
        /// Based on the relationship metadata involving referenced and
        /// referencing columns of a foreign key, add the join predicates
        /// to the subquery Query structure created for the given target entity Name
        /// and related source alias.
        /// There are only a couple of options for the foreign key - we only use the
        /// valid foreign key definition. It is guaranteed at least one fk definition
        /// will be valid since the MetadataProvider.ValidateAllFkHaveBeenInferred.
        /// </summary>
        /// <param name="targetEntityName">Entity name as in config file for the related entity.</param>
        /// <param name="relatedSourceAlias">The alias assigned for the underlying source of this related entity.</param>
        /// <param name="subQuery">The subquery to which the join predicates are to be added.</param>
        public void AddJoinPredicatesForRelatedEntity(
            string targetEntityName,
            string relatedSourceAlias,
            BaseSqlQueryStructure subQuery)
        {
            SourceDefinition sourceDefinition = GetUnderlyingSourceDefinition();
            DatabaseObject relatedEntityDbObject = MetadataProvider.EntityToDatabaseObject[targetEntityName];
            SourceDefinition relatedEntitySourceDefinition = MetadataProvider.GetSourceDefinition(targetEntityName);
            if (// Search for the foreign key information either in the source or target entity.
                sourceDefinition.SourceEntityRelationshipMap.TryGetValue(
                    EntityName,
                    out RelationshipMetadata? relationshipMetadata)
                && relationshipMetadata.TargetEntityToFkDefinitionMap.TryGetValue(
                    targetEntityName,
                    out List<ForeignKeyDefinition>? foreignKeyDefinitions)
                || relatedEntitySourceDefinition.SourceEntityRelationshipMap.TryGetValue(
                    targetEntityName, out relationshipMetadata)
                && relationshipMetadata.TargetEntityToFkDefinitionMap.TryGetValue(
                    EntityName,
                    out foreignKeyDefinitions))
            {
                Dictionary<DatabaseObject, string> associativeTableAndAliases = new();
                // For One-One and One-Many, not all fk definitions would be valid
                // but at least 1 will be.
                // Identify the side of the relationship first, then check if its valid
                // by ensuring the referencing and referenced column count > 0
                // before adding the predicates.
                foreach (ForeignKeyDefinition foreignKeyDefinition in foreignKeyDefinitions)
                {
                    // First identify which side of the relationship, this fk definition
                    // is looking at.
                    if (foreignKeyDefinition.Pair.ReferencingDbTable.Equals(DatabaseObject))
                    {
                        // Case where fk in parent entity references the nested entity.
                        // Verify this is a valid fk definition before adding the join predicate.
                        if (foreignKeyDefinition.ReferencingColumns.Count > 0
                            && foreignKeyDefinition.ReferencedColumns.Count > 0)
                        {
                            subQuery.Predicates.AddRange(CreateJoinPredicates(
                                SourceAlias,
                                foreignKeyDefinition.ReferencingColumns,
                                relatedSourceAlias,
                                foreignKeyDefinition.ReferencedColumns));
                        }
                    }
                    else if (foreignKeyDefinition.Pair.ReferencingDbTable.Equals(relatedEntityDbObject))
                    {
                        // Case where fk in nested entity references the parent entity.
                        if (foreignKeyDefinition.ReferencingColumns.Count > 0
                            && foreignKeyDefinition.ReferencedColumns.Count > 0)
                        {
                            subQuery.Predicates.AddRange(CreateJoinPredicates(
                                relatedSourceAlias,
                                foreignKeyDefinition.ReferencingColumns,
                                SourceAlias,
                                foreignKeyDefinition.ReferencedColumns));
                        }
                    }
                    else
                    {
                        DatabaseObject associativeTableDbObject =
                            foreignKeyDefinition.Pair.ReferencingDbTable;
                        // Case when the linking object is the referencing table
                        if (!associativeTableAndAliases.TryGetValue(
                                associativeTableDbObject,
                                out string? associativeTableAlias))
                        {
                            // this is the first fk definition found for this associative table.
                            // create an alias for it and store for later lookup.
                            associativeTableAlias = CreateTableAlias();
                            associativeTableAndAliases.Add(associativeTableDbObject, associativeTableAlias);
                        }

                        if (foreignKeyDefinition.Pair.ReferencedDbTable.Equals(DatabaseObject))
                        {
                            subQuery.Predicates.AddRange(CreateJoinPredicates(
                                associativeTableAlias,
                                foreignKeyDefinition.ReferencingColumns,
                                SourceAlias,
                                foreignKeyDefinition.ReferencedColumns));
                        }
                        else
                        {
                            subQuery.Joins.Add(new SqlJoinStructure
                            (
                                associativeTableDbObject,
                                associativeTableAlias,
                                CreateJoinPredicates(
                                    associativeTableAlias,
                                    foreignKeyDefinition.ReferencingColumns,
                                    relatedSourceAlias,
                                    foreignKeyDefinition.ReferencedColumns
                                    ).ToList()
                            ));
                        }
                    }
                }
            }
            else
            {
                throw new DataApiBuilderException(
                message: $"Could not find relationship between entities: {EntityName} and " +
                $"{targetEntityName}.",
                statusCode: HttpStatusCode.BadRequest,
                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Creates equality predicates between the columns of the left table and
        /// the columns of the right table. The columns are compared in order,
        /// thus the lists should be the same length.
        /// </summary>
        protected static IEnumerable<Predicate> CreateJoinPredicates(
            string leftTableAlias,
            List<string> leftColumnNames,
            string rightTableAlias,
            List<string> rightColumnNames)
        {
            return leftColumnNames.Zip(rightColumnNames,
                    (leftColumnName, rightColumnName) =>
                    {
                        // no table name or schema here is needed because this is a subquery that joins on table alias
                        Column leftColumn = new(tableSchema: string.Empty, tableName: string.Empty, columnName: leftColumnName, tableAlias: leftTableAlias);
                        Column rightColumn = new(tableSchema: string.Empty, tableName: string.Empty, columnName: rightColumnName, tableAlias: rightTableAlias);
                        return new Predicate(
                            new PredicateOperand(leftColumn),
                            PredicateOperation.Equal,
                            new PredicateOperand(rightColumn)
                        );
                    }
                );
        }

        /// <summary>
        /// Return the StoredProcedureDefinition associated with this database object
        /// </summary>
        protected StoredProcedureDefinition GetUnderlyingStoredProcedureDefinition()
        {
            return MetadataProvider.GetStoredProcedureDefinition(EntityName);
        }

        /// <summary>
        /// Get primary key as list of string
        /// </summary>
        public List<string> PrimaryKey()
        {
            return GetUnderlyingSourceDefinition().PrimaryKey;
        }

        /// <summary>
        /// get all columns of the table
        /// </summary>
        public List<string> AllColumns()
        {
            return GetUnderlyingSourceDefinition().Columns.Select(col => col.Key).ToList();
        }

        /// <summary>
        /// Get a list of the output columns for this table.
        /// An output column is a labelled column that holds
        /// both the backing column and a label with the exposed name.
        /// </summary>
        /// <returns>List of LabelledColumns</returns>
        protected List<LabelledColumn> GenerateOutputColumns()
        {
            List<LabelledColumn> outputColumns = new();
            foreach (string columnName in GetUnderlyingSourceDefinition().Columns.Keys)
            {
                if (!MetadataProvider.TryGetExposedColumnName(
                    entityName: EntityName,
                    backingFieldName: columnName,
                    out string? exposedName))
                {
                    continue;
                }

                outputColumns.Add(new(
                    tableSchema: DatabaseObject.SchemaName,
                    tableName: DatabaseObject.Name,
                    columnName: columnName,
                    label: exposedName!,
                    tableAlias: SourceAlias));
            }

            return outputColumns;
        }

        /// <summary>
        /// Tries to parse the string parameter to the given system type
        /// Useful for inferring parameter types for columns or procedure parameters
        /// </summary>
        /// <param name="param"></param>
        /// <param name="systemType"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        protected static object ParseParamAsSystemType(string param, Type systemType)
        {
            return systemType.Name switch
            {
                "String" => param,
                "Byte" => byte.Parse(param),
                "Byte[]" => Convert.FromBase64String(param),
                "Int16" => short.Parse(param),
                "Int32" => int.Parse(param),
                "Int64" => long.Parse(param),
                "Single" => float.Parse(param),
                "Double" => double.Parse(param),
                "Decimal" => decimal.Parse(param),
                "Boolean" => bool.Parse(param),
                "DateTime" => DateTimeOffset.Parse(param, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal).DateTime,
                "DateTimeOffset" => DateTimeOffset.Parse(param, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal),
                "Date" => DateOnly.Parse(param),
                "Guid" => Guid.Parse(param),
                "TimeOnly" => TimeOnly.Parse(param),
                "TimeSpan" => TimeOnly.Parse(param),
                _ => throw new NotSupportedException($"{systemType.Name} is not supported")
            };
        }

        /// <summary>
        /// Very similar to GQLArgumentToDictParams but only extracts the argument names from
        /// the specified field which means that the method does not need a middleware context
        /// to resolve the values of the arguments
        /// </summary>
        /// <param name="fieldName">the field from which to extract the argument names</param>
        /// <param name="mutationParameters">a dictionary of mutation parameters</param>
        internal static List<string> GetSubArgumentNamesFromGQLMutArguments
        (
            string fieldName,
            IDictionary<string, object?> mutationParameters)
        {
            string errMsg;

            if (mutationParameters.TryGetValue(fieldName, out object? item))
            {
                // An inline argument was set
                // TODO: This assumes the input was NOT nullable.
                if (item is List<ObjectFieldNode> mutationInputRaw)
                {
                    return mutationInputRaw.Select(node => node.Name.Value).ToList();
                }
                else
                {
                    errMsg = $"Unexpected {fieldName} argument format.";
                }
            }
            else
            {
                errMsg = $"Expected {fieldName} argument in mutation arguments.";
            }

            // should not happen due to gql schema validation
            throw new DataApiBuilderException(
                message: errMsg,
                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                statusCode: HttpStatusCode.BadRequest);
        }

        /// <summary>
        /// Creates a dictionary of fields and their values
        /// from a field with type List<ObjectFieldNode> fetched
        /// a dictionary of parameters
        /// Used to extract values from parameters
        /// </summary>
        /// <param name="context">GQL middleware context used to resolve the values of arguments</param>
        /// <param name="fieldName">the gql field from which to extract the parameters</param>
        /// <param name="mutationParameters">a dictionary of mutation parameters</param>
        /// <exception cref="InvalidDataException"></exception>
        internal static IDictionary<string, object?> GQLMutArgumentToDictParams(
            IMiddlewareContext context,
            string fieldName,
            IDictionary<string, object?> mutationParameters)
        {
            string errMsg;

            if (mutationParameters.TryGetValue(fieldName, out object? item))
            {
                IObjectField fieldSchema = context.Selection.Field;
                IInputField itemsArgumentSchema = fieldSchema.Arguments[fieldName];
                InputObjectType itemsArgumentObject = ExecutionHelper.InputObjectTypeFromIInputField(itemsArgumentSchema);

                // An inline argument was set
                // TODO: This assumes the input was NOT nullable.
                if (item is List<ObjectFieldNode> mutationInputRaw)
                {
                    Dictionary<string, object?> mutationInput = new();
                    foreach (ObjectFieldNode node in mutationInputRaw)
                    {
                        string nodeName = node.Name.Value;
                        mutationInput.Add(nodeName, ExecutionHelper.ExtractValueFromIValueNode(
                            value: node.Value,
                            argumentSchema: itemsArgumentObject.Fields[nodeName],
                            variables: context.Variables));
                    }

                    return mutationInput;
                }
                else
                {
                    errMsg = $"Unexpected {fieldName} argument format.";
                }
            }
            else
            {
                errMsg = $"Expected {fieldName} argument in mutation arguments.";
            }

            // should not happen due to gql schema validation
            throw new DataApiBuilderException(
                message: errMsg,
                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                statusCode: HttpStatusCode.BadRequest);
        }

        /// <summary>
        /// After SqlQueryStructure is instantiated, process a database authorization policy
        /// for GraphQL requests with the ODataASTVisitor to populate DbPolicyPredicates.
        /// Processing will also occur for GraphQL sub-queries.
        /// </summary>
        /// <param name="dbPolicyClause">FilterClause from processed runtime configuration permissions Policy:Database</param>
        /// <param name="operation">CRUD operation for which the database policy predicates are to be evaluated.</param>
        /// <exception cref="DataApiBuilderException">Thrown when the OData visitor traversal fails. Possibly due to malformed clause.</exception>
        public void ProcessOdataClause(FilterClause? dbPolicyClause, EntityActionOperation operation)
        {
            if (dbPolicyClause is null)
            {
                DbPolicyPredicatesForOperations[operation] = null;
                return;
            }

            ODataASTVisitor visitor = new(this, MetadataProvider, operation);
            try
            {
                DbPolicyPredicatesForOperations[operation] = GetFilterPredicatesFromOdataClause(dbPolicyClause, visitor);
            }
            catch (Exception ex)
            {
                throw new DataApiBuilderException(
                    message: "Policy query parameter is not well formed for GraphQL Policy Processing.",
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed,
                    innerException: ex);
            }
        }

        protected static string? GetFilterPredicatesFromOdataClause(FilterClause filterClause, ODataASTVisitor visitor)
        {
            return filterClause.Expression.Accept<string>(visitor);
        }

        /// <summary>
        /// Helper method to get the database policy for the given operation.
        /// </summary>
        /// <param name="operation">Operation for which the database policy is to be determined.</param>
        /// <returns>Database policy for the operation.</returns>
        public string? GetDbPolicyForOperation(EntityActionOperation operation)
        {
            if (!DbPolicyPredicatesForOperations.TryGetValue(operation, out string? policy))
            {
                policy = null;
            }

            return policy;
        }

        /// <summary>
        /// Gets the value of the parameter cast as the system type
        /// </summary>
        /// <param name="fieldValue">Field value as a string</param>
        /// <param name="fieldName">Field name whose value is being converted to the specified system type. This is used only for constructing the error messages incase of conversion failures</param>
        /// <param name="systemType">System type to which the parameter value is parsed to</param>
        /// <returns>The parameter value parsed to the specified system type</returns>
        /// <exception cref="DataApiBuilderException">Raised when the conversion of parameter value to the specified system type fails. The error message returned will be different in development
        /// and production modes. In production mode, the error message returned will be generic so as to not reveal information about the database object backing the entity</exception>
        protected object GetParamAsSystemType(string fieldValue, string fieldName, Type systemType)
        {
            try
            {
                return ParseParamAsSystemType(fieldValue, systemType);
            }
            catch (Exception e) when (e is FormatException || e is ArgumentNullException || e is OverflowException)
            {

                string errorMessage;
                EntitySourceType sourceTypeOfDbObject = MetadataProvider.EntityToDatabaseObject[EntityName].SourceType;
                if (MetadataProvider.IsDevelopmentMode())
                {
                    if (sourceTypeOfDbObject is EntitySourceType.StoredProcedure)
                    {
                        errorMessage = $@"Parameter ""{fieldValue}"" cannot be resolved as stored procedure parameter ""{fieldName}"" " +
                                $@"with type ""{systemType.Name}"".";
                    }
                    else
                    {
                        errorMessage = $"Parameter \"{fieldValue}\" cannot be resolved as column \"{fieldName}\" " +
                                $"with type \"{systemType.Name}\".";
                    }
                }
                else
                {
                    string fieldNameToBeDisplayedInErrorMessage = fieldName;
                    if (sourceTypeOfDbObject is EntitySourceType.Table || sourceTypeOfDbObject is EntitySourceType.View)
                    {
                        if (MetadataProvider.TryGetExposedColumnName(EntityName, fieldName, out string? exposedName))
                        {
                            fieldNameToBeDisplayedInErrorMessage = exposedName!;
                        }
                    }

                    errorMessage = $"Invalid value provided for field: {fieldNameToBeDisplayedInErrorMessage}";
                }

                throw new DataApiBuilderException(
                    message: errorMessage,
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                    innerException: e);

            }
        }
    }
}
