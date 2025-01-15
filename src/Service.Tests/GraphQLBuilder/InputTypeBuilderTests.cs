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

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes = new();
            ObjectTypeDefinitionNode node = root.Definitions[0] as ObjectTypeDefinitionNode;

            InputTypeBuilder.GenerateInputTypesForObjectType(node, inputTypes);

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

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes = new();
            ObjectTypeDefinitionNode node = root.Definitions[0] as ObjectTypeDefinitionNode;

            InputTypeBuilder.GenerateInputTypesForObjectType(node, inputTypes);

            string expectedInputName = $"Foo{_numericAggregateFieldsSuffix}";
            Assert.IsFalse(inputTypes.ContainsKey(expectedInputName), "No numeric aggregation input type should be created");
        }

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

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes = new();
            ObjectTypeDefinitionNode node = root.Definitions[0] as ObjectTypeDefinitionNode;

            InputTypeBuilder.GenerateInputTypesForObjectType(node, inputTypes);

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
    }
}
