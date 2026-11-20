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

    /// <summary>
    /// Initializes a rewriter with the typed values available for policy parameter aliases.
    /// </summary>
    /// <param name="parameterAliasNodes">Typed AST values keyed by parameter alias.</param>
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

    /// <summary>
    /// Recursively replaces aliases and normalizes Boolean values according to whether the
    /// current node is used as a predicate or as an operand value.
    /// </summary>
    /// <param name="node">The AST node to rewrite.</param>
    /// <param name="isPredicate">Whether the node occupies a Boolean predicate position.</param>
    /// <returns>The rewritten AST node.</returns>
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

    /// <summary>
    /// Rewrites both operands of a binary operator, treating operands of logical operators
    /// as predicates and operands of comparison operators as values.
    /// </summary>
    /// <param name="node">The binary operator node to rewrite.</param>
    /// <returns>A binary operator node containing the rewritten operands.</returns>
    private SingleValueNode RewriteBinaryOperator(BinaryOperatorNode node)
    {
        bool operandsArePredicates = node.OperatorKind is BinaryOperatorKind.And or BinaryOperatorKind.Or;
        return new BinaryOperatorNode(
            node.OperatorKind,
            RewriteNode(node.Left, operandsArePredicates),
            RewriteNode(node.Right, operandsArePredicates));
    }

    /// <summary>
    /// Replaces a parameter alias with its supplied typed value and applies any
    /// context-specific Boolean normalization to that value.
    /// </summary>
    /// <param name="aliasNode">The parameter alias to resolve.</param>
    /// <param name="isPredicate">Whether the alias occupies a Boolean predicate position.</param>
    /// <returns>The rewritten typed value for the alias.</returns>
    /// <exception cref="ODataException">Thrown when no value was supplied for the alias.</exception>
    private SingleValueNode RewriteAlias(ParameterAliasNode aliasNode, bool isPredicate)
    {
        if (!_parameterAliasNodes.TryGetValue(aliasNode.Alias, out SingleValueNode? valueNode))
        {
            throw new ODataException($"No value was supplied for database policy parameter alias '{aliasNode.Alias}'.");
        }

        return RewriteNode(valueNode, isPredicate);
    }

    /// <summary>
    /// Converts a bare Boolean value used as a predicate into an explicit equality with
    /// <see langword="true"/>, while preserving expressions that are already predicates.
    /// </summary>
    /// <param name="node">The node to normalize.</param>
    /// <param name="isPredicate">Whether the node occupies a Boolean predicate position.</param>
    /// <returns>The original node or an explicit Boolean equality predicate.</returns>
    private static SingleValueNode NormalizeBooleanPredicate(SingleValueNode node, bool isPredicate)
    {
        if (!isPredicate ||
            node.TypeReference?.PrimitiveKind() is not EdmPrimitiveTypeKind.Boolean ||
            IsPredicateExpression(node))
        {
            return node;
        }

        return new BinaryOperatorNode(
            BinaryOperatorKind.Equal,
            node,
            new ConstantNode(true));
    }

    /// <summary>
    /// Returns whether a Boolean node already represents a predicate rather than a bare value.
    /// OData can wrap comparison predicates in one or more conversion nodes when binding logical
    /// operators. Such predicates must not be rewritten as "predicate eq true", which is invalid SQL.
    /// </summary>
    private static bool IsPredicateExpression(SingleValueNode node)
    {
        return node switch
        {
            BinaryOperatorNode => true,
            UnaryOperatorNode => true,
            ConvertNode convertNode => IsPredicateExpression(convertNode.Source),
            _ => false
        };
    }
}
