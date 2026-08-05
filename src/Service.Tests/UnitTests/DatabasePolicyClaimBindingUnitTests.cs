// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Core.Parsers;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Tests that database policy claim values are bound as typed AST constants and
    /// never interpreted as URI/OData syntax.
    /// </summary>
    [TestClass]
    public class DatabasePolicyClaimBindingUnitTests
    {
        /// <summary>
        /// Verifies that attacker-influenced claim values never enter the policy URI. The OData
        /// parser must see one equality expression whose right operand is the original claim value,
        /// regardless of percent encoding, and Cosmos DB must receive that value as a parameter.
        /// </summary>
        [DataTestMethod]
        [DataRow("alice%27 or 1 eq 1 or %27", DisplayName = "Percent-encoded quote")]
        [DataRow("alice%2527 or 1 eq 1 or %2527", DisplayName = "Double-encoded quote")]
        [DataRow("alice%252527 or 1 eq 1 or %27", DisplayName = "Mixed nested encodings")]
        [DataRow("alice' or 1 eq 1 or '", DisplayName = "Literal quote")]
        [DataRow("50% complete", DisplayName = "Legitimate percent character")]
        public void DatabasePolicyClaimValue_IsTypedAstConstantAndCosmosParameter(string claimValue)
        {
            const string claimAlias = "@dabClaim0";
            Dictionary<string, SingleValueNode> claimValueNodes = new()
            {
                [claimAlias] = new ConstantNode(claimValue)
            };

            ODataUriParser parser = new(
                BuildModel(),
                new Uri($"Entities/?$filter=name eq {claimAlias}", UriKind.Relative))
            {
                Resolver = new ClaimsTypeDataUriResolver(claimValueNodes)
            };

            foreach ((string alias, SingleValueNode valueNode) in claimValueNodes)
            {
                parser.ParameterAliasNodes.Add(alias, valueNode);
            }

            FilterClause clause = parser.ParseFilter();
            BinaryOperatorNode comparison = (BinaryOperatorNode)clause.Expression;
            Assert.AreEqual(BinaryOperatorKind.Equal, comparison.OperatorKind);
            Assert.IsInstanceOfType<ConstantNode>(comparison.Right);
            Assert.AreEqual(claimValue, ((ConstantNode)comparison.Right).Value);

            List<object?> parameterValues = new();
            string predicate = clause.Expression.Accept(new ODataASTCosmosVisitor(
                "c",
                value =>
                {
                    parameterValues.Add(value);
                    return "@param1";
                }));

            Assert.AreEqual("(c.name = @param1)", predicate);
            CollectionAssert.AreEqual(new object?[] { claimValue }, parameterValues);
        }

        private static IEdmModel BuildModel()
        {
            EdmModel model = new();
            EdmEntityType entityType = new("Dab", "Entity");
            EdmStructuralProperty id = entityType.AddStructuralProperty("id", EdmPrimitiveTypeKind.Int32, false);
            entityType.AddKeys(id);
            entityType.AddStructuralProperty("name", EdmPrimitiveTypeKind.String, true);
            model.AddElement(entityType);

            EdmEntityContainer container = new("Dab", "Container");
            container.AddEntitySet("Entities", entityType);
            model.AddElement(container);
            return model;
        }
    }
}
