using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.GraphQLBuilder
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
type Foo @model {{
    id: {fieldType}!
}}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes = new();
            ObjectTypeDefinitionNode node = root.Definitions[0] as ObjectTypeDefinitionNode;
            InputTypeBuilder.GenerateInputTypeForObjectType(node, inputTypes);

            Assert.AreEqual(expectedFilterName, inputTypes["Foo"].Fields.First(f => f.Name.Value == "id").Type.NamedType().Name.Value);
        }

        [TestMethod]
        public void RelationshipTypesBuildAppropriateFilterType()
        {
            string gql =
    @"
type Book @model {
    id: Int!
    publisher: Publisher! @relationship(target: ""Publisher"", cardinality: ""One"")
}

type Publisher @model {
    id: Int!
    books(first: Int, after: String, _filter: PublisherFilterInput): PublisherConnection @relationship(target: ""Book"", cardinality: ""Many"")
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes = new();
            foreach (ObjectTypeDefinitionNode node in root.Definitions)
            {
                InputTypeBuilder.GenerateInputTypeForObjectType(node, inputTypes);
            }

            InputObjectTypeDefinitionNode publisherFilterInput = inputTypes["Publisher"];

            CollectionAssert.AreEquivalent(new[] { "Int", "Book", "Publisher" }, inputTypes.Keys);
            Assert.AreEqual(4, publisherFilterInput.Fields.Count);
            Assert.IsTrue(publisherFilterInput.Fields.Any(f => f.Type.NamedType().Name.Value == "BookFilterInput"), "No field found for BookFilterInput");
        }
    }
}
