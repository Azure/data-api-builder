using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using System;

namespace Azure.DataApiBuilder.Service.Parsers
{
    /// <summary>
    /// Custom OData Resolver which attempts to assist with processing resolved claims in the filter clause.
    /// </summary>
    /// <seealso cref="https://devblogs.microsoft.com/odata/tutorial-sample-odatauriparser-extension-support/#write-customized-extensions-from-scratch"/>
    public class ClaimsTypeDataUriResolver : ODataUriResolver
    {
        /// <summary>
        /// Between two nodes in the filter clause, determine which is the node representing an EDM model field (QueryNodeKind.SingleValuePropertyAccess)
        /// and which node represents the constant likely resolved from access token claims (QueryNodeKind.Constant).
        /// Overwrites the node from token claims with a new ConstantNode containing the original value cast as the type of the EDM Model field node.
        /// </summary>
        /// <param name="binaryOperatorKind">the operator kind</param>
        /// <param name="leftNode">the left operand</param>
        /// <param name="rightNode">the right operand</param>
        /// <param name="typeReference">type reference for the result BinaryOperatorNode.</param>
        public override void PromoteBinaryOperandTypes(BinaryOperatorKind binaryOperatorKind, ref SingleValueNode leftNode, ref SingleValueNode rightNode, out IEdmTypeReference typeReference)
        {
            // Check for string instances of @claims. directive and try to cast to the type on the other side of the operator.
            if (leftNode.TypeReference.PrimitiveKind() != rightNode.TypeReference.PrimitiveKind())
            {
                if ((leftNode.Kind == QueryNodeKind.SingleValuePropertyAccess) && (rightNode is ConstantNode))
                {
                    TryConvertNodeToTargetType(primaryOperand: ref leftNode, operandToConvert: ref rightNode);
                }
                else if (rightNode.Kind == QueryNodeKind.SingleValuePropertyAccess && leftNode is ConstantNode)
                {
                    TryConvertNodeToTargetType(primaryOperand: ref rightNode, operandToConvert: ref leftNode);
                }
            }

            base.PromoteBinaryOperandTypes(binaryOperatorKind, ref leftNode, ref rightNode, out typeReference);
        }

        /// <summary>
        /// Helper class which tries to convert the operandToConvert node's value to the value type of
        /// the primaryOperand node.
        /// </summary>
        /// <param name="primaryOperand">Node representing the EDM Model field</param>
        /// <param name="operandToConvert">Node representing a constant value (claims resolved node).</param>
        private static void TryConvertNodeToTargetType(ref SingleValueNode primaryOperand, ref SingleValueNode operandToConvert)
        {
            ConstantNode operandToConvertAsConstant = (ConstantNode)operandToConvert;

            if (primaryOperand.TypeReference.PrimitiveKind() == EdmPrimitiveTypeKind.Int32)
            {
                string? objectValue = operandToConvertAsConstant.Value.ToString();
                if (objectValue is not null && Int32.TryParse(objectValue, out int result))
                {
                    operandToConvert = new ConstantNode(constantValue: result);
                }
            }
            else if (primaryOperand.TypeReference.PrimitiveKind() == EdmPrimitiveTypeKind.String)
            {
                string? objectValue = operandToConvertAsConstant.Value.ToString();
                if (objectValue is not null)
                {
                    operandToConvert = new ConstantNode(constantValue: objectValue);
                }
            }
            else if (primaryOperand.TypeReference.PrimitiveKind() == EdmPrimitiveTypeKind.Boolean)
            {
                if (Boolean.TryParse(operandToConvertAsConstant.Value.ToString(), out bool result))
                {
                    operandToConvert = new ConstantNode(constantValue: result);
                }
            }
        }
    }
}
