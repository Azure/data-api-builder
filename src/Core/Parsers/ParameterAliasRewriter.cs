// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Core.Parsers;

/// <summary>
/// Replaces OData parameter aliases with their typed AST values after parsing.
/// The resolver still supplies aliases during binary type promotion; this pass
/// also covers aliases in unary and root Boolean expressions.
/// </summary>
internal sealed class ParameterAliasRewriter
{
    private readonly IReadOnlyDictionary<string, SingleValueNode> _parameterAliasNodes;

    public ParameterAliasRewriter(IReadOnlyDictionary<string, SingleValueNode> parameterAliasNodes)
    {
        _parameterAliasNodes = parameterAliasNodes;
    }

    /// <summary>
    /// Rewrites all supported nodes in a filter clause and normalizes bare Boolean
    /// values into comparisons that are valid SQL predicates across providers.
    /// </summary>
    public FilterClause Rewrite(FilterClause filterClause)
    {
        SingleValueNode expression = RewriteNode(filterClause.Expression, isPredicate: true);
        return new FilterClause(expression, filterClause.RangeVariable);
    }

    private SingleValueNode RewriteNode(SingleValueNode node, bool isPredicate)
    {
        return node switch
        {
            BinaryOperatorNode binaryNode => RewriteBinaryOperator(binaryNode),
            UnaryOperatorNode unaryNode => new UnaryOperatorNode(
                unaryNode.OperatorKind,
                RewriteNode(unaryNode.Operand, isPredicate: true)),
            ConvertNode convertNode => NormalizeBooleanPredicate(
                new ConvertNode(
                    RewriteNode(convertNode.Source, isPredicate: false),
                    convertNode.TypeReference),
                isPredicate),
            ParameterAliasNode aliasNode => RewriteAlias(aliasNode, isPredicate),
            ConstantNode constantNode => NormalizeBooleanPredicate(constantNode, isPredicate),
            SingleValuePropertyAccessNode propertyNode => NormalizeBooleanPredicate(propertyNode, isPredicate),
            _ => throw new ODataException(
                $"Database policy expression node '{node.Kind}' is not supported for typed claim binding.")
        };
    }

    private SingleValueNode RewriteBinaryOperator(BinaryOperatorNode node)
    {
        bool operandsArePredicates = node.OperatorKind is BinaryOperatorKind.And or BinaryOperatorKind.Or;
        return new BinaryOperatorNode(
            node.OperatorKind,
            RewriteNode(node.Left, operandsArePredicates),
            RewriteNode(node.Right, operandsArePredicates));
    }

    private SingleValueNode RewriteAlias(ParameterAliasNode aliasNode, bool isPredicate)
    {
        if (!_parameterAliasNodes.TryGetValue(aliasNode.Alias, out SingleValueNode? valueNode))
        {
            throw new ODataException($"No value was supplied for database policy parameter alias '{aliasNode.Alias}'.");
        }

        return RewriteNode(valueNode, isPredicate);
    }

    private static SingleValueNode NormalizeBooleanPredicate(SingleValueNode node, bool isPredicate)
    {
        if (!isPredicate || node.TypeReference?.PrimitiveKind() is not EdmPrimitiveTypeKind.Boolean)
        {
            return node;
        }

        return new BinaryOperatorNode(
            BinaryOperatorKind.Equal,
            node,
            new ConstantNode(true));
    }
}
