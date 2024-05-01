// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using static Azure.DataApiBuilder.Core.Authorization.AuthorizationResolver;

namespace Azure.DataApiBuilder.Core.Models;

/// <summary>
/// Contains methods to parse a GQL filter parameter
/// </summary>
public class GQLFilterParser
{
    public static readonly string NullStringValue = "NULL";

    private readonly RuntimeConfigProvider _configProvider;
    private readonly IMetadataProviderFactory _metadataProviderFactory;

    private IncrementingInteger? _tableCounter;

    /// <summary>
    /// Constructor for GQLFilterParser
    /// </summary>
    /// <param name="runtimeConfigProvider">runtimeConfig provider</param>
    /// <param name="metadataProviderFactory">metadataProvider factory.</param>
    public GQLFilterParser(RuntimeConfigProvider runtimeConfigProvider, IMetadataProviderFactory metadataProviderFactory)
    {
        _configProvider = runtimeConfigProvider;
        _metadataProviderFactory = metadataProviderFactory;
        _tableCounter = new IncrementingInteger();
    }

    /// <summary>
    /// Parse a predicate for a *FilterInput input type
    /// </summary>
    /// <param name="ctx">The GraphQL context, used to get the query variables</param>
    /// <param name="filterArgumentSchema">An IInputField object which describes the schema of the filter argument</param>
    /// <param name="fields">The fields in the *FilterInput being processed</param>
    /// <param name="queryStructure">The query structure for the entity being filtered providing
    /// the source alias of the underlying *FilterInput being processed,
    /// source definition of the table/view of the underlying *FilterInput being processed,
    /// and the function that parametrizes literals before they are written in string predicate operands.</param>
    public Predicate Parse(
        IMiddlewareContext ctx,
        IInputField filterArgumentSchema,
        List<ObjectFieldNode> fields,
        BaseQueryStructure queryStructure)
    {
        string schemaName = queryStructure.DatabaseObject.SchemaName;
        string sourceName = queryStructure.DatabaseObject.Name;
        string sourceAlias = queryStructure.SourceAlias;
        string entityName = queryStructure.EntityName;
        SourceDefinition sourceDefinition = queryStructure.GetUnderlyingSourceDefinition();

        string dataSourceName = _configProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);
        ISqlMetadataProvider metadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);

        InputObjectType filterArgumentObject = ExecutionHelper.InputObjectTypeFromIInputField(filterArgumentSchema);

        List<PredicateOperand> predicates = new();
        foreach (ObjectFieldNode field in fields)
        {
            object? fieldValue = ExecutionHelper.ExtractValueFromIValueNode(
                value: field.Value,
                argumentSchema: filterArgumentObject.Fields[field.Name.Value],
                variables: ctx.Variables);

            if (fieldValue is null)
            {
                continue;
            }

            string name = field.Name.ToString();

            bool fieldIsAnd = string.Equals(name, $"{PredicateOperation.AND}", StringComparison.OrdinalIgnoreCase);
            bool fieldIsOr = string.Equals(name, $"{PredicateOperation.OR}", StringComparison.OrdinalIgnoreCase);

            InputObjectType filterInputObjectType = ExecutionHelper.InputObjectTypeFromIInputField(filterArgumentObject.Fields[name]);
            if (fieldIsAnd || fieldIsOr)
            {
                PredicateOperation op = fieldIsAnd ? PredicateOperation.AND : PredicateOperation.OR;
                List<IValueNode> otherPredicates;
                if (fieldValue is IEnumerable<IValueNode>)
                {
                    otherPredicates = (List<IValueNode>)fieldValue;
                }
                else if (fieldValue is IEnumerable<ObjectFieldNode>)
                {
                    ObjectFieldNode fieldObject = ((List<ObjectFieldNode>)fieldValue).First();
                    ObjectValueNode value = new(fieldObject);
                    otherPredicates = new List<IValueNode> { value };
                }
                else
                {
                    throw new DataApiBuilderException(
                        message: "Invalid filter object input value type.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                predicates.Push(new PredicateOperand(ParseAndOr(
                    ctx,
                    argumentSchema: filterArgumentObject.Fields[name],
                    filterArgumentSchema: filterArgumentSchema,
                    otherPredicates,
                    queryStructure,
                    op)));
            }
            else
            {
                List<ObjectFieldNode> subfields = (List<ObjectFieldNode>)fieldValue;

                // Preserve the name value present in the filter.
                // In some cases, 'name' represents a relationship field on a source entity,
                // and not a field on a relationship target entity.
                // Additionally, 'name' may not be the same as the relationship's target entity because
                // runtime configuration supports overriding the entity name in the source entity configuration.
                // e.g.
                // comics (Source entity)  series (Target entity)
                // - id                    - id
                // - myseries [series]     - name
                // e.g. GraphQL request
                // {  comics ( filter: { myseries: { name: { eq: 'myName' } } } ) { <field selection> } }
                string backingColumnName = name;

                metadataProvider.TryGetBackingColumn(queryStructure.EntityName, field: name, out string? resolvedBackingColumnName);

                // When runtime configuration defines relationship metadata,
                // an additional field on the entity representing GraphQL type
                // will exist. That relationship field does not represent a column in
                // the representative database table and the relationship field will not have an authorization
                // rule entry. Authorization is handled by permissions defined for the relationship's
                // target entity.
                bool relationshipField = true;
                if (!string.IsNullOrWhiteSpace(resolvedBackingColumnName))
                {
                    backingColumnName = resolvedBackingColumnName;
                    relationshipField = false;
                }

                // Only perform field (column) authorization when the field is not a relationship field.
                // Due to the recursive behavior of SqlExistsQueryStructure compilation, the column authorization
                // check only occurs when access to the column's owner entity is confirmed.
                if (!relationshipField)
                {
                    string graphQLTypeName = queryStructure.EntityName;
                    string originalEntityName = metadataProvider.GetEntityName(graphQLTypeName);

                    bool columnAccessPermitted = queryStructure.AuthorizationResolver.AreColumnsAllowedForOperation(
                        entityName: originalEntityName,
                        roleName: GetHttpContextFromMiddlewareContext(ctx).Request.Headers[CLIENT_ROLE_HEADER],
                        operation: EntityActionOperation.Read,
                        columns: new[] { name });

                    if (!columnAccessPermitted)
                    {
                        throw new DataApiBuilderException(
                            message: DataApiBuilderException.GRAPHQL_FILTER_FIELD_AUTHZ_FAILURE,
                            statusCode: HttpStatusCode.Forbidden,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
                    }
                }

                // A non-standard InputType is inferred to be referencing an entity.
                // Example: {entityName}FilterInput
                if (!StandardQueryInputs.IsStandardInputType(filterInputObjectType.Name))
                {
                    if (sourceDefinition.PrimaryKey.Count != 0)
                    {
                        // For SQL i.e. when there are primary keys on the source, we need to perform a join
                        // between the parent entity being filtered and the related entity representing the
                        // non-scalar filter input.
                        HandleNestedFilterForSql(
                            ctx,
                            filterArgumentObject.Fields[name],
                            subfields,
                            predicates,
                            queryStructure,
                            metadataProvider);
                    }
                    else if (queryStructure is CosmosQueryStructure cosmosQueryStructure)
                    {
                        // This path will never get called for sql since the primary key will always required
                        // This path will only be exercised for CosmosDb_NoSql
                        FieldDefinitionNode? fieldDefinitionNode = metadataProvider.GetSchemaGraphQLFieldFromFieldName(cosmosQueryStructure.EntityName, name);

                        if (fieldDefinitionNode is null)
                        {
                            throw new DataApiBuilderException(
                                message: "Invalid filter object used as a nested field input value type.",
                                statusCode: HttpStatusCode.BadRequest,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                        }

                        string nestedFieldTypeName = fieldDefinitionNode.Type.NamedType().Name.Value;
                        if (fieldDefinitionNode.Type.IsListType())
                        {
                            HandleNestedFilterForCosmos(
                                ctx,
                                filterArgumentObject.Fields[name],
                                subfields,
                                backingColumnName,
                                nestedFieldTypeName,
                                predicates,
                                cosmosQueryStructure,
                                metadataProvider);
                        }
                        else
                        {
                            cosmosQueryStructure.DatabaseObject.Name = sourceName + "." + backingColumnName;
                            cosmosQueryStructure.SourceAlias = sourceName + "." + backingColumnName;
                            cosmosQueryStructure.EntityName = metadataProvider.GetEntityName(nestedFieldTypeName);

                            predicates.Push(new PredicateOperand(Parse(ctx,
                                filterArgumentObject.Fields[name],
                                subfields,
                                cosmosQueryStructure)));

                            cosmosQueryStructure.DatabaseObject.Name = sourceName;
                            cosmosQueryStructure.SourceAlias = sourceAlias;
                        }
                    }
                }
                else
                {
                    bool isListType = false;
                    if (queryStructure is CosmosQueryStructure)
                    {
                        FieldDefinitionNode? fieldDefinitionNode = metadataProvider.GetSchemaGraphQLFieldFromFieldName(queryStructure.EntityName, name);
                        if (fieldDefinitionNode is null)
                        {
                            throw new DataApiBuilderException(
                                message: "Invalid filter object used as a nested field input value type.",
                                statusCode: HttpStatusCode.BadRequest,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                        }

                        isListType = fieldDefinitionNode.Type.IsListType();
                    }

                    predicates.Push(
                   new PredicateOperand(
                       ParseScalarType(
                           ctx: ctx,
                           argumentSchema: filterArgumentObject.Fields[name],
                           fieldName: backingColumnName,
                           fields: subfields,
                           schemaName: schemaName,
                           tableName: sourceName,
                           tableAlias: sourceAlias,
                           processLiterals: queryStructure.MakeDbConnectionParam,
                           isListType: isListType)));
                }
            }
        }

        return MakeChainPredicate(predicates, PredicateOperation.AND);
    }

    /// <summary>
    /// For CosmosDB, a nested filter represents an EXISTS clause with a subquery.
    /// This function:
    /// 1. Defines the Exists Query structure
    /// 2. Recursively parses any more(possibly nested) filters on the Exists sub query.
    /// 3. Adds join predicates between the related entities to the Exists sub query.
    /// 4. Adds the Exists subquery to the existing list of predicates.
    /// </summary>
    /// <param name="ctx">The middleware context.</param>
    /// <param name="filterField">The nested filter field.</param>
    /// <param name="subfields">The subfields of the nested filter.</param>
    /// <param name="columnName">Current Column Name</param>
    /// <param name="entityType">Current Entity Type</param>
    /// <param name="predicates">The predicates parsed so far.</param>
    /// <param name="queryStructure">The query structure of the entity being filtered, it would be modified to contain EXIST predicates</param>
    /// <param name="metadataProvider"> Cosmos Metadata Provider, to get metadata information for a given entity </param>
    /// <exception cref="DataApiBuilderException">
    private void HandleNestedFilterForCosmos(
        IMiddlewareContext ctx,
        IInputField filterField,
        List<ObjectFieldNode> subfields,
        string columnName,
        string entityType,
        List<PredicateOperand> predicates,
        CosmosQueryStructure queryStructure,
        ISqlMetadataProvider metadataProvider)
    {
        // Validate that the field referenced in the nested input filter can be accessed.
        bool entityAccessPermitted = queryStructure.AuthorizationResolver.AreRoleAndOperationDefinedForEntity(
            entityIdentifier: entityType,
            roleName: GetHttpContextFromMiddlewareContext(ctx).Request.Headers[CLIENT_ROLE_HEADER],
            operation: EntityActionOperation.Read);

        if (!entityAccessPermitted)
        {
            throw new DataApiBuilderException(
                message: DataApiBuilderException.GRAPHQL_FILTER_ENTITY_AUTHZ_FAILURE,
                statusCode: HttpStatusCode.Forbidden,
                subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
        }

        List<Predicate> predicatesForExistsQuery = new();
        CosmosExistsQueryStructure existsQuery = new(
            ctx,
            new Dictionary<string, object?>(),
            _configProvider,
            metadataProvider,
            queryStructure.AuthorizationResolver,
            this,
            queryStructure.Counter,
            predicatesForExistsQuery);

        existsQuery.DatabaseObject.SchemaName = $"{queryStructure.SourceAlias}.{columnName}";
        existsQuery.DatabaseObject.Name = existsQuery.SourceAlias;
        existsQuery.EntityName = metadataProvider.GetEntityName(entityType);

        // Recursively parse and obtain the predicates for the Exists clause subquery
        Predicate existsQueryFilterPredicate = Parse(ctx,
                filterField,
                subfields,
                existsQuery);

        predicatesForExistsQuery.Push(existsQueryFilterPredicate);

        // The right operand is the SqlExistsQueryStructure.
        PredicateOperand right = new(existsQuery);

        // Create a new unary Exists Predicate
        Predicate existsPredicate = new(left: null, PredicateOperation.EXISTS, right);

        // Add it to the rest of the existing predicates.
        predicates.Push(new PredicateOperand(existsPredicate));

        // Add all parameters from the exists subquery to the main queryStructure.
        foreach ((string key, DbConnectionParam value) in existsQuery.Parameters)
        {
            queryStructure.Parameters.Add(key, value);
        }
    }

    /// <summary>
    /// For SQL, a nested filter represents an EXISTS clause with a join between
    /// the parent entity being filtered and the related entity representing the
    /// non-scalar filter input. This function:
    /// 1. Defines the Exists Query structure
    /// 2. Recursively parses any more(possibly nested) filters on the Exists sub query.
    /// 3. Adds join predicates between the related entities to the Exists sub query.
    /// 4. Adds the Exists subquery to the existing list of predicates.
    /// </summary>
    /// <param name="ctx">The middleware context</param>
    /// <param name="filterField">The nested filter field.</param>
    /// <param name="subfields">The subfields of the nested filter.</param>
    /// <param name="predicates">The predicates parsed so far.</param>
    /// <param name="queryStructure">The query structure of the entity being filtered.</param>
    /// <exception cref="DataApiBuilderException">
    /// throws if a relationship directive is not found on the nested filter input</exception>
    private void HandleNestedFilterForSql(
        IMiddlewareContext ctx,
        InputField filterField,
        List<ObjectFieldNode> subfields,
        List<PredicateOperand> predicates,
        BaseQueryStructure queryStructure,
        ISqlMetadataProvider metadataProvider)
    {
        string? targetGraphQLTypeNameForFilter = RelationshipDirectiveType.GetTarget(filterField);

        if (targetGraphQLTypeNameForFilter is null)
        {
            throw new DataApiBuilderException(
                message: "The GraphQL schema is missing the relationship directive on input field.",
                statusCode: HttpStatusCode.InternalServerError,
                subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
        }

        string nestedFilterEntityName = metadataProvider.GetEntityName(targetGraphQLTypeNameForFilter);

        // Validate that the field referenced in the nested input filter can be accessed.
        bool entityAccessPermitted = queryStructure.AuthorizationResolver.AreRoleAndOperationDefinedForEntity(
            entityIdentifier: nestedFilterEntityName,
            roleName: GetHttpContextFromMiddlewareContext(ctx).Request.Headers[CLIENT_ROLE_HEADER],
            operation: EntityActionOperation.Read);

        if (!entityAccessPermitted)
        {
            throw new DataApiBuilderException(
                message: DataApiBuilderException.GRAPHQL_FILTER_ENTITY_AUTHZ_FAILURE,
                statusCode: HttpStatusCode.Forbidden,
                subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
        }

        List<Predicate> predicatesForExistsQuery = new();

        // Create an SqlExistsQueryStructure as the predicate operand of Exists predicate
        // This query structure has no order by, no limit and selects 1
        // its predicates are obtained from recursively parsing the nested filter
        // and an additional predicate to reflect the join between main query and this exists subquery.
        SqlExistsQueryStructure existsQuery = new(
            GetHttpContextFromMiddlewareContext(ctx),
            metadataProvider,
            queryStructure.AuthorizationResolver,
            this,
            predicatesForExistsQuery,
            nestedFilterEntityName,
            queryStructure.Counter);

        // Recursively parse and obtain the predicates for the Exists clause subquery
        Predicate existsQueryFilterPredicate = Parse(ctx,
                filterField,
                subfields,
                existsQuery);
        predicatesForExistsQuery.Push(existsQueryFilterPredicate);

        // Add JoinPredicates to the subquery query structure so a predicate connecting
        // the outer table is added to the where clause of subquery
        existsQuery.AddJoinPredicatesForRelatedEntity(
            queryStructure.EntityName,
            queryStructure.SourceAlias,
            existsQuery);

        // The right operand is the SqlExistsQueryStructure.
        PredicateOperand right = new(existsQuery);

        // Create a new unary Exists Predicate
        Predicate existsPredicate = new(left: null, PredicateOperation.EXISTS, right);

        // Add it to the rest of the existing predicates.
        predicates.Push(new PredicateOperand(existsPredicate));

        // Add all parameters from the exists subquery to the main queryStructure.
        foreach ((string key, DbConnectionParam value) in existsQuery.Parameters)
        {
            queryStructure.Parameters.Add(key, value);
        }
    }

    /// <summary>
    /// Helper method to get the HttpContext from the MiddlewareContext.
    /// </summary>
    /// <param name="ctx">Middleware context for the object.</param>
    /// <returns>HttpContext</returns>
    /// <exception cref="DataApiBuilderException">throws exception when http context could not be found.</exception>
    public HttpContext GetHttpContextFromMiddlewareContext(IMiddlewareContext ctx)
    {
        // Get HttpContext from IMiddlewareContext and fail if resolved value is null.
        if (!ctx.ContextData.TryGetValue(nameof(HttpContext), out object? httpContextValue))
        {
            throw new DataApiBuilderException(
                message: "No HttpContext found in GraphQL Middleware Context.",
                statusCode: HttpStatusCode.BadRequest,
                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
        }

        return (HttpContext)httpContextValue!;
    }

    /// <summary>
    /// Calls the appropriate scalar type filter parser based on the type of
    /// the fields.
    /// </summary>
    /// <param name="ctx">The GraphQL context, used to get the query variables</param>
    /// <param name="argumentSchema">An IInputField object which describes the schema of the scalar input argument (e.g. IntFilterInput)</param>
    /// <param name="fieldName">The name of the field</param>
    /// <param name="fields">The subfields of the scalar field</param>
    /// <param name="schemaName">The db schema name to which the table belongs</param>
    /// <param name="tableName">The name of the table underlying the *FilterInput being processed</param>
    /// <param name="tableAlias">The alias of the table underlying the *FilterInput being processed</param>
    /// <param name="processLiterals">Parametrizes literals before they are written in string predicate operands</param>
    /// <param name="isListType">Flag to give a hint about the node type. It is only applicable for CosmosDB</param>
    private static Predicate ParseScalarType(
        IMiddlewareContext ctx,
        IInputField argumentSchema,
        string fieldName,
        List<ObjectFieldNode> fields,
        string schemaName,
        string tableName,
        string tableAlias,
        Func<object, string?, string> processLiterals,
        bool isListType = false)
    {
        Column column = new(schemaName, tableName, columnName: fieldName, tableAlias);

        return FieldFilterParser.Parse(ctx, argumentSchema, column, fields, processLiterals, isListType);
    }

    /// <summary>
    /// Parse the list of *FilterInput objects passed in and/or fields into a single predicate
    /// </summary>
    /// <returns>
    /// The predicate representation of the and/or.
    /// If and/or is passed as empty, a predicate representing 1 != 1 is returned
    /// </returns>
    /// <param name="ctx">The GraphQL context, used to get the query variables</param>
    /// <param name="argumentSchema">An IInputField object which describes the and/or filter input argument</param>
    /// <param name="filterArgumentSchema">An IInputField object which describes the base filter input argument (e.g. BookFilterInput)
    /// to which the and/or belongs </param>
    /// <param name="fields">The subfields of the and/or field</param>
    /// <param name="schemaName">The db schema name to which the table belongs</param>
    /// <param name="tableName">The name of the table underlying the *FilterInput being processed</param>
    /// <param name="tableAlias">The alias of the table underlying the *FilterInput being processed</param>
    /// <param name="sourceDefinition">Definition of the table/view underlying the *FilterInput being processed</param>
    /// <param name="op">The operation (and or or)</param>
    /// <param name="processLiterals">Parametrizes literals before they are written in string predicate operands</param>
    private Predicate ParseAndOr(
        IMiddlewareContext ctx,
        IInputField argumentSchema,
        IInputField filterArgumentSchema,
        List<IValueNode> fields,
        BaseQueryStructure baseQuery,
        PredicateOperation op)
    {
        if (fields.Count == 0)
        {
            return Predicate.MakeFalsePredicate();
        }

        List<PredicateOperand> operands = new();
        foreach (IValueNode field in fields)
        {
            object? fieldValue = ExecutionHelper.ExtractValueFromIValueNode(
                value: field,
                argumentSchema: argumentSchema,
                ctx.Variables);

            if (fieldValue is null)
            {
                continue;
            }

            List<ObjectFieldNode> subfields;
            if (fieldValue is List<ObjectFieldNode>)
            {
                subfields = (List<ObjectFieldNode>)fieldValue;
            }
            else if (fieldValue is Array)
            {
                ObjectFieldNode[] objectFieldNodes = (ObjectFieldNode[])fieldValue;
                subfields = objectFieldNodes.ToList();
            }
            else
            {
                throw new DataApiBuilderException(
                    message: "Invalid value extracted from IValueNode",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            operands.Add(new PredicateOperand(
                Parse(ctx,
                    filterArgumentSchema,
                    subfields,
                    baseQuery)));
        }

        return MakeChainPredicate(operands, op);
    }

    /// <summary>
    /// Merge a list of predicate operands into a single predicate
    /// </summary>
    /// <param name="operands">A list of PredicateOperands to be connected with a PredicateOperation</param>
    /// <param name="op">An operation used to connect the predicate operands</param>
    /// <param name="pos">No need to specify this parameter, it is used to make the method recursive</param>
    /// <param name="addParenthesis">Specify whether the final predicate should be put in parenthesis or not</param>
    public static Predicate MakeChainPredicate(
        List<PredicateOperand> operands,
        PredicateOperation op,
        int pos = 0,
        bool addParenthesis = true)
    {
        if (operands.Count == 0)
        {
            return Predicate.MakeFalsePredicate();
        }

        if (pos == operands.Count - 1)
        {
            return operands[pos].AsPredicate()!;
        }

        return new Predicate(
            operands[pos],
            op,
            new PredicateOperand(MakeChainPredicate(operands, op, pos + 1, false)),
            addParenthesis: addParenthesis && operands.Count > 1
        );
    }
}

public static class FieldFilterParser
{
    /// <summary>
    /// Parse a scalar field into a predicate
    /// </summary>
    /// <param name="ctx">The GraphQL context, used to get the query variables</param>
    /// <param name="argumentSchema">An IInputField object which describes the schema of the scalar input argument (e.g. IntFilterInput)</param>
    /// <param name="column">The table column targeted by the field</param>
    /// <param name="fields">The subfields of the scalar field</param>
    /// <param name="processLiterals">Parametrizes literals before they are written in string predicate operands</param>
    /// <param name="isListType">Flag which gives a hint about the node type in the given schema. only for CosmosDB it can be of list type. Refer <a href=https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/query/array-contains>here</a>.</param>
    public static Predicate Parse(
        IMiddlewareContext ctx,
        IInputField argumentSchema,
        Column column,
        List<ObjectFieldNode> fields,
        Func<object, string?, string> processLiterals,
        bool isListType = false)
    {
        List<PredicateOperand> predicates = new();

        InputObjectType argumentObject = ExecutionHelper.InputObjectTypeFromIInputField(argumentSchema);
        foreach (ObjectFieldNode field in fields)
        {
            string name = field.Name.ToString();
            object? value = ExecutionHelper.ExtractValueFromIValueNode(
                value: field.Value,
                argumentSchema: argumentObject.Fields[field.Name.Value],
                variables: ctx.Variables);

            bool processLiteral = true;

            if (value is null)
            {
                continue;
            }

            PredicateOperation op;
            switch (name)
            {
                case "eq":
                    op = PredicateOperation.Equal;
                    break;
                case "neq":
                    op = PredicateOperation.NotEqual;
                    break;
                case "lt":
                    op = PredicateOperation.LessThan;
                    break;
                case "gt":
                    op = PredicateOperation.GreaterThan;
                    break;
                case "lte":
                    op = PredicateOperation.LessThanOrEqual;
                    break;
                case "gte":
                    op = PredicateOperation.GreaterThanOrEqual;
                    break;
                case "contains":
                    if (isListType)
                    {
                        op = PredicateOperation.ARRAY_CONTAINS;
                    }
                    else
                    {
                        op = PredicateOperation.LIKE;
                        value = $"%{EscapeLikeString((string)value)}%";
                    }

                    break;
                case "notContains":
                    if (isListType)
                    {
                        op = PredicateOperation.NOT_ARRAY_CONTAINS;
                    }
                    else
                    {
                        op = PredicateOperation.NOT_LIKE;
                        value = $"%{EscapeLikeString((string)value)}%";
                    }

                    break;
                case "startsWith":
                    op = PredicateOperation.LIKE;
                    value = $"{EscapeLikeString((string)value)}%";
                    break;
                case "endsWith":
                    op = PredicateOperation.LIKE;
                    value = $"%{EscapeLikeString((string)value)}";
                    break;
                case "isNull":
                    processLiteral = false;
                    bool isNull = (bool)value;
                    op = isNull ? PredicateOperation.IS : PredicateOperation.IS_NOT;
                    value = GQLFilterParser.NullStringValue;
                    break;
                default:
                    throw new NotSupportedException($"Operation {name} on int type not supported.");
            }

            predicates.Push(new PredicateOperand(new Predicate(
                new PredicateOperand(column),
                op,
                new PredicateOperand(processLiteral ? $"{processLiterals(value, column.ColumnName)}" : value.ToString()))
                ));
        }

        return GQLFilterParser.MakeChainPredicate(predicates, PredicateOperation.AND);
    }

    private static string EscapeLikeString(string input)
    {
        input = input.Replace(@"\", @"\\");
        input = input.Replace(@"%", @"\%");
        input = input.Replace(@"[", @"\[");
        input = input.Replace(@"]", @"\]");
        input = input.Replace(@"_", @"\_");
        return input;
    }
}
