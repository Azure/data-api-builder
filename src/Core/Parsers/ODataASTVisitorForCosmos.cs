// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Core.Parsers
{
    internal class ODataASTVisitorForCosmos : QueryNodeVisitor<string>
    {
        private string _prefix;

        public ODataASTVisitorForCosmos(string prefix)
        {
            this._prefix = prefix;
        }

        public override string Visit(SingleValuePropertyAccessNode nodeIn)
        {
            return ($"{_prefix}.{nodeIn.Property.Name}");
        }

        public override string Visit(BinaryOperatorNode nodeIn)
        {
            string left = nodeIn.Left.Accept(this);
            string right = nodeIn.Right.Accept(this);

            return $"({left} {GetFilterPredicateOperator(nodeIn.OperatorKind)} {right})";
        }

        public override string Visit(ConvertNode nodeIn)
        {
            return nodeIn.Source.Accept(this);
        }

        public override string Visit(ConstantNode nodeIn)
        {
            if (nodeIn.TypeReference is null)
            {
                // Represents a NULL value, we support NULL in queries so return "NULL" here
                return "NULL";
            }

            return $"'{nodeIn.Value}'";
        }

        /// <summary>
        /// Return the correct string for the binary operator that will be a part of the filter predicates
        /// that will make up the filter of the query.
        /// </summary>
        /// <param name="op">The op we will translate.</param>
        /// <returns>The string which is a translation of the op.</returns>
        private static string GetFilterPredicateOperator(BinaryOperatorKind op)
        {
            switch (op)
            {
                case BinaryOperatorKind.Equal:
                    return "=";
                case BinaryOperatorKind.GreaterThan:
                    return ">";
                case BinaryOperatorKind.GreaterThanOrEqual:
                    return ">=";
                case BinaryOperatorKind.LessThan:
                    return "<";
                case BinaryOperatorKind.LessThanOrEqual:
                    return "<=";
                case BinaryOperatorKind.NotEqual:
                    return "!=";
                case BinaryOperatorKind.And:
                    return "AND";
                case BinaryOperatorKind.Or:
                    return "OR";
                default:
                    throw new ArgumentException($"Uknown Predicate Operation of {op}");
            }
        }
    }
}
