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
    public class EnumTypeBuilderTests
    {
        private static readonly string _numericAggregateFieldsSuffix = "NumericAggregateFields";
        private static readonly string _scalarFieldsSuffix = "ScalarFields";

        /// <summary>
        /// Tests generation of numeric aggregation enum types.
        /// Verifies:
        /// 1. Enum type is created when numeric fields are present
        /// 2. All numeric fields (Int, Float) are included in the enum type
        /// 3. Non-numeric fields are excluded
        /// 4. Field names are preserved in the generated enum type
        /// </summary>
        [TestMethod]
        [TestCategory("Enum Type Builder - Numeric Aggregation")]
        public void GenerateAggregationNumericEnum_WithNumericFields_CreatesEnumType()
        {
            string gql = @"
type Foo @model(name:""Foo"") {
    id: ID!
    count: Int!
    price: Float!
    rating: Float
}";

            Dictionary<string, EnumTypeDefinitionNode> enumTypes = new();
            PopulateEnumTypesForAggregation(gql, enumTypes);

            string expectedEnumName = $"Foo{_numericAggregateFieldsSuffix}";
            Assert.IsTrue(enumTypes.ContainsKey(expectedEnumName), "Numeric aggregation enum type should be created");

            EnumTypeDefinitionNode numericEnum = enumTypes[expectedEnumName];
            Assert.AreEqual(3, numericEnum.Values.Count, "Should have enum values for count, price, and rating");

            // Verify field names are present
            HashSet<string> fieldNames = new(numericEnum.Values.Select(f => f.Name.Value));
            Assert.IsTrue(fieldNames.Contains("count"));
            Assert.IsTrue(fieldNames.Contains("price"));
            Assert.IsTrue(fieldNames.Contains("rating"));
        }

        /// <summary>
        /// Tests that no numeric aggregation enum type is created when entity has no numeric fields.
        /// Verifies:
        /// 1. No enum type is generated for entities with only non-numeric fields
        /// 2. ID fields are not considered for numeric aggregation
        /// </summary>
        [TestMethod]
        [TestCategory("Enum Type Builder - Numeric Aggregation")]
        public void GenerateAggregationNumericEnum_WithNoNumericFields_DoesNotCreateEnumType()
        {
            string gql = @"
type Foo @model(name:""Foo"") {
    id: ID!
    name: String!
    isActive: Boolean
}";

            Dictionary<string, EnumTypeDefinitionNode> enumTypes = new();
            PopulateEnumTypesForAggregation(gql, enumTypes);

            string expectedEnumName = $"Foo{_numericAggregateFieldsSuffix}";
            Assert.IsFalse(enumTypes.ContainsKey(expectedEnumName), "No numeric aggregation enum type should be created");
        }

        /// <summary>
        /// Tests generation of scalar fields enum types.
        /// Verifies:
        /// 1. Enum type is created when scalar fields are present
        /// 2. All scalar fields (Int, Float, String, Boolean, ID) are included in the enum type
        /// 3. Non-scalar fields are excluded
        /// 4. Field names are preserved in the generated enum type
        /// </summary>
        [TestMethod]
        [TestCategory("Enum Type Builder - Scalar Fields")]
        public void GenerateScalarFieldsEnum_WithScalarFields_CreatesEnumType()
        {
            string gql = @"
type Foo @model(name:""Foo"") {
    id: ID!
    name: String!
    count: Int!
    price: Float!
    isActive: Boolean
    related: Bar
}";

            Dictionary<string, EnumTypeDefinitionNode> enumTypes = new();
            PopulateScalarFieldsEnum(gql, enumTypes);

            string expectedEnumName = $"Foo{_scalarFieldsSuffix}";
            Assert.IsTrue(enumTypes.ContainsKey(expectedEnumName), "Scalar fields enum type should be created");

            EnumTypeDefinitionNode scalarEnum = enumTypes[expectedEnumName];
            Assert.AreEqual(5, scalarEnum.Values.Count, "Should have enum values for id, name, count, price, and isActive");

            // Verify field names are present
            HashSet<string> fieldNames = new(scalarEnum.Values.Select(f => f.Name.Value));
            Assert.IsTrue(fieldNames.Contains("id"));
            Assert.IsTrue(fieldNames.Contains("name"));
            Assert.IsTrue(fieldNames.Contains("count"));
            Assert.IsTrue(fieldNames.Contains("price"));
            Assert.IsTrue(fieldNames.Contains("isActive"));
            Assert.IsFalse(fieldNames.Contains("related"), "Non-scalar field should not be included");
        }

        /// <summary>
        /// Tests that no scalar fields enum type is created when entity has no scalar fields.
        /// </summary>
        [TestMethod]
        [TestCategory("Enum Type Builder - Scalar Fields")]
        public void GenerateScalarFieldsEnum_WithNoScalarFields_DoesNotCreateEnumType()
        {
            string gql = @"
type Foo @model(name:""Foo"") {
    related: Bar
    another: Baz
}";

            Dictionary<string, EnumTypeDefinitionNode> enumTypes = new();
            PopulateScalarFieldsEnum(gql, enumTypes);

            string expectedEnumName = $"Foo{_scalarFieldsSuffix}";
            Assert.IsFalse(enumTypes.ContainsKey(expectedEnumName), "No scalar fields enum type should be created");
        }

        private static void PopulateEnumTypesForAggregation(string gql, Dictionary<string, EnumTypeDefinitionNode> enumTypes)
        {
            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            ObjectTypeDefinitionNode node = root.Definitions[0] as ObjectTypeDefinitionNode;
            EnumTypeBuilder.GenerateAggregationNumericEnumForObjectType(node, enumTypes);
        }

        private static void PopulateScalarFieldsEnum(string gql, Dictionary<string, EnumTypeDefinitionNode> enumTypes)
        {
            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            ObjectTypeDefinitionNode node = root.Definitions[0] as ObjectTypeDefinitionNode;
            EnumTypeBuilder.GenerateScalarFieldsEnumForObjectType(node, enumTypes);
        }
    }
}
