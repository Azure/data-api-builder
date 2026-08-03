// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Helpers;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder
{
    /// <summary>
    /// Unit tests for the pure naming helpers in <see cref="GraphQLNaming"/>.
    /// </summary>
    [TestClass]
    public class GraphQLNamingTests
    {
        [DataTestMethod]
        [DataRow("Book", "Book", DisplayName = "Valid name is unchanged")]
        [DataRow("1Book", "Book", DisplayName = "Illegal leading digit is stripped")]
        [DataRow("_Book", "Book", DisplayName = "Illegal leading underscore is stripped")]
        [DataRow("Bo-ok", "Book", DisplayName = "Invalid symbol removed")]
        public void SanitizeGraphQLName_SingleSegment(string input, string expected)
        {
            string[] segments = GraphQLNaming.SanitizeGraphQLName(input);
            Assert.AreEqual(1, segments.Length);
            Assert.AreEqual(expected, segments[0]);
        }

        [TestMethod]
        public void SanitizeGraphQLName_RemovesSpacesAndInvalidSymbols()
        {
            // Spaces and non-alphanumeric symbols are removed, yielding a single segment.
            string[] segments = GraphQLNaming.SanitizeGraphQLName("my table!");
            CollectionAssert.AreEqual(new[] { "mytable" }, segments);
        }

        [DataTestMethod]
        [DataRow("Book", false)]
        [DataRow("book", false)]
        [DataRow("1book", true)]
        [DataRow("_book", true)]
        public void ViolatesNamePrefixRequirements(string name, bool expected)
        {
            Assert.AreEqual(expected, GraphQLNaming.ViolatesNamePrefixRequirements(name));
        }

        [DataTestMethod]
        [DataRow("Book", false)]
        [DataRow("Book_1", false)]
        [DataRow("Bo-ok", true)]
        [DataRow("my table", true)]
        public void ViolatesNameRequirements(string name, bool expected)
        {
            Assert.AreEqual(expected, GraphQLNaming.ViolatesNameRequirements(name));
        }

        [DataTestMethod]
        [DataRow("__type", true)]
        [DataRow("__schema", true)]
        [DataRow("type", false)]
        [DataRow("_type", false)]
        public void IsIntrospectionField(string fieldName, bool expected)
        {
            Assert.AreEqual(expected, GraphQLNaming.IsIntrospectionField(fieldName));
        }

        [TestMethod]
        public void GetDefinedSingularName_WithSingular_ReturnsSingular()
        {
            Entity entity = GraphQLTestHelpers.GenerateEntityWithSingularPlural("Book", "Books");
            Assert.AreEqual("Book", GraphQLNaming.GetDefinedSingularName("Book", entity));
        }

        [TestMethod]
        public void GetDefinedSingularName_WithoutSingular_Throws()
        {
            Entity entity = GraphQLTestHelpers.GenerateEmptyEntity();
            Assert.ThrowsException<System.ArgumentException>(
                () => GraphQLNaming.GetDefinedSingularName("Book", entity));
        }

        [TestMethod]
        public void GetDefinedPluralName_WithPlural_ReturnsPlural()
        {
            Entity entity = GraphQLTestHelpers.GenerateEntityWithSingularPlural("Book", "Books");
            Assert.AreEqual("Books", GraphQLNaming.GetDefinedPluralName("Book", entity));
        }

        [TestMethod]
        public void GetDefinedPluralName_WithoutPlural_Throws()
        {
            Entity entity = GraphQLTestHelpers.GenerateEntityWithStringType("Book");
            Assert.ThrowsException<System.ArgumentException>(
                () => GraphQLNaming.GetDefinedPluralName("Book", entity));
        }

        [DataTestMethod]
        [DataRow("Book", "book", DisplayName = "First char lowercased")]
        [DataRow("my table", "mytable", DisplayName = "Spaces removed, first char lowercased")]
        [DataRow("book", "book", DisplayName = "Already camel-case unchanged")]
        public void FormatNameForField(string input, string expected)
        {
            Assert.AreEqual(expected, GraphQLNaming.FormatNameForField(input));
        }

        [TestMethod]
        public void Pluralize_ReturnsConfiguredPluralName()
        {
            Entity entity = GraphQLTestHelpers.GenerateEntityWithSingularPlural("Book", "Books");
            NameNode result = GraphQLNaming.Pluralize("Book", entity);
            Assert.AreEqual("Books", result.Value);
        }

        [TestMethod]
        public void ObjectTypeToEntityName_NoModelDirective_ReturnsTypeName()
        {
            ObjectTypeDefinitionNode node = ParseObjectType("type Book { id: Int }");
            Assert.AreEqual("Book", GraphQLNaming.ObjectTypeToEntityName(node));
        }

        [TestMethod]
        public void ObjectTypeToEntityName_ModelDirectiveWithName_ReturnsModelName()
        {
            ObjectTypeDefinitionNode node = ParseObjectType(@"type Book @model(name: ""TopLevelBook"") { id: Int }");
            Assert.AreEqual("TopLevelBook", GraphQLNaming.ObjectTypeToEntityName(node));
        }

        [TestMethod]
        public void ObjectTypeToEntityName_ModelDirectiveNoArguments_ReturnsTypeName()
        {
            ObjectTypeDefinitionNode node = ParseObjectType("type Book @model { id: Int }");
            Assert.AreEqual("Book", GraphQLNaming.ObjectTypeToEntityName(node));
        }

        [TestMethod]
        public void GenerateByPKQueryName_UsesSingularWithSuffix()
        {
            Entity entity = GraphQLTestHelpers.GenerateEntityWithSingularPlural("Book", "Books");
            Assert.AreEqual("book_by_pk", GraphQLNaming.GenerateByPKQueryName("Book", entity));
        }

        [TestMethod]
        public void GenerateListQueryName_UsesPlural()
        {
            Entity entity = GraphQLTestHelpers.GenerateEntityWithSingularPlural("Book", "Books");
            Assert.AreEqual("books", GraphQLNaming.GenerateListQueryName("Book", entity));
        }

        [TestMethod]
        public void GenerateStoredProcedureGraphQLFieldName_PrefixesExecute()
        {
            Entity entity = GraphQLTestHelpers.GenerateEntityWithStringType("GetBook");
            Assert.AreEqual("executeGetBook", GraphQLNaming.GenerateStoredProcedureGraphQLFieldName("GetBook", entity));
        }

        [TestMethod]
        public void GenerateLinkingNodeName_ConcatenatesWithPrefix()
        {
            Assert.AreEqual("linkingObjectBookAuthor", GraphQLNaming.GenerateLinkingNodeName("Book", "Author"));
        }

        private static ObjectTypeDefinitionNode ParseObjectType(string sdl)
        {
            DocumentNode document = Utf8GraphQLParser.Parse(sdl);
            return (ObjectTypeDefinitionNode)document.Definitions[0];
        }
    }
}
