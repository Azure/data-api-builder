// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Services;
using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Core.Parsers
{
    /// <summary>
    /// Visits an OData filter AST and produces DAX filter predicate expressions.
    /// DAX uses && / || instead of AND / OR, and BLANK() instead of NULL.
    /// Column references are quoted with square brackets: [ColumnName].
    /// </summary>
    public class DaxODataASTVisitor : QueryNodeVisitor<string>
    {
        private readonly string _tableName;
        private readonly ISqlMetadataProvider _metadataProvider;
        private readonly string _entityName;

        public DaxODataASTVisitor(string tableName, string entityName, ISqlMetadataProvider metadataProvider)
        {
            _tableName = tableName;
            _entityName = entityName;
            _metadataProvider = metadataProvider;
        }

        /// <inheritdoc/>
        public override string Visit(BinaryOperatorNode nodeIn)
        {
            string left = nodeIn.Left.Accept(this);
            string right = nodeIn.Right.Accept(this);
            return CreateResult(nodeIn.OperatorKind, left, right);
        }

        /// <inheritdoc/>
        public override string Visit(UnaryOperatorNode nodeIn)
        {
            string child = nodeIn.Operand.Accept(this);
            return nodeIn.OperatorKind switch
            {
                UnaryOperatorKind.Not => $"(NOT {child})",
                _ => throw new NotSupportedException($"Unary operator {nodeIn.OperatorKind} is not supported in DAX filters.")
            };
        }

        /// <inheritdoc/>
        public override string Visit(SingleValuePropertyAccessNode nodeIn)
        {
            _metadataProvider.TryGetBackingColumn(_entityName, nodeIn.Property.Name, out string? backingColumnName);
            return $"'{_tableName}'[{backingColumnName}]";
        }

        /// <inheritdoc/>
        public override string Visit(ConstantNode nodeIn)
        {
            if (nodeIn.TypeReference is null)
            {
                return "BLANK()";
            }

            object value = nodeIn.Value;
            return value switch
            {
                string s => $"\"{s}\"",
                bool b => b ? "TRUE" : "FALSE",
                _ => value?.ToString() ?? "BLANK()"
            };
        }

        /// <inheritdoc/>
        public override string Visit(ConvertNode nodeIn)
        {
            return nodeIn.Source.Accept(this);
        }

        private static string CreateResult(BinaryOperatorKind op, string left, string right)
        {
            if (left == "BLANK()" || right == "BLANK()")
            {
                return CreateBlankResult(op, left, right);
            }

            return $"({left} {GetOperator(op)} {right})";
        }

        private static string CreateBlankResult(BinaryOperatorKind op, string left, string right)
        {
            string field = left == "BLANK()" ? right : left;
            return op switch
            {
                BinaryOperatorKind.Equal => $"ISBLANK({field})",
                BinaryOperatorKind.NotEqual => $"NOT ISBLANK({field})",
                _ => $"({left} {GetOperator(op)} {right})"
            };
        }

        private static string GetOperator(BinaryOperatorKind op)
        {
            return op switch
            {
                BinaryOperatorKind.Equal => "=",
                BinaryOperatorKind.NotEqual => "<>",
                BinaryOperatorKind.GreaterThan => ">",
                BinaryOperatorKind.GreaterThanOrEqual => ">=",
                BinaryOperatorKind.LessThan => "<",
                BinaryOperatorKind.LessThanOrEqual => "<=",
                BinaryOperatorKind.And => "&&",
                BinaryOperatorKind.Or => "||",
                _ => throw new NotSupportedException($"Operator {op} is not supported in DAX filters.")
            };
        }
    }
}
