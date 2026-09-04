// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Transactions;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Resolvers.Sql_Query_Structures;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class SqlMutationEngineHelperTests
    {
        [TestMethod]
        public void FetchPrimaryKeyFieldValues_ReturnsMappedNonNullKeys()
        {
            SourceDefinition definition = new() { PrimaryKey = new() { "book_id", "edition" } };
            Mock<ISqlMetadataProvider> metadata = CreateMetadata(definition);
            Dictionary<string, object?> values = new() { ["id"] = 7, ["edition"] = 2 };

            Dictionary<string, object?> result = InvokeStatic<Dictionary<string, object?>>(
                "FetchPrimaryKeyFieldValues", metadata.Object, "Book", values);

            CollectionAssert.AreEquivalent(new[] { "book_id", "edition" }, new List<string>(result.Keys));
            Assert.AreEqual(7, result["book_id"]);
            Assert.AreEqual(2, result["edition"]);
        }

        [DataTestMethod]
        [DataRow(false, false, DisplayName = "Missing primary-key field mapping")]
        [DataRow(true, true, DisplayName = "Mapped primary-key field has a null value")]
        public void FetchPrimaryKeyFieldValues_MissingMappingOrNullValue_Throws(bool mappingExists, bool nullValue)
        {
            SourceDefinition definition = new() { PrimaryKey = new() { "book_id" } };
            Mock<ISqlMetadataProvider> metadata = new();
            metadata.Setup(x => x.GetSourceDefinition("Book")).Returns(definition);
            string? exposed = mappingExists ? "id" : null;
            metadata.Setup(x => x.TryGetExposedColumnName("Book", "book_id", out exposed)).Returns(mappingExists);
            Dictionary<string, object?> values = new() { ["id"] = nullValue ? null : 7 };

            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(() =>
                InvokeStatic<Dictionary<string, object?>>("FetchPrimaryKeyFieldValues", metadata.Object, "Book", values));

            Assert.IsInstanceOfType<DataApiBuilderException>(exception.InnerException);
        }

        [TestMethod]
        public void PopulateReferencingFields_NullComputedFields_DoesNothing()
        {
            MultipleCreateStructure structure = new("Book", "Publisher");
            ForeignKeyDefinition foreignKey = new()
            {
                ReferencingColumns = new() { "publisher_id" },
                ReferencedColumns = new() { "id" }
            };

            InvokeStatic<object?>("PopulateReferencingFields", new Mock<ISqlMetadataProvider>().Object,
                structure, foreignKey, null, false, "Publisher");

            Assert.AreEqual(0, structure.CurrentEntityParams.Count);
            Assert.AreEqual(0, structure.LinkingTableParams.Count);
        }

        /// <summary>
        /// Verifies linking-table foreign keys use referenced backing names to retrieve computed values.
        /// </summary>
        [TestMethod]
        public void PopulateReferencingFields_LinkingTable_UsesBackingReferencedNames()
        {
            MultipleCreateStructure structure = new("BookAuthor", "Book", isLinkingTableInsertionRequired: true);
            ForeignKeyDefinition foreignKey = new()
            {
                ReferencingColumns = new() { "book_id", "author_id" },
                ReferencedColumns = new() { "id", "author_key" }
            };
            Dictionary<string, object?> values = new() { ["id"] = 7, ["author_key"] = 9 };

            InvokeStatic<object?>("PopulateReferencingFields", new Mock<ISqlMetadataProvider>().Object,
                structure, foreignKey, values, true, null);

            Assert.AreEqual(7, structure.LinkingTableParams["book_id"]);
            Assert.AreEqual(9, structure.LinkingTableParams["author_id"]);
        }

        [DataTestMethod]
        [DataRow(true, DisplayName = "Uses the exposed referenced-field name when mapped")]
        [DataRow(false, DisplayName = "Falls back to the referenced backing-field name")]
        public void PopulateReferencingFields_CurrentEntity_ResolvesExposedNameWhenAvailable(bool mappingExists)
        {
            MultipleCreateStructure structure = new("Book", "Publisher");
            ForeignKeyDefinition foreignKey = new()
            {
                ReferencingColumns = new() { "publisher_id" },
                ReferencedColumns = new() { "publisher_key" }
            };
            Mock<ISqlMetadataProvider> metadata = new();
            string? exposedName = mappingExists ? "publisherId" : null;
            metadata.Setup(x => x.TryGetExposedColumnName("Publisher", "publisher_key", out exposedName))
                .Returns(mappingExists);
            string valueName = mappingExists ? "publisherId" : "publisher_key";
            Dictionary<string, object?> values = new() { [valueName] = 11 };

            InvokeStatic<object?>("PopulateReferencingFields", metadata.Object,
                structure, foreignKey, values, false, "Publisher");

            Assert.AreEqual(11, structure.CurrentEntityParams["publisher_id"]);
        }

        [TestMethod]
        public void GetBackingColumnsFromCollection_MapsNamesAndPreservesValues()
        {
            Mock<ISqlMetadataProvider> metadata = new();
            metadata.Setup(x => x.TryGetBackingColumn("Book", It.IsAny<string>(), out It.Ref<string?>.IsAny))
                .Returns((string _, string exposed, out string? backing) =>
                {
                    backing = exposed == "id" ? "book_id" : null;
                    return backing is not null;
                });
            Dictionary<string, object?> parameters = new() { ["id"] = 7, ["title"] = null };

            Dictionary<string, object?> result = SqlMutationEngine.GetBackingColumnsFromCollection(
                "Book", parameters, metadata.Object);

            Assert.AreEqual(7, result["book_id"]);
            Assert.IsNull(result["title"]);
        }

        [TestMethod]
        public void GetBackingColumnsFromCollection_EmptyInput_ReturnsEmptyDictionary()
        {
            Dictionary<string, object?> result = SqlMutationEngine.GetBackingColumnsFromCollection(
                "Book", new Dictionary<string, object?>(), new Mock<ISqlMetadataProvider>().Object);

            Assert.AreEqual(0, result.Count);
        }

        [DataTestMethod]
        [DataRow(DatabaseType.MySQL, IsolationLevel.RepeatableRead)]
        [DataRow(DatabaseType.MSSQL, IsolationLevel.ReadCommitted)]
        [DataRow(DatabaseType.PostgreSQL, IsolationLevel.ReadCommitted)]
        public void ConstructTransactionScopeBasedOnDbType_UsesExpectedIsolationLevel(
            DatabaseType databaseType,
            IsolationLevel expected)
        {
            Mock<ISqlMetadataProvider> metadata = new();
            metadata.Setup(x => x.GetDatabaseType()).Returns(databaseType);

            using TransactionScope scope = InvokeStatic<TransactionScope>(
                "ConstructTransactionScopeBasedOnDbType", metadata.Object);

            Assert.AreEqual(expected, Transaction.Current!.IsolationLevel);
        }

        [TestMethod]
        public void GetDbOperationResultJsonDocument_ReturnsResultAndEmptyMetadata()
        {
            Tuple<JsonDocument?, IMetadata?> result = InvokeStatic<Tuple<JsonDocument?, IMetadata?>>(
                "GetDbOperationResultJsonDocument", "success");

            Assert.AreEqual("success", result.Item1!.RootElement.GetProperty("result").GetString());
            Assert.IsNotNull(result.Item2);
        }

        /// <summary>
        /// Verifies GraphQL update authorization is delegated as update while other column-aware operations retain their operation.
        /// </summary>
        [DataTestMethod]
        [DataRow(EntityActionOperation.UpdateGraphQL, EntityActionOperation.Update, true, DisplayName = "UpdateGraphQL delegates as Update and is authorized")]
        [DataRow(EntityActionOperation.Create, EntityActionOperation.Create, false, DisplayName = "Create delegates unchanged and is denied")]
        public void AreFieldsAuthorizedForEntity_DelegatesColumnOperations(
            EntityActionOperation requested,
            EntityActionOperation delegated,
            bool expected)
        {
            Mock<IAuthorizationResolver> authorization = new();
            authorization.Setup(x => x.AreColumnsAllowedForOperation(
                "Book", "role", delegated, It.IsAny<IEnumerable<string>>())).Returns(expected);
            SqlMutationEngine engine = CreateUninitializedEngine(authorization.Object);

            bool result = InvokeInstance<bool>(
                engine, "AreFieldsAuthorizedForEntity", "role", "Book", requested, new[] { "title" });

            Assert.AreEqual(expected, result);
            authorization.Verify(x => x.AreColumnsAllowedForOperation(
                "Book", "role", delegated, It.IsAny<IEnumerable<string>>()), Times.Once);
        }

        [DataTestMethod]
        [DataRow(EntityActionOperation.Delete)]
        [DataRow(EntityActionOperation.Execute)]
        public void AreFieldsAuthorizedForEntity_OperationsWithoutColumnAuthorization_ReturnTrue(EntityActionOperation operation)
        {
            Mock<IAuthorizationResolver> authorization = new();
            SqlMutationEngine engine = CreateUninitializedEngine(authorization.Object);

            Assert.IsTrue(InvokeInstance<bool>(
                engine, "AreFieldsAuthorizedForEntity", "role", "Book", operation, Array.Empty<string>()));
            authorization.VerifyNoOtherCalls();
        }

        [TestMethod]
        public void AreFieldsAuthorizedForEntity_InvalidOperation_Throws()
        {
            SqlMutationEngine engine = CreateUninitializedEngine(new Mock<IAuthorizationResolver>().Object);

            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(() =>
                InvokeInstance<bool>(engine, "AreFieldsAuthorizedForEntity", "role", "Book", EntityActionOperation.Read, Array.Empty<string>()));

            Assert.IsInstanceOfType<DataApiBuilderException>(exception.InnerException);
        }

        /// <summary>
        /// Verifies each supported stored-procedure operation produces its operation-specific HTTP result shape.
        /// </summary>
        [DataTestMethod]
        [DataRow(EntityActionOperation.Delete, false, typeof(NoContentResult))]
        [DataRow(EntityActionOperation.Insert, true, typeof(CreatedResult))]
        [DataRow(EntityActionOperation.Insert, false, typeof(CreatedResult))]
        [DataRow(EntityActionOperation.Update, true, typeof(OkObjectResult))]
        [DataRow(EntityActionOperation.Update, false, typeof(OkObjectResult))]
        [DataRow(EntityActionOperation.UpdateIncremental, false, typeof(OkObjectResult))]
        [DataRow(EntityActionOperation.Upsert, false, typeof(OkObjectResult))]
        [DataRow(EntityActionOperation.UpsertIncremental, false, typeof(OkObjectResult))]
        public async Task ExecuteStoredProcedure_ReturnsResponseForEachSupportedOperation(
            EntityActionOperation operation,
            bool hasRows,
            Type expectedResultType)
        {
            JsonArray result = hasRows ? new JsonArray(new JsonObject { ["id"] = 7 }) : new JsonArray();
            (SqlMutationEngine engine, StoredProcedureRequestContext context, string dataSourceName) =
                CreateStoredProcedureFixture(operation, result);

            IActionResult? response = await engine.ExecuteAsync(context, dataSourceName);

            Assert.IsInstanceOfType(response, expectedResultType);
            if (response is CreatedResult created)
            {
                Assert.AreEqual("https://example.test/api/procedure", created.Location);
            }
        }

        [TestMethod]
        public async Task ExecuteStoredProcedure_RejectsUnsupportedOperationAfterExecution()
        {
            (SqlMutationEngine engine, StoredProcedureRequestContext context, string dataSourceName) =
                CreateStoredProcedureFixture(EntityActionOperation.Create, new JsonArray());

            await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() => engine.ExecuteAsync(context, dataSourceName));
        }

        [TestMethod]
        public void PopulateCurrentAndLinkingEntityParams_NullInput_DoesNothing()
        {
            MultipleCreateStructure structure = new("Book", string.Empty);

            InvokeStatic<object?>("PopulateCurrentAndLinkingEntityParams", structure,
                new Mock<ISqlMetadataProvider>().Object, null);

            Assert.AreEqual(0, structure.CurrentEntityParams.Count);
            Assert.AreEqual(0, structure.LinkingTableParams.Count);
        }

        [TestMethod]
        public void PopulateCurrentAndLinkingEntityParams_PartitionsColumnsRelationshipsAndLinkingFields()
        {
            MultipleCreateStructure structure = new("Book", string.Empty, new Dictionary<string, object?>
            {
                ["title"] = "DAB",
                ["royalty"] = 10,
                ["authors"] = new object()
            });
            Mock<ISqlMetadataProvider> metadata = new();
            metadata.Setup(x => x.TryGetBackingColumn("Book", "title", out It.Ref<string?>.IsAny)).Returns(true);
            Dictionary<string, EntityRelationship> relationships = new()
            {
                ["authors"] = new(Cardinality.Many, "Author", null, null, "book_author", null, null)
            };

            InvokeStatic<object?>("PopulateCurrentAndLinkingEntityParams", structure, metadata.Object, relationships);

            Assert.AreEqual("DAB", structure.CurrentEntityParams["title"]);
            Assert.AreEqual(10, structure.LinkingTableParams["royalty"]);
            Assert.IsFalse(structure.CurrentEntityParams.ContainsKey("authors"));
            Assert.IsFalse(structure.LinkingTableParams.ContainsKey("authors"));
        }

        [TestMethod]
        public void DetermineRelationships_NullMetadata_DoesNothing()
        {
            MultipleCreateStructure structure = new("Book", string.Empty, new Dictionary<string, object?>());

            InvokeStatic<object?>("DetermineReferencedAndReferencingRelationships",
                new Mock<HotChocolate.Resolvers.IMiddlewareContext>().Object,
                structure,
                new Mock<ISqlMetadataProvider>().Object,
                null,
                new List<HotChocolate.Language.ObjectFieldNode>());

            Assert.AreEqual(0, structure.ReferencedRelationships.Count);
            Assert.AreEqual(0, structure.ReferencingRelationships.Count);
        }

        [TestMethod]
        public void DetermineRelationships_NullInput_Throws()
        {
            MultipleCreateStructure structure = new("Book", string.Empty);

            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(() =>
                InvokeStatic<object?>("DetermineReferencedAndReferencingRelationships",
                    new Mock<HotChocolate.Resolvers.IMiddlewareContext>().Object,
                    structure,
                    new Mock<ISqlMetadataProvider>().Object,
                    new Dictionary<string, EntityRelationship>(),
                    new List<HotChocolate.Language.ObjectFieldNode>()));

            Assert.IsInstanceOfType<DataApiBuilderException>(exception.InnerException);
        }

        /// <summary>
        /// Verifies many-to-many input is classified as referencing data while fields without relationships are ignored.
        /// </summary>
        [TestMethod]
        public void DetermineRelationships_ManyToManyIsReferencingAndUnknownFieldIsIgnored()
        {
            object relationshipValue = new();
            MultipleCreateStructure structure = new("Book", string.Empty, new Dictionary<string, object?>
            {
                ["authors"] = relationshipValue,
                ["title"] = "DAB"
            });
            Dictionary<string, EntityRelationship> relationships = new()
            {
                ["authors"] = new(Cardinality.Many, "Author", null, null, "book_author", null, null)
            };

            InvokeStatic<object?>("DetermineReferencedAndReferencingRelationships",
                new Mock<HotChocolate.Resolvers.IMiddlewareContext>().Object,
                structure,
                new Mock<ISqlMetadataProvider>().Object,
                relationships,
                new List<HotChocolate.Language.ObjectFieldNode>());

            Assert.AreEqual(1, structure.ReferencingRelationships.Count);
            Assert.AreEqual("authors", structure.ReferencingRelationships[0].Item1);
            Assert.AreSame(relationshipValue, structure.ReferencingRelationships[0].Item2);
            Assert.AreEqual(0, structure.ReferencedRelationships.Count);
        }

        [TestMethod]
        public void MultipleCreateArgumentParsing_MissingRootFieldThrowsBadRequest()
        {
            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));

            Assert.ThrowsException<DataApiBuilderException>(() =>
                SqlMutationEngine.GQLMultipleCreateArgumentToDictParams(
                    Mock.Of<IMiddlewareContext>(),
                    "item",
                    new Dictionary<string, object?>(),
                    Mock.Of<ISqlMetadataProvider>(),
                    "Book",
                    runtimeConfig));
        }

        [TestMethod]
        public void MultipleCreateArgumentParsing_UnsupportedInputTypeThrowsBadRequest()
        {
            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));

            Assert.ThrowsException<DataApiBuilderException>(() =>
                SqlMutationEngine.GQLMultipleCreateArgumentToDictParamsHelper(
                    Mock.Of<IMiddlewareContext>(),
                    null!,
                    new object(),
                    Mock.Of<ISqlMetadataProvider>(),
                    "Book",
                    runtimeConfig));
        }

        [TestMethod]
        public void ProcessMultipleCreateInputField_NullInputThrowsBadRequest()
        {
            SqlMutationEngine engine = CreateUninitializedEngine(Mock.Of<IAuthorizationResolver>());
            MultipleCreateStructure structure = new("Book", string.Empty);

            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(() =>
                InvokeInstance<object?>(engine, "ProcessMultipleCreateInputField",
                    Mock.Of<IMiddlewareContext>(), null, Mock.Of<ISqlMetadataProvider>(), structure, 0));

            Assert.IsInstanceOfType<DataApiBuilderException>(exception.InnerException);
        }

        [TestMethod]
        public void ProcessMultipleCreateInputField_NonNodeObjectThrowsBadRequest()
        {
            SqlMutationEngine engine = CreateUninitializedEngine(Mock.Of<IAuthorizationResolver>());
            MultipleCreateStructure structure = new(
                "Book",
                string.Empty,
                new Dictionary<string, object?> { ["title"] = "DAB" });

            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(() =>
                InvokeInstance<object?>(engine, "ProcessMultipleCreateInputField",
                    Mock.Of<IMiddlewareContext>(), new object(), Mock.Of<ISqlMetadataProvider>(), structure, 0));

            Assert.IsInstanceOfType<DataApiBuilderException>(exception.InnerException);
        }

        [TestMethod]
        public void ProcessMultipleCreateInputField_NullListNodeThrowsBadRequest()
        {
            SqlMutationEngine engine = CreateUninitializedEngine(Mock.Of<IAuthorizationResolver>());
            MultipleCreateStructure structure = new(
                "Book",
                string.Empty,
                new List<IDictionary<string, object?>> { new Dictionary<string, object?>() });

            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(() =>
                InvokeInstance<object?>(engine, "ProcessMultipleCreateInputField",
                    Mock.Of<IMiddlewareContext>(), new List<IValueNode> { null! }, Mock.Of<ISqlMetadataProvider>(), structure, 0));

            Assert.IsInstanceOfType<DataApiBuilderException>(exception.InnerException);
        }

        /// <summary>
        /// Verifies invalid private mutation invocations distinguish missing GraphQL context from an unsupported operation.
        /// </summary>
        [DataTestMethod]
        [DataRow(EntityActionOperation.UpdateGraphQL, DisplayName = "UpdateGraphQL without middleware context throws ArgumentNullException")]
        [DataRow(EntityActionOperation.Delete, DisplayName = "Delete is unsupported by this mutation path")]
        public async Task PerformMutationOperation_RejectsInvalidInvocation(EntityActionOperation operation)
        {
            (SqlMutationEngine engine, ISqlMetadataProvider metadata) = CreateMutationOperationFixture();
            MethodInfo method = typeof(SqlMutationEngine).GetMethod("PerformMutationOperation", BindingFlags.Instance | BindingFlags.NonPublic)!;

            Task task = (Task)method.Invoke(engine, new object?[]
            {
                "Book",
                operation,
                new Dictionary<string, object?>(),
                metadata,
                null
            })!;

            if (operation is EntityActionOperation.UpdateGraphQL)
            {
                await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () => await task);
            }
            else
            {
                await Assert.ThrowsExceptionAsync<NotSupportedException>(async () => await task);
            }
        }

        /// <summary>
        /// Verifies empty linking-table and current-entity insert results produce their distinct failure statuses.
        /// </summary>
        [DataTestMethod]
        [DataRow(true, false, DisplayName = "Empty linking-table result is an internal server error")]
        [DataRow(false, true, DisplayName = "Null current-entity result is forbidden")]
        public void BuildAndExecuteInsertDbQueries_ReportsEmptyResults(bool linkingEntity, bool returnNull)
        {
            (SqlMutationEngine engine, ISqlMetadataProvider metadata) =
                CreateMutationOperationFixture(returnNull ? null : new DbResultSet(new Dictionary<string, object>()));
            SourceDefinition definition = new();

            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(() =>
                InvokeInstance<Dictionary<string, object?>>(engine, "BuildAndExecuteInsertDbQueries",
                    metadata,
                    "Book",
                    "Parent",
                    new Dictionary<string, object?>(),
                    definition,
                    linkingEntity,
                    1));

            DataApiBuilderException error = (DataApiBuilderException)exception.InnerException!;
            Assert.AreEqual(
                linkingEntity ? System.Net.HttpStatusCode.InternalServerError : System.Net.HttpStatusCode.Forbidden,
                error.StatusCode);
        }

        private static Mock<ISqlMetadataProvider> CreateMetadata(SourceDefinition definition)
        {
            Mock<ISqlMetadataProvider> metadata = new();
            metadata.Setup(x => x.GetSourceDefinition("Book")).Returns(definition);
            metadata.Setup(x => x.TryGetExposedColumnName("Book", "book_id", out It.Ref<string?>.IsAny))
                .Returns((string _, string backing, out string? exposed) =>
                {
                    exposed = backing == "book_id" ? "id" : backing;
                    return true;
                });
            metadata.Setup(x => x.TryGetExposedColumnName("Book", "edition", out It.Ref<string?>.IsAny))
                .Returns((string _, string backing, out string? exposed) =>
                {
                    exposed = backing;
                    return true;
                });
            return metadata;
        }

        private static (SqlMutationEngine Engine, ISqlMetadataProvider Metadata) CreateMutationOperationFixture(
            DbResultSet? syncResult = null)
        {
            Entity entity = new(
                Source: new EntitySource("dbo.Books", EntitySourceType.Table, null, null),
                GraphQL: null,
                Fields: null,
                Rest: null,
                Permissions: Array.Empty<EntityPermission>(),
                Mappings: null,
                Relationships: null,
                Mcp: null);
            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty),
                Entities: new RuntimeEntities(new Dictionary<string, Entity> { ["Book"] = entity, ["Parent"] = entity }));
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);

            SourceDefinition definition = new();
            DatabaseTable table = new("dbo", "Books") { TableDefinition = definition };
            Mock<ISqlMetadataProvider> metadata = new();
            metadata.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.MSSQL);
            metadata.Setup(x => x.GetSourceDefinition("Book")).Returns(definition);
            metadata.SetupGet(x => x.EntityToDatabaseObject)
                .Returns(new Dictionary<string, DatabaseObject> { ["Book"] = table });

            Mock<IQueryBuilder> queryBuilder = new();
            queryBuilder.Setup(x => x.Build(It.IsAny<SqlInsertStructure>())).Returns("INSERT");
            Mock<IQueryExecutor> queryExecutor = new();
            queryExecutor.Setup(x => x.ExecuteQuery<DbResultSet>(
                    It.IsAny<string>(),
                    It.IsAny<IDictionary<string, DbConnectionParam>>(),
                    It.IsAny<Func<DbDataReader, List<string>?, DbResultSet>>(),
                    It.IsAny<HttpContext?>(),
                    It.IsAny<List<string>?>(),
                    It.IsAny<string>()))
                .Returns(syncResult);
            Mock<IAbstractQueryManagerFactory> queryManagerFactory = new();
            queryManagerFactory.Setup(x => x.GetQueryBuilder(DatabaseType.MSSQL)).Returns(queryBuilder.Object);
            queryManagerFactory.Setup(x => x.GetQueryExecutor(DatabaseType.MSSQL)).Returns(queryExecutor.Object);

            Mock<IHttpContextAccessor> accessor = new();
            accessor.Setup(x => x.HttpContext).Returns(new DefaultHttpContext());
            Mock<IAuthorizationResolver> authorization = new();
            authorization.Setup(x => x.ResolveDBPolicy(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<EntityActionOperation>(),
                    It.IsAny<HttpContext>()))
                .Returns(ResolvedDatabasePolicy.Empty);

            SqlMutationEngine engine = new(
                queryManagerFactory.Object,
                Mock.Of<IMetadataProviderFactory>(),
                Mock.Of<IQueryEngineFactory>(),
                authorization.Object,
                null!,
                accessor.Object,
                runtimeConfigProvider);
            return (engine, metadata.Object);
        }

        private static SqlMutationEngine CreateUninitializedEngine(IAuthorizationResolver authorizationResolver)
        {
            SqlMutationEngine engine = (SqlMutationEngine)RuntimeHelpers.GetUninitializedObject(typeof(SqlMutationEngine));
            typeof(SqlMutationEngine).GetField("_authorizationResolver", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(engine, authorizationResolver);
            return engine;
        }

        private static (SqlMutationEngine Engine, StoredProcedureRequestContext Context, string DataSourceName)
            CreateStoredProcedureFixture(EntityActionOperation operation, JsonArray result)
        {
            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            string dataSourceName = runtimeConfigProvider.GetConfig().DefaultDataSourceName;

            StoredProcedureDefinition definition = new();
            DatabaseStoredProcedure procedure = new("dbo", "procedure")
            {
                StoredProcedureDefinition = definition
            };
            Mock<ISqlMetadataProvider> metadata = new();
            metadata.SetupGet(x => x.EntityToDatabaseObject)
                .Returns(new Dictionary<string, DatabaseObject> { ["Procedure"] = procedure });
            metadata.Setup(x => x.GetStoredProcedureDefinition("Procedure")).Returns(definition);
            metadata.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.MSSQL);

            Mock<IMetadataProviderFactory> metadataFactory = new();
            metadataFactory.Setup(x => x.GetMetadataProvider(dataSourceName)).Returns(metadata.Object);
            Mock<IQueryBuilder> queryBuilder = new();
            queryBuilder.Setup(x => x.Build(It.IsAny<SqlExecuteStructure>())).Returns("EXEC dbo.procedure");
            Mock<IQueryExecutor> queryExecutor = new();
            queryExecutor.Setup(x => x.ExecuteQueryAsync(
                    It.IsAny<string>(),
                    It.IsAny<IDictionary<string, DbConnectionParam>>(),
                    It.IsAny<Func<System.Data.Common.DbDataReader, List<string>?, Task<JsonArray>>>(),
                    dataSourceName,
                    It.IsAny<HttpContext>(),
                    It.IsAny<List<string>?>()))
                .ReturnsAsync(result);
            Mock<IAbstractQueryManagerFactory> queryManagerFactory = new();
            queryManagerFactory.Setup(x => x.GetQueryBuilder(DatabaseType.MSSQL)).Returns(queryBuilder.Object);
            queryManagerFactory.Setup(x => x.GetQueryExecutor(DatabaseType.MSSQL)).Returns(queryExecutor.Object);

            DefaultHttpContext httpContext = new();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("example.test");
            httpContext.Request.Path = "/api/procedure";
            Mock<IHttpContextAccessor> accessor = new();
            accessor.Setup(x => x.HttpContext).Returns(httpContext);

            SqlMutationEngine engine = new(
                queryManagerFactory.Object,
                metadataFactory.Object,
                new Mock<IQueryEngineFactory>().Object,
                new Mock<IAuthorizationResolver>().Object,
                null!,
                accessor.Object,
                runtimeConfigProvider);
            StoredProcedureRequestContext context = new("Procedure", procedure, null, operation);
            context.PopulateResolvedParameters();
            return (engine, context, dataSourceName);
        }

        private static T InvokeStatic<T>(string methodName, params object?[] arguments)
        {
            MethodInfo method = typeof(SqlMutationEngine).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
            return (T)method.Invoke(null, arguments)!;
        }

        private static T InvokeInstance<T>(SqlMutationEngine instance, string methodName, params object?[] arguments)
        {
            MethodInfo method = typeof(SqlMutationEngine).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (T)method.Invoke(instance, arguments)!;
        }
    }
}
