using System;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

/// <summary>
/// This class is a visitor for an AST generated when parsing a $filter query string
/// with the OData Uri Parser.
/// </summary>
public class ODataASTVisitor : QueryNodeVisitor<string>
{
    private SqlQueryStructure _struct;

    public ODataASTVisitor(SqlQueryStructure structure)
    {
        _struct = structure;
    }

    /// <summary>
    /// Represents visiting a BinaryOperatorNode, which will hold either
    /// a Predicate operation (eq, gt, lt, etc), or a Logical operaton (And, Or).
    /// </summary>
    /// <param name="nodeIn">The node visited.</param>
    /// <returns>String concatenation of (left op right)</returns>
    public override string Visit(BinaryOperatorNode nodeIn)
    {
        // In order traversal but add parens to maintain order of logical operations
        string left = nodeIn.Left.Accept(this);
        string right = nodeIn.Right.Accept(this);
        return CreateResult(nodeIn.OperatorKind, left, right);
    }

    /// <summary>
    /// Represents visiting a UnaryNode, which is what holds unary
    /// operators such as NOT.
    /// </summary>
    /// <param name="nodeIn">The node visisted.</param>
    /// <returns>String concatenation of (op children)</returns>
    public override string Visit(UnaryOperatorNode nodeIn)
    {
        string child = nodeIn.Operand.Accept(this);
        return $"({GetFilterPredicateOperator(nodeIn.OperatorKind)} {child} )";
    }

    /// <summary>
    /// Represents visiting a SingleValuePropertyAccessNode, which is what
    /// holds a field name in the AST.
    /// </summary>
    /// <param name="nodeIn">The node visited.</param>
    /// <returns>String representing the Field name</returns>
    public override string Visit(SingleValuePropertyAccessNode nodeIn)
    {
        return nodeIn.Property.Name;
    }

    /// <summary>
    /// Represents visiting a ConstantNode, which is what
    /// holds a value in the AST. 
    /// </summary>
    /// <param name="nodeIn">The node visited.</param>
    /// <returns>String representing param that holds given value.</returns>
    public override string Visit(ConstantNode nodeIn)
    {
        if (nodeIn.TypeReference is null)
        {
            // Represents a NULL value, we support NULL in queries so return "NULL" here
            return "NULL";
        }

        return $"@{_struct.MakeParamWithValue(GetParamWithSystemType(nodeIn.Value.ToString(), nodeIn.TypeReference))}";
    }

    /// <summary>
    /// Represents visiting a ConvertNode, which holds
    /// some other node as its source.
    /// </summary>
    /// <param name="nodeIn">The node visited.</param>
    /// <returns></returns>
    public override string Visit(ConvertNode nodeIn)
    {
        // call accept on source to keep traversing AST.
        return nodeIn.Source.Accept(this);
    }

    ///<summary>
    /// Gets the value of the parameter cast as the type the edm model associated to this parameter
    ///</summary>
    /// <exception cref="ArgumentException">Param is not valid for given EdmType</exception>
    private static object GetParamWithSystemType(string param, IEdmTypeReference edmType)
    {
        try
        {
            switch (edmType.PrimitiveKind())
            {
                case EdmPrimitiveTypeKind.String:
                    return param;
                case EdmPrimitiveTypeKind.Int64:
                    return long.Parse(param);
                default:
                    // should never happen due to the config being validated for correct types
                    throw new NotSupportedException($"{edmType} is not supported");
            }
        }
        catch (Exception e)
        {
            if (e is FormatException ||
                e is ArgumentNullException ||
                e is OverflowException)
            {
                throw new ArgumentException($"Parameter \"{param}\" cannot be resolved as type \"{edmType}\".");
            }

            throw;
        }
    }

    /// <summary>
    /// Create the correct resultant string given left, op, right.
    /// Check for null values and then format the predicate properly.
    /// </summary>
    /// <param name="op">The binary operation</param>
    /// <param name="left">left side of the predicate</param>
    /// <param name="right">right side of the predicate</param>
    /// <returns>string representing the correct formatting.</returns>
    private static string CreateResult(BinaryOperatorKind op, string left, string right)
    {
        if (left.Equals("NULL") || right.Equals("NULL"))
        {
            return CreateNullResult(op, left, right);
        }

        return $"({left} {GetFilterPredicateOperator(op)} {right})";
    }

    /// <summary>
    /// Create the correctly formed response with NULLs.
    /// </summary>
    /// <param name="op">The binary operation</param>
    /// <param name="field">The value representing a field.</param>
    /// <returns>The correct format for a NULL given the op and left hand side.</returns>
    private static string CreateNullResult(BinaryOperatorKind op, string left, string right)
    {
        switch (op)
        {
            case BinaryOperatorKind.Equal:
                if (!left.Equals("NULL"))
                {
                    return $"({left} IS NULL)";
                }

                return $"({right} IS NULL)";
            case BinaryOperatorKind.NotEqual:
                if (!left.Equals("NULL"))
                {
                    return $"({left} IS NOT NULL)";
                }

                return $"({right} IS NOT NULL)";
            case BinaryOperatorKind.GreaterThan:
            case BinaryOperatorKind.GreaterThanOrEqual:
            case BinaryOperatorKind.LessThan:
            case BinaryOperatorKind.LessThanOrEqual:
                return $"({left} {GetFilterPredicateOperator(op)} {right})";
            default:
                throw new NotSupportedException($"{op} is not supported with {left} and {right}");
        }
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

    /// <summary>
    /// Return the correct string for the unary operator that will be a part of the filter predicates
    /// that will make up the filter of the query.
    /// </summary>
    /// <param name="op">The op we will translate.</param>
    /// <returns>The string which is a translation of the op.</returns>
    private static string GetFilterPredicateOperator(UnaryOperatorKind op)
    {
        switch (op)
        {
            case UnaryOperatorKind.Not:
                return "NOT";
            default:
                throw new ArgumentException($"Uknown Predicate Operation of {op}");
        }
    }
}
