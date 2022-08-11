using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Config;
using HotChocolate.Language;

namespace Azure.DataApiBuilder.Service.Models
{
    /// <summary>
    /// Contains methods to parse a GraphQL filter parameter
    /// </summary>
    public static class GraphQLFilterParsers
    {
        public static readonly string NullStringValue = "NULL";

        /// <summary>
        /// Parse a predicate for a *FilterInput input type
        /// </summary>
        /// <param name="ctx">The GraphQL context, used to get the query variables</param>
        /// <param name="filterArgumentSchema">An IInputField object which describes the schema of the filter argument</param>
        /// <param name="fields">The fields in the *FilterInput being processed</param>
        /// <param name="tableAlias">The table alias underlyin the *FilterInput being processed</param>
        /// <param name="table">The table underlying the *FilterInput being processed</param>
        /// <param name="processLiterals">Parametrizes literals before they are written in string predicate operands</param>
        public static Predicate Parse(
            IDictionary<string, object?> fields,
            string schemaName,
            string tableName,
            string tableAlias,
            TableDefinition table,
            Func<object, string> processLiterals)
        {
            List<PredicateOperand> predicates = new();
            foreach ((string name, object? fieldValue) in fields)
            {
                if (fieldValue is null)
                {
                    continue;
                }

                bool fieldIsAnd = string.Equals(name, $"{PredicateOperation.AND}", StringComparison.OrdinalIgnoreCase);
                bool fieldIsOr = string.Equals(name, $"{PredicateOperation.OR}", StringComparison.OrdinalIgnoreCase);

                if (fieldIsAnd || fieldIsOr)
                {
                    PredicateOperation op = fieldIsAnd ? PredicateOperation.AND : PredicateOperation.OR;

                    IEnumerable<object> otherPredicates = (IEnumerable<object>)fieldValue;
                    predicates.Push(new PredicateOperand(ParseAndOr(
                        otherPredicates,
                        schemaName,
                        tableName,
                        tableAlias,
                        table,
                        op,
                        processLiterals)));
                }
                else
                {
                    IDictionary<string, object?> subfields = (IDictionary<string, object?>)fieldValue;
                    predicates.Push(new PredicateOperand(ParseScalarType(
                        name,
                        subfields,
                        schemaName,
                        tableName,
                        tableAlias,
                        processLiterals)));
                }
            }

            return MakeChainPredicate(predicates, PredicateOperation.AND);
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
            string name,
            IDictionary<string, object?> fields,
            string schemaName,
            string tableName,
            string tableAlias,
            Func<object, string> processLiterals)
        {
            Column column = new(schemaName, tableName, columnName: name, tableAlias);

            return FieldFilterParser.Parse(column, fields, processLiterals);
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
        /// <param name="table">The table underlying the *FilterInput being processed</param>
        /// <param name="op">The operation (and or or)</param>
        /// <param name="processLiterals">Parametrizes literals before they are written in string predicate operands</param>
        private static Predicate ParseAndOr(
            IEnumerable<object> fields,
            string schemaName,
            string tableName,
            string tableAlias,
            TableDefinition table,
            PredicateOperation op,
            Func<object, string> processLiterals)
        {
            if (!fields.Any())
            {
                return Predicate.MakeFalsePredicate();
            }

            List<PredicateOperand> operands = new(
                fields.Cast<IDictionary<string, object?>>()
                .Select(field => new PredicateOperand(Parse(field, schemaName, tableName, tableAlias, table, processLiterals)))
            );
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
}
