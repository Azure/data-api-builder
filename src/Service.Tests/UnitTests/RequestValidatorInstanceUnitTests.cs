// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the instance-level validation methods on <see cref="RequestValidator"/>
    /// that rely on database metadata (mocked in-memory, no live database).
    /// </summary>
    [TestClass]
    public class RequestValidatorInstanceUnitTests
    {
        private const string ENTITY_NAME = "entity";

        #region CheckFirstValidity (static)

        [DataTestMethod]
        [DataRow("5", 5)]
        [DataRow("1", 1)]
        [DataRow("-1", -1)]
        public void CheckFirstValidity_Valid_ReturnsValue(string first, int expected)
        {
            Assert.AreEqual(expected, RequestValidator.CheckFirstValidity(first));
        }

        [DataTestMethod]
        [DataRow("0")]
        [DataRow("-2")]
        [DataRow("abc")]
        [DataRow("")]
        public void CheckFirstValidity_Invalid_Throws(string first)
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => RequestValidator.CheckFirstValidity(first));
            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        #endregion

        #region ValidateRequestContext

        [TestMethod]
        public void ValidateRequestContext_ValidField_DoesNotThrow()
        {
            Mock<ISqlMetadataProvider> provider = BuildTableProvider();
            RequestValidator validator = CreateValidator(provider);

            FindRequestContext context = new(ENTITY_NAME, GetTableDbo(), isList: false)
            {
                FieldsToBeReturned = new List<string> { "title" }
            };

            validator.ValidateRequestContext(context);
        }

        [TestMethod]
        public void ValidateRequestContext_InvalidField_Throws()
        {
            Mock<ISqlMetadataProvider> provider = BuildTableProvider();
            RequestValidator validator = CreateValidator(provider);

            FindRequestContext context = new(ENTITY_NAME, GetTableDbo(), isList: false)
            {
                FieldsToBeReturned = new List<string> { "nonexistent" }
            };

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateRequestContext(context));
            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        #endregion

        #region ValidateInsertRequestContext

        [TestMethod]
        public void ValidateInsertRequestContext_ValidBody_DoesNotThrow()
        {
            RequestValidator validator = CreateValidator(BuildTableProvider());
            InsertRequestContext context = CreateInsertContext(@"{""title"":""Book""}");

            validator.ValidateInsertRequestContext(context);
        }

        [TestMethod]
        public void ValidateInsertRequestContext_MissingRequiredField_Throws()
        {
            RequestValidator validator = CreateValidator(BuildTableProvider());
            // 'title' is non-nullable and required for the replacement-style insert.
            InsertRequestContext context = CreateInsertContext(@"{""description"":""x""}");

            Assert.ThrowsException<DataApiBuilderException>(() => validator.ValidateInsertRequestContext(context));
        }

        [TestMethod]
        public void ValidateInsertRequestContext_NullValueForNonNullable_Throws()
        {
            RequestValidator validator = CreateValidator(BuildTableProvider());
            InsertRequestContext context = CreateInsertContext(@"{""title"":null}");

            Assert.ThrowsException<DataApiBuilderException>(() => validator.ValidateInsertRequestContext(context));
        }

        [TestMethod]
        public void ValidateInsertRequestContext_ExtraFieldStrict_Throws()
        {
            RequestValidator validator = CreateValidator(BuildTableProvider(), requestBodyStrict: true);
            InsertRequestContext context = CreateInsertContext(@"{""title"":""Book"",""unexpected"":""x""}");

            Assert.ThrowsException<DataApiBuilderException>(() => validator.ValidateInsertRequestContext(context));
        }

        [TestMethod]
        public void ValidateInsertRequestContext_ExtraFieldNonStrict_DoesNotThrow()
        {
            RequestValidator validator = CreateValidator(BuildTableProvider(), requestBodyStrict: false);
            InsertRequestContext context = CreateInsertContext(@"{""title"":""Book"",""unexpected"":""x""}");

            validator.ValidateInsertRequestContext(context);
        }

        [TestMethod]
        public void ValidateInsertRequestContext_ReadOnlyFieldInBodyStrict_Throws()
        {
            RequestValidator validator = CreateValidator(BuildTableProviderWithReadOnlyColumn());
            InsertRequestContext context = CreateInsertContext(@"{""title"":""Book"",""computed"":""x""}");

            Assert.ThrowsException<DataApiBuilderException>(() => validator.ValidateInsertRequestContext(context));
        }

        #endregion

        #region ValidateStoredProcedureRequestContext

        [TestMethod]
        public void ValidateStoredProcedureRequestContext_ValidParameters_DoesNotThrow()
        {
            RequestValidator validator = CreateValidator(BuildStoredProcedureProvider());
            StoredProcedureRequestContext context = CreateSpContext(@"{""param1"":""value""}");

            validator.ValidateStoredProcedureRequestContext(context);
        }

        [TestMethod]
        public void ValidateStoredProcedureRequestContext_ExtraParameterStrict_Throws()
        {
            RequestValidator validator = CreateValidator(BuildStoredProcedureProvider(), requestBodyStrict: true);
            StoredProcedureRequestContext context = CreateSpContext(@"{""param1"":""value"",""extra"":""x""}");

            Assert.ThrowsException<DataApiBuilderException>(() => validator.ValidateStoredProcedureRequestContext(context));
        }

        [TestMethod]
        public void ValidateStoredProcedureRequestContext_ExtraParameterNonStrict_DoesNotThrow()
        {
            RequestValidator validator = CreateValidator(BuildStoredProcedureProvider(), requestBodyStrict: false);
            StoredProcedureRequestContext context = CreateSpContext(@"{""param1"":""value"",""extra"":""x""}");

            validator.ValidateStoredProcedureRequestContext(context);
        }

        #endregion

        #region ValidateEntity

        [TestMethod]
        public void ValidateEntity_ExistingEntity_DoesNotThrow()
        {
            Mock<ISqlMetadataProvider> provider = BuildTableProvider();
            provider.Setup(x => x.GetLinkingEntities()).Returns(new Dictionary<string, Entity>());
            RequestValidator validator = CreateValidator(provider);

            validator.ValidateEntity(ENTITY_NAME);
        }

        [TestMethod]
        public void ValidateEntity_UnknownEntity_Throws()
        {
            Mock<ISqlMetadataProvider> provider = new();
            provider.Setup(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject>());
            provider.Setup(x => x.GetLinkingEntities()).Returns(new Dictionary<string, Entity>());
            RequestValidator validator = CreateValidator(provider);

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateEntity(ENTITY_NAME));
            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [TestMethod]
        public void ValidateEntity_LinkingEntity_Throws()
        {
            Mock<ISqlMetadataProvider> provider = BuildTableProvider();
            provider.Setup(x => x.GetLinkingEntities()).Returns(new Dictionary<string, Entity>
            {
                [ENTITY_NAME] = null!
            });
            RequestValidator validator = CreateValidator(provider);

            Assert.ThrowsException<DataApiBuilderException>(() => validator.ValidateEntity(ENTITY_NAME));
        }

        #endregion

        #region Helpers

        private static DatabaseTable GetTableDbo() =>
            new(schemaName: "dbo", tableName: "tbl") { TableDefinition = new SourceDefinition() };

        private static InsertRequestContext CreateInsertContext(string json) =>
            new(ENTITY_NAME, GetTableDbo(), ParsePayload(json), EntityActionOperation.Insert);

        private static StoredProcedureRequestContext CreateSpContext(string json)
        {
            DatabaseStoredProcedure dbo = new(schemaName: "dbo", tableName: "sp")
            {
                SourceType = EntitySourceType.StoredProcedure,
                StoredProcedureDefinition = new StoredProcedureDefinition()
            };
            StoredProcedureRequestContext context = new(ENTITY_NAME, dbo, ParsePayload(json), EntityActionOperation.Execute);
            context.PopulateResolvedParameters();
            return context;
        }

        private static JsonElement ParsePayload(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static SourceDefinition CreateTableSourceDefinition()
        {
            SourceDefinition sourceDefinition = new() { PrimaryKey = new List<string> { "id" } };
            sourceDefinition.Columns.Add("id", new ColumnDefinition { SystemType = typeof(int), IsAutoGenerated = true, IsNullable = false });
            sourceDefinition.Columns.Add("title", new ColumnDefinition { SystemType = typeof(string), IsNullable = false });
            sourceDefinition.Columns.Add("description", new ColumnDefinition { SystemType = typeof(string), IsNullable = true });
            return sourceDefinition;
        }

        private static Mock<ISqlMetadataProvider> BuildTableProvider() => BuildProviderForSourceDefinition(CreateTableSourceDefinition());

        private static Mock<ISqlMetadataProvider> BuildTableProviderWithReadOnlyColumn()
        {
            SourceDefinition sourceDefinition = CreateTableSourceDefinition();
            sourceDefinition.Columns.Add("computed", new ColumnDefinition { SystemType = typeof(string), IsReadOnly = true, IsNullable = true });
            return BuildProviderForSourceDefinition(sourceDefinition);
        }

        private static Mock<ISqlMetadataProvider> BuildProviderForSourceDefinition(SourceDefinition sourceDefinition)
        {
            Mock<ISqlMetadataProvider> provider = new();
            provider.Setup(x => x.GetSourceDefinition(It.IsAny<string>())).Returns(sourceDefinition);
            provider.Setup(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject>
            {
                [ENTITY_NAME] = new DatabaseTable("dbo", "tbl") { TableDefinition = sourceDefinition }
            });

            foreach (string column in sourceDefinition.Columns.Keys)
            {
                string exposed = column;
                string backing = column;
                provider.Setup(x => x.TryGetExposedColumnName(It.IsAny<string>(), column, out exposed)).Returns(true);
                provider.Setup(x => x.TryGetBackingColumn(It.IsAny<string>(), column, out backing)).Returns(true);
            }

            return provider;
        }

        private static Mock<ISqlMetadataProvider> BuildStoredProcedureProvider()
        {
            StoredProcedureDefinition spDefinition = new()
            {
                Parameters = new Dictionary<string, ParameterDefinition>
                {
                    ["param1"] = new ParameterDefinition { SystemType = typeof(string) }
                }
            };

            Mock<ISqlMetadataProvider> provider = new();
            provider.Setup(x => x.GetStoredProcedureDefinition(It.IsAny<string>())).Returns(spDefinition);
            return provider;
        }

        private static RequestValidator CreateValidator(Mock<ISqlMetadataProvider> provider, bool requestBodyStrict = true)
        {
            RuntimeConfig config = new(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "Server=test;", Options: null),
                Runtime: new(
                    Rest: new(Enabled: true, Path: "/api", RequestBodyStrict: requestBodyStrict),
                    GraphQL: new(),
                    Mcp: null,
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new(new Dictionary<string, Entity>
                {
                    [ENTITY_NAME] = new Entity(
                        Source: new(ENTITY_NAME, EntitySourceType.Table, null, null),
                        GraphQL: new(ENTITY_NAME, ENTITY_NAME + "s"),
                        Fields: null,
                        Rest: new(Enabled: true),
                        Permissions: System.Array.Empty<EntityPermission>(),
                        Mappings: null,
                        Relationships: null,
                        Mcp: null)
                }));

            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            Mock<IMetadataProviderFactory> factory = new();
            factory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(provider.Object);
            return new RequestValidator(factory.Object, configProvider);
        }

        #endregion
    }
}
