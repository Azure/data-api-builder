using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.Models
{

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
                        value = GraphQLFilterParsers.NullStringValue;
                        break;
                    default:
                        throw new NotSupportedException($"Operation {name} on int type not supported.");
                }

                predicates.Push(new PredicateOperand(new Predicate(
                    new PredicateOperand(column),
                    op,
                    new PredicateOperand(processLiteral ? $"@{processLiterals(value)}" : value.ToString()))
                ));
            }

            return GraphQLFilterParsers.MakeChainPredicate(predicates, PredicateOperation.AND);
        }

        public static Predicate Parse(
            Column column,
            IDictionary<string, object?> fields,
            Func<object, string> processLiterals)
        {
            List<PredicateOperand> predicates = new();

            foreach (KeyValuePair<string, object?> field in fields)
            {
                string name = field.Key;
                object? value = field.Value;
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
                        value = GraphQLFilterParsers.NullStringValue;
                        break;
                    default:
                        throw new NotSupportedException($"Operation {name} on int type not supported.");
                }

                predicates.Push(new PredicateOperand(new Predicate(
                    new PredicateOperand(column),
                    op,
                    new PredicateOperand(processLiteral ? $"@{processLiterals(value)}" : value.ToString()))
                ));
            }

            return GraphQLFilterParsers.MakeChainPredicate(predicates, PredicateOperation.AND);
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
