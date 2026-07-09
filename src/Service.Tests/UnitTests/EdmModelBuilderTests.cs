// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.OData.Edm;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="EdmModelBuilder"/> which builds an OData EDM model from
    /// in-memory (mocked) database metadata (no live database).
    /// </summary>
    [TestClass]
    public class EdmModelBuilderTests
    {
        [TestMethod]
        public void BuildModel_TableEntity_CreatesEntityTypeWithKeyAndProperties()
        {
            Dictionary<string, DatabaseObject> entities = new()
            {
                ["Book"] = new DatabaseTable("dbo", "books") { SourceType = EntitySourceType.Table, TableDefinition = BuildSourceDefinition() }
            };

            IEdmModel model = new EdmModelBuilder()
                .BuildModel(BuildProvider(entities).Object)
                .GetModel();

            IEdmEntityType? entityType = model.SchemaElements.OfType<IEdmEntityType>()
                .FirstOrDefault(e => e.Name == "Book.dbo.books");

            Assert.IsNotNull(entityType, "Expected an EDM entity type for the Book table.");
            Assert.AreEqual(3, entityType!.DeclaredProperties.Count());
            Assert.AreEqual(1, entityType.DeclaredKey!.Count());
            Assert.AreEqual("id", entityType.DeclaredKey!.First().Name);
        }

        [TestMethod]
        public void BuildModel_TableEntity_AddsEntitySetToContainer()
        {
            Dictionary<string, DatabaseObject> entities = new()
            {
                ["Book"] = new DatabaseTable("dbo", "books") { SourceType = EntitySourceType.Table, TableDefinition = BuildSourceDefinition() }
            };

            IEdmModel model = new EdmModelBuilder()
                .BuildModel(BuildProvider(entities).Object)
                .GetModel();

            Assert.IsNotNull(model.EntityContainer);
            Assert.IsTrue(model.EntityContainer.EntitySets().Any(s => s.Name == "Book.dbo.books"));
        }

        [TestMethod]
        public void BuildModel_StoredProcedureEntity_IsSkipped()
        {
            Dictionary<string, DatabaseObject> entities = new()
            {
                ["GetBook"] = new DatabaseStoredProcedure("dbo", "get_book")
                {
                    SourceType = EntitySourceType.StoredProcedure,
                    StoredProcedureDefinition = new StoredProcedureDefinition()
                }
            };

            IEdmModel model = new EdmModelBuilder()
                .BuildModel(BuildProvider(entities).Object)
                .GetModel();

            Assert.IsFalse(model.SchemaElements.OfType<IEdmEntityType>().Any(),
                "Stored procedures should not be added to the EDM model.");
        }

        [TestMethod]
        public void BuildModel_LinkingEntity_IsSkipped()
        {
            Dictionary<string, DatabaseObject> entities = new()
            {
                ["BookAuthor"] = new DatabaseTable("dbo", "book_author") { SourceType = EntitySourceType.Table, TableDefinition = BuildSourceDefinition() }
            };
            Dictionary<string, Entity> linking = new() { ["BookAuthor"] = null! };

            IEdmModel model = new EdmModelBuilder()
                .BuildModel(BuildProvider(entities, linking).Object)
                .GetModel();

            Assert.IsFalse(model.SchemaElements.OfType<IEdmEntityType>().Any(),
                "Linking entities should not be added to the EDM model.");
        }

        #region Helpers

        private static SourceDefinition BuildSourceDefinition()
        {
            SourceDefinition sourceDefinition = new() { PrimaryKey = new List<string> { "id" } };
            sourceDefinition.Columns.Add("id", new ColumnDefinition { SystemType = typeof(int) });
            sourceDefinition.Columns.Add("title", new ColumnDefinition { SystemType = typeof(string) });
            sourceDefinition.Columns.Add("publisher_id", new ColumnDefinition { SystemType = typeof(int) });
            return sourceDefinition;
        }

        private static Mock<ISqlMetadataProvider> BuildProvider(
            Dictionary<string, DatabaseObject> entities,
            Dictionary<string, Entity>? linkingEntities = null)
        {
            SourceDefinition sourceDefinition = BuildSourceDefinition();

            Mock<ISqlMetadataProvider> provider = new();
            provider.Setup(x => x.GetLinkingEntities()).Returns(linkingEntities ?? new Dictionary<string, Entity>());
            provider.Setup(x => x.GetEntityNamesAndDbObjects()).Returns(entities);
            provider.Setup(x => x.GetSourceDefinition(It.IsAny<string>())).Returns(sourceDefinition);

            foreach (string column in sourceDefinition.Columns.Keys)
            {
                string exposed = column;
                provider.Setup(x => x.TryGetExposedColumnName(It.IsAny<string>(), column, out exposed)).Returns(true);
            }

            return provider;
        }

        #endregion
    }
}
