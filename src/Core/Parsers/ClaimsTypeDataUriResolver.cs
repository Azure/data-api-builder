// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Core.Parsers
{
    /// <summary>
    /// Custom OData Resolver which attempts to assist with processing resolved token claims
    /// within an authorization policy string that will be used to create an OData filter clause.
    /// This resolver's type coercion is meant to be utilized for authorization policy processing
    /// and NOT URL query string processing.
    /// </summary>
    /// <seealso cref="https://devblogs.microsoft.com/odata/tutorial-sample-odatauriparser-extension-support/#write-customized-extensions-from-scratch"/>
    public class ClaimsTypeDataUriResolver : ODataUriResolver
    {
        private readonly IReadOnlyDictionary<string, SingleValueNode> _claimValueNodes;

        public ClaimsTypeDataUriResolver(IReadOnlyDictionary<string, SingleValueNode>? claimValueNodes = null)
        {
            _claimValueNodes = claimValueNodes ?? new Dictionary<string, SingleValueNode>();
        }

        /// <summary>
        /// Between two nodes in the filter clause, determine the:
        /// - PrimaryOperand: Node representing an OData EDM model object and has Kind == QueryNodeKind.SingleValuePropertyAccess.
        /// - OperandToConvert: Node representing a constant value and has kind QueryNodeKind.Constant.
        /// This resolver will overwrite the OperandToConvert node to a new ConstantNode where the value type is that of the PrimaryOperand node.
        /// </summary>
        /// <param name="binaryOperatorKind">the operator kind</param>
        /// <param name="leftNode">the left operand</param>
        /// <param name="rightNode">the right operand</param>
        /// <param name="typeReference">type reference for the result BinaryOperatorNode.</param>
        public override void PromoteBinaryOperandTypes(BinaryOperatorKind binaryOperatorKind, ref SingleValueNode leftNode, ref SingleValueNode rightNode, out IEdmTypeReference typeReference)
        {
            ResolveClaimAlias(ref leftNode);
            ResolveClaimAlias(ref rightNode);

            EdmPrimitiveTypeKind? leftPrimitiveKind = leftNode.TypeReference?.PrimitiveKind();
            EdmPrimitiveTypeKind? rightPrimitiveKind = rightNode.TypeReference?.PrimitiveKind();

            if (leftPrimitiveKind != rightPrimitiveKind)
            {
                if (leftPrimitiveKind.HasValue &&
                    leftNode.Kind == QueryNodeKind.SingleValuePropertyAccess &&
                    rightNode is ConstantNode)
                {
                    TryConvertNodeToTargetType(
                        targetType: leftPrimitiveKind.Value,
                        operandToConvert: ref rightNode
                        );
                }
                else if (rightPrimitiveKind.HasValue &&
                    rightNode.Kind == QueryNodeKind.SingleValuePropertyAccess &&
                    leftNode is ConstantNode)
                {
                    TryConvertNodeToTargetType(
                        targetType: rightPrimitiveKind.Value,
                        operandToConvert: ref leftNode
                        );
                }
            }

            base.PromoteBinaryOperandTypes(binaryOperatorKind, ref leftNode, ref rightNode, out typeReference);
        }

        /// <summary>
        /// Replaces a policy parameter alias with its typed claim constant before OData
        /// performs type promotion. The claim value therefore never enters URI text.
        /// </summary>
        private void ResolveClaimAlias(ref SingleValueNode node)
        {
            if (node is ParameterAliasNode aliasNode &&
                _claimValueNodes.TryGetValue(aliasNode.Alias, out SingleValueNode? claimValueNode))
            {
                node = claimValueNode;
            }
        }

        /// <summary>
        /// Uses type specific parsers to attempt converting the supplied node to a new ConstantNode of type targetType
        /// when the supplied node's type differs from the target's type.
        /// When no explicit conversion is provided, no conversion occurs here and the conversion attempt is deferred
        /// to base.PromoteBinaryOperandTypes().
        /// </summary>
        /// <param name="targetType">Primitive type (string, bool, int, etc.) of the primary node's value.</param>
        /// <param name="operandToConvert">Node representing a constant value which should be converted to a ConstantNode of type targetType.</param>
        private static void TryConvertNodeToTargetType(EdmPrimitiveTypeKind targetType, ref SingleValueNode operandToConvert)
        {
            ConstantNode? preConvertedConstant = operandToConvert as ConstantNode;

            if (preConvertedConstant?.Value is null)
            {
                return;
            }

            if (targetType == EdmPrimitiveTypeKind.Int32)
            {
                if (int.TryParse(preConvertedConstant.Value.ToString(), out int result))
                {
                    operandToConvert = new ConstantNode(constantValue: result);
                }
            }
            else if (targetType == EdmPrimitiveTypeKind.String)
            {
                string? objectValue = preConvertedConstant.Value.ToString();
                if (objectValue is not null)
                {
                    operandToConvert = new ConstantNode(constantValue: objectValue);
                }
            }
            else if (targetType == EdmPrimitiveTypeKind.Boolean)
            {
                if (bool.TryParse(preConvertedConstant.Value.ToString(), out bool result))
                {
                    operandToConvert = new ConstantNode(constantValue: result);
                }
            }
            else if (targetType == EdmPrimitiveTypeKind.Guid)
            {
                if (Guid.TryParse(preConvertedConstant.Value.ToString(), out Guid result))
                {
                    operandToConvert = new ConstantNode(constantValue: result);
                }
            }
        }
    }
}
