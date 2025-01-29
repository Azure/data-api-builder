// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder
{
    [TestClass]
    public class InputTypeBuilderTests
    {
        private static readonly string _numericAggregateFieldsSuffix = "NumericAggregateFields";

        [DataTestMethod]
        [DataRow("ID", "IdFilterInput")]
        [DataRow("Int", "IntFilterInput")]
        [DataRow("String", "StringFilterInput")]
        [DataRow("Float", "FloatFilterInput")]
        [DataRow("Boolean", "BooleanFilterInput")]
        public void BuiltInTypesGenerateInputFields(string fieldType, string expectedFilterName)
        {
            string gql =
    $@"
type Foo @model(name:""Foo"") {{
    id: {fieldType}!
}}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes = new();
            ObjectTypeDefinitionNode node = root.Definitions[0] as ObjectTypeDefinitionNode;
            InputTypeBuilder.GenerateFilterInputTypeForObjectType(node, inputTypes);

            Assert.AreEqual(expectedFilterName, inputTypes["FooFilterInput"].Fields.First(f => f.Name.Value == "id").Type.NamedType().Name.Value);
        }

        [TestMethod]
        public void RelationshipTypesBuildAppropriateFilterType()
        {
            string gql =
    @"
type Book @model(name:""Book"") {
    id: Int!
    publisher: Publisher! @relationship(target: ""Publisher"", cardinality: ""One"")
}

type Publisher @model(name:""Publisher"") {
    id: Int!
    books(first: Int, after: String, " + QueryBuilder.FILTER_FIELD_NAME +
    @": PublisherFilterInput): PublisherConnection @relationship(target: ""Book"", cardinality: ""Many"")
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes = new();
            foreach (ObjectTypeDefinitionNode node in root.Definitions)
            {
                InputTypeBuilder.GenerateFilterInputTypeForObjectType(node, inputTypes);
            }

            InputObjectTypeDefinitionNode publisherFilterInput = inputTypes["PublisherFilterInput"];

            CollectionAssert.AreEquivalent(new[] { "Int", "BookFilterInput", "PublisherFilterInput" }, inputTypes.Keys);
            Assert.AreEqual(4, publisherFilterInput.Fields.Count);
            Assert.IsTrue(publisherFilterInput.Fields.Any(f => f.Type.NamedType().Name.Value == "BookFilterInput"), "No field found for BookFilterInput");
        }

        /// <summary>
        /// Tests generation of numeric aggregation input types.
        /// Verifies:
        /// 1. Input type is created when numeric fields are present
        /// 2. All numeric fields (Int, Float) are included in the input type
        /// 3. Non-numeric fields are excluded
        /// 4. Field names are preserved in the generated input type
        [TestMethod]
        [TestCategory("Input Type Builder - Numeric Aggregation")]
        public void GenerateAggregationNumericInput_WithNumericFields_CreatesInputType()
        {
            string gql = @"
type Foo @model(name:""Foo"") {
    id: ID!
    count: Int!
    price: Float!
    rating: Float
}";

            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes = new();
            PopulateInputTypesForAggregation(gql, inputTypes);

            string expectedInputName = $"Foo{_numericAggregateFieldsSuffix}";
            Assert.IsTrue(inputTypes.ContainsKey(expectedInputName), "Numeric aggregation input type should be created");

            InputObjectTypeDefinitionNode numericInput = inputTypes[expectedInputName];
            Assert.AreEqual(3, numericInput.Fields.Count, "Should have fields for count, price, and rating");

            // Verify field names are present
            HashSet<string> fieldNames = new(numericInput.Fields.Select(f => f.Name.Value));
            Assert.IsTrue(fieldNames.Contains("count"));
            Assert.IsTrue(fieldNames.Contains("price"));
            Assert.IsTrue(fieldNames.Contains("rating"));
        }

        /// <summary>
        /// Tests that no numeric aggregation input type is created when entity has no numeric fields.
        /// Verifies:
        /// 1. No input type is generated for entities with only non-numeric fields
        /// 2. ID fields are not considered for numeric aggregation
        /// </summary>
        [TestMethod]
        [TestCategory("Input Type Builder - Numeric Aggregation")]
        public void GenerateAggregationNumericInput_WithNoNumericFields_DoesNotCreateInputType()
        {
            string gql = @"
type Foo @model(name:""Foo"") {
    id: ID!
    name: String!
    isActive: Boolean
}";

            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes = new();
            PopulateInputTypesForAggregation(gql, inputTypes);

            string expectedInputName = $"Foo{_numericAggregateFieldsSuffix}";
            Assert.IsFalse(inputTypes.ContainsKey(expectedInputName), "No numeric aggregation input type should be created");
        }

        /// <summary>
        /// Tests that numeric aggregation input preserves the original field types.
        /// Verifies:
        /// 1. Different numeric types (Int, Float, Decimal) are preserved in the input type
        /// 2. Field nullability is preserved
        /// 3. All numeric fields are included regardless of type
        /// </summary>
        [TestMethod]
        [TestCategory("Input Type Builder - Numeric Aggregation")]
        public void GenerateAggregationNumericInput_PreservesFieldTypes()
        {
            string gql = @"
type Foo @model(name:""Foo"") {
    intField: Int!
    floatField: Float
    decimalField: Decimal!
}";
            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes = new();
            PopulateInputTypesForAggregation(gql, inputTypes);

            string expectedInputName = $"Foo{_numericAggregateFieldsSuffix}";
            InputObjectTypeDefinitionNode numericInput = inputTypes[expectedInputName];

            // Verify field types are preserved
            InputValueDefinitionNode intField = numericInput.Fields.First(f => f.Name.Value == "intField");
            InputValueDefinitionNode floatField = numericInput.Fields.First(f => f.Name.Value == "floatField");
            InputValueDefinitionNode decimalField = numericInput.Fields.First(f => f.Name.Value == "decimalField");

            Assert.AreEqual("Int!", intField.Type.ToString());
            Assert.AreEqual("Float", floatField.Type.ToString());
            Assert.AreEqual("Decimal!", decimalField.Type.ToString());
        }

        private static void PopulateInputTypesForAggregation(string gql, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes)
        {
            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            ObjectTypeDefinitionNode node = root.Definitions[0] as ObjectTypeDefinitionNode;
            InputTypeBuilder.GenerateAggregationNumericInputForObjectType(node, inputTypes);
        }
    }
}
