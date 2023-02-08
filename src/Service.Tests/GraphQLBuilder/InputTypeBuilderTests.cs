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
    }
}
