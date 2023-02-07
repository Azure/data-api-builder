using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.Models
{
    /// <summary>
    /// Contains methods to parse a GQL filter parameter
    /// </summary>
    public class GQLFilterParser
    {
        public static readonly string NullStringValue = "NULL";

        private readonly ISqlMetadataProvider _metadataProvider;

        /// <summary>
        /// Constructor for the filter parser.
        /// </summary>
        /// <param name="metadataProvider">The metadata provider of the respective database.</param>
        public GQLFilterParser(ISqlMetadataProvider metadataProvider)
        {
            _metadataProvider = metadataProvider;
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
            SourceDefinition sourceDefinition = queryStructure.GetUnderlyingSourceDefinition();

            InputObjectType filterArgumentObject = ResolverMiddleware.InputObjectTypeFromIInputField(filterArgumentSchema);

            List<PredicateOperand> predicates = new();
            foreach (ObjectFieldNode field in fields)
            {
                object? fieldValue = ResolverMiddleware.ExtractValueFromIValueNode(
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

                InputObjectType filterInputObjectType = ResolverMiddleware.InputObjectTypeFromIInputField(filterArgumentObject.Fields[name]);
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
                    string backingColumnName = name;
                    _metadataProvider.TryGetBackingColumn(queryStructure.EntityName, field: name, out string? resolvedBackingColumnName);
                    if (!string.IsNullOrWhiteSpace(resolvedBackingColumnName))
                    {
                        backingColumnName = resolvedBackingColumnName;
                    }

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
                                queryStructure);
                        }
                        else
                        {
                            queryStructure.DatabaseObject.Name = sourceName + "." + backingColumnName;
                            queryStructure.SourceAlias = sourceName + "." + backingColumnName;
                            predicates.Push(new PredicateOperand(Parse(ctx,
                                filterArgumentObject.Fields[name],
                                subfields,
                                queryStructure)));
                            queryStructure.DatabaseObject.Name = sourceName;
                            queryStructure.SourceAlias = sourceAlias;
                        }
                    }
                    else
                    {
                        predicates.Push(
                            new PredicateOperand(
                                ParseScalarType(
                                    ctx,
                                    argumentSchema: filterArgumentObject.Fields[name],
                                    backingColumnName,
                                    subfields,
                                    schemaName,
                                    sourceName,
                                    sourceAlias,
                                    queryStructure.MakeParamWithValue)));
                    }
                }
            }

            return MakeChainPredicate(predicates, PredicateOperation.AND);
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
            BaseQueryStructure queryStructure)
        {
            string? targetGraphQLTypeNameForFilter = RelationshipDirectiveType.GetTarget(filterField);

            if (targetGraphQLTypeNameForFilter is null)
            {
                throw new DataApiBuilderException(
                    message: "The GraphQL schema is missing the relationship directive on input field.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            string nestedFilterEntityName = _metadataProvider.GetEntityName(targetGraphQLTypeNameForFilter);
            List<Predicate> predicatesForExistsQuery = new();

            // Create an SqlExistsQueryStructure as the predicate operand of Exists predicate
            // This query structure has no order by, no limit and selects 1
            // its predicates are obtained from recursively parsing the nested filter
            // and an additional predicate to reflect the join between main query and this exists subquery.
            SqlExistsQueryStructure existsQuery = new(
                ctx,
                _metadataProvider,
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
            foreach ((string key, object? value) in existsQuery.Parameters)
            {
                queryStructure.Parameters.Add(key, value);
            }
        }

        /// <summary>
        /// Calls the appropriate scalar type filter parser based on the type of
        /// the fields
        /// </summary>
        /// <param name="ctx">The GraphQL context, used to get the query variables</param>
        /// <param name="argumentSchema">An IInputField object which describes the schema of the scalar input argument (e.g. IntFilterInput)</param>
        /// <param name="name">The name of the field</param>
        /// <param name="fields">The subfields of the scalar field</param>
        /// <param name="schemaName">The db schema name to which the table belongs</param>
        /// <param name="tableName">The name of the table underlying the *FilterInput being processed</param>
        /// <param name="tableAlias">The alias of the table underlying the *FilterInput being processed</param>
        /// <param name="processLiterals">Parametrizes literals before they are written in string predicate operands</param>
        private static Predicate ParseScalarType(
            IMiddlewareContext ctx,
            IInputField argumentSchema,
            string name,
            List<ObjectFieldNode> fields,
            string schemaName,
            string tableName,
            string tableAlias,
            Func<object, string> processLiterals)
        {
            Column column = new(schemaName, tableName, columnName: name, tableAlias);

            return FieldFilterParser.Parse(ctx, argumentSchema, column, fields, processLiterals);
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
                object? fieldValue = ResolverMiddleware.ExtractValueFromIValueNode(
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
        public static Predicate Parse(
            IMiddlewareContext ctx,
            IInputField argumentSchema,
            Column column,
            List<ObjectFieldNode> fields,
            Func<object, string> processLiterals)
        {
            List<PredicateOperand> predicates = new();

            InputObjectType argumentObject = ResolverMiddleware.InputObjectTypeFromIInputField(argumentSchema);
            foreach (ObjectFieldNode field in fields)
            {
                string name = field.Name.ToString();
                object? value = ResolverMiddleware.ExtractValueFromIValueNode(
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
                        op = PredicateOperation.LIKE;
                        value = $"%{EscapeLikeString((string)value)}%";
                        break;
                    case "notContains":
                        op = PredicateOperation.NOT_LIKE;
                        value = $"%{EscapeLikeString((string)value)}%";
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
                    new PredicateOperand(processLiteral ? $"{processLiterals(value)}" : value.ToString()))
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
}
