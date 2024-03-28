// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Core.Parsers
{
    /// <summary>
    /// This class is a visitor for an AST generated when parsing a $filter query string
    /// with the OData Uri Parser.
    /// </summary>
    public class ODataASTVisitor : QueryNodeVisitor<string>
    {
        private readonly BaseSqlQueryStructure _struct;
        private readonly ISqlMetadataProvider _metadataProvider;
        private readonly EntityActionOperation _operation;

        public ODataASTVisitor(BaseSqlQueryStructure structure, ISqlMetadataProvider metadataProvider, EntityActionOperation operation = EntityActionOperation.None)
        {
            _struct = structure;
            _metadataProvider = metadataProvider;
            _operation = operation;
        }

        /// <summary>
        /// Represents visiting a BinaryOperatorNode, which will hold either
        /// a Predicate operation (eq, gt, lt, etc), or a Logical operation (And, Or).
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        /// <returns>String concatenation of (left op right).</returns>
        public override string Visit(BinaryOperatorNode nodeIn)
        {
            // In order traversal but add parens to maintain order of logical operations
            string left = nodeIn.Left.Accept(this);
            string right = nodeIn.Right.Accept(this);

            if (IsSimpleBinaryExpression(nodeIn))
            {
                // Whenever we encounter a simple binary expression like "@item.name ne 'DAB'", we know that we would have just added a parameter for 'name',
                // and are coming back to the root node after traversing the left and right child.
                // Thats when we need to populate the DbType for the parameter.
                PopulateDbTypeForProperty(nodeIn);
            }

            return CreateResult(nodeIn.OperatorKind, left, right);
        }

        /// <summary>
        /// Represents visiting a UnaryNode, which is what holds unary
        /// operators such as NOT.
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        /// <returns>String concatenation of (op children)</returns>
        public override string Visit(UnaryOperatorNode nodeIn)
        {
            string child = nodeIn.Operand.Accept(this);
            return $"({GetFilterPredicateOperator(nodeIn.OperatorKind)} {child} )";
        }

        /// <summary>
        /// Represents visiting a SingleValuePropertyAccessNode, which is what
        /// holds an exposed field name in the AST. We return the value associated with
        /// the name in the aliasings so that we have the backing column even
        /// in the case where an alias is used in the request.
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        /// <returns>String representing the Field name</returns>
        public override string Visit(SingleValuePropertyAccessNode nodeIn)
        {
            _metadataProvider.TryGetBackingColumn(_struct.EntityName, nodeIn.Property.Name, out string? backingColumnName);
            if (_operation is EntityActionOperation.Create)
            {
                _struct.FieldsReferencedInDbPolicyForCreateAction.Add(backingColumnName!);
            }

            return _metadataProvider.GetQueryBuilder().QuoteIdentifier(backingColumnName!);
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

            return $"{_struct.MakeDbConnectionParam(GetParamWithSystemType(nodeIn.Value.ToString()!, nodeIn.TypeReference))}";
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
                    case EdmPrimitiveTypeKind.Guid:
                        return Guid.Parse(param);
                    case EdmPrimitiveTypeKind.Byte:
                        return byte.Parse(param);
                    case EdmPrimitiveTypeKind.Int16:
                        return short.Parse(param);
                    case EdmPrimitiveTypeKind.Int32:
                        return int.Parse(param);
                    case EdmPrimitiveTypeKind.Int64:
                        return long.Parse(param);
                    case EdmPrimitiveTypeKind.Single:
                        return float.Parse(param);
                    case EdmPrimitiveTypeKind.Double:
                        return double.Parse(param);
                    case EdmPrimitiveTypeKind.Decimal:
                        return decimal.Parse(param);
                    case EdmPrimitiveTypeKind.Boolean:
                        return bool.Parse(param);
                    case EdmPrimitiveTypeKind.Date:
                        return Date.Parse(param);
                    case EdmPrimitiveTypeKind.DateTimeOffset:
                        return DateTimeOffset.Parse(param);
                    case EdmPrimitiveTypeKind.String:
                        return param;
                    case EdmPrimitiveTypeKind.TimeOfDay:
                        return TimeOnly.Parse(param);
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

        /// <summary>
        /// Helper method to populate the DbType for the property referenced in the OData filter.
        /// Since the nodes are processed in a postorder fashion, the current BinaryOperatorNode has already been processed,
        /// and the parameter is already created for the property.
        /// We just need to populate the DbType.
        /// </summary>
        /// <param name="nodeIn">Binary operator node<</param>
        private void PopulateDbTypeForProperty(BinaryOperatorNode nodeIn)
        {
            SingleValuePropertyAccessNode propertyNode = nodeIn.Left.GetType() == typeof(SingleValuePropertyAccessNode) ?
                    (SingleValuePropertyAccessNode)nodeIn.Left : (SingleValuePropertyAccessNode)nodeIn.Right;
            string? paramName = BaseQueryStructure.GetEncodedParamName(_struct.Counter.Current() - 1);
            _metadataProvider.TryGetBackingColumn(_struct.EntityName, propertyNode.Property.Name, out string? backingColumnName);
            _struct.Parameters[paramName].DbType = _struct.GetUnderlyingSourceDefinition().Columns[backingColumnName!].DbType;
            _struct.Parameters[paramName].SqlDbType = _struct.GetUnderlyingSourceDefinition().Columns[backingColumnName!].SqlDbType;
        }

        /// <summary>
        /// Helper method to determine if the BinaryOperatorNode represents a simple binary expression -
        /// SingleValuePropertyAccessNode followed by ConstantNode or vice versa.
        /// Eg. id gt 5, 'DAB' ne name, etc.
        /// </summary>
        /// <param name="nodeIn">Binary operator node</param>
        /// <returns>Whether BinaryOperatorNode represents a simple binary expression.</returns>
        private static bool IsSimpleBinaryExpression(BinaryOperatorNode nodeIn)
        {
            return nodeIn.Left.GetType() == typeof(SingleValuePropertyAccessNode) && nodeIn.Right.GetType() == typeof(ConstantNode) ||
                nodeIn.Left.GetType() == typeof(ConstantNode) && nodeIn.Right.GetType() == typeof(SingleValuePropertyAccessNode);
        }
    }
}
