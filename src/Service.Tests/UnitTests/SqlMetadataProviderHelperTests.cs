// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Language;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlTypes;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class SqlMetadataProviderHelperTests
    {
        [TestMethod]
        public void InferredDatabaseObjectAccessors_ReturnConfiguredValues()
        {
            SourceDefinition sourceDefinition = new();
            MsSqlMetadataProvider provider = CreateProvider(new DatabaseTable("dbo", "books")
            {
                TableDefinition = sourceDefinition
            });

            Assert.AreEqual("dbo", provider.GetSchemaName("Book"));
            Assert.AreEqual("books", provider.GetDatabaseObjectName("Book"));
            Assert.AreSame(sourceDefinition, provider.GetSourceDefinition("Book"));
            Assert.AreEqual(string.Empty, provider.GetDatabaseName());
            Assert.AreEqual(DatabaseType.MSSQL, provider.GetDatabaseType());
        }

        [TestMethod]
        public void GetStoredProcedureDefinition_ReturnsConfiguredDefinition()
        {
            StoredProcedureDefinition definition = new();
            MsSqlMetadataProvider provider = CreateProvider(new DatabaseStoredProcedure("dbo", "get_books")
            {
                StoredProcedureDefinition = definition
            });

            Assert.AreSame(definition, provider.GetStoredProcedureDefinition("Book"));
        }

        [DataTestMethod]
        [DataRow("GetSchemaName")]
        [DataRow("GetDatabaseObjectName")]
        [DataRow("GetSourceDefinition")]
        [DataRow("GetStoredProcedureDefinition")]
        public void InferredDatabaseObjectAccessors_MissingEntity_Throw(string methodName)
        {
            MsSqlMetadataProvider provider = CreateProvider();
            MethodInfo method = typeof(MsSqlMetadataProvider).GetMethod(methodName)!;

            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(
                () => method.Invoke(provider, new object[] { "Missing" }));

            Assert.IsInstanceOfType<DataApiBuilderException>(exception.InnerException);
        }

        [DataTestMethod]
        [DataRow("Order Items", "OrderItems")]
        [DataRow("  multiple  spaces  ", "MultipleSpaces")]
        [DataRow("NoSpaces", "NoSpaces")]
        [DataRow("UPPER CASE", "UPPERCASE")]
        [DataRow("", "")]
        public void RemoveWhitespaceAddCamelCase_TransformsDeterministically(string input, string expected)
        {
            MethodInfo method = typeof(MsSqlMetadataProvider).BaseType!.GetMethod(
                "RemoveWhitespaceAddCamelCase", BindingFlags.Static | BindingFlags.NonPublic)!;

            Assert.AreEqual(expected, method.Invoke(null, new object[] { input }));
        }

        [TestMethod]
        public void FieldMappingLookups_UseCachesAndConfiguredFieldAliases()
        {
            Entity entity = CreateEntity(new List<FieldMetadata>
            {
                new() { Name = "book_id", Alias = "id" }
            });
            MsSqlMetadataProvider provider = CreateProvider(entity: entity);
            Dictionary<string, Dictionary<string, string>> backingToExposed = GetMap(provider, "EntityBackingColumnsToExposedNames");
            Dictionary<string, Dictionary<string, string>> exposedToBacking = GetMap(provider, "EntityExposedNamesToBackingColumnNames");
            backingToExposed["Book"] = new() { ["title"] = "bookTitle" };
            exposedToBacking["Book"] = new() { ["bookTitle"] = "title" };

            Assert.IsTrue(provider.TryGetExposedColumnName("Book", "title", out string? cachedExposed));
            Assert.AreEqual("bookTitle", cachedExposed);
            Assert.IsTrue(provider.TryGetExposedColumnName("Book", "BOOK_ID", out string? configuredExposed));
            Assert.AreEqual("id", configuredExposed);
            Assert.IsTrue(provider.TryGetBackingColumn("Book", "bookTitle", out string? cachedBacking));
            Assert.AreEqual("title", cachedBacking);
            Assert.IsTrue(provider.TryGetBackingColumn("Book", "ID", out string? configuredBacking));
            Assert.AreEqual("book_id", configuredBacking);
            Assert.IsFalse(provider.TryGetExposedColumnName("Book", "missing", out _));
            Assert.IsFalse(provider.TryGetBackingColumn("Book", "missing", out _));

            Assert.IsTrue(provider.TryGetExposedFieldToBackingFieldMap("Book", out IReadOnlyDictionary<string, string>? exposedMap));
            Assert.AreSame(exposedToBacking["Book"], exposedMap);
            Assert.IsTrue(provider.TryGetBackingFieldToExposedFieldMap("Book", out IReadOnlyDictionary<string, string>? backingMap));
            Assert.AreSame(backingToExposed["Book"], backingMap);
            Assert.IsFalse(provider.TryGetExposedFieldToBackingFieldMap("Missing", out _));
            Assert.IsFalse(provider.TryGetBackingFieldToExposedFieldMap("Missing", out _));
        }

        [TestMethod]
        public void FieldMappingLookups_MissingInitializationThrow()
        {
            MsSqlMetadataProvider provider = CreateProvider(entity: CreateEntity());

            Assert.ThrowsException<KeyNotFoundException>(() => provider.TryGetExposedColumnName("Book", "id", out _));
            Assert.ThrowsException<KeyNotFoundException>(() => provider.TryGetBackingColumn("Book", "id", out _));
        }

        [TestMethod]
        public void TryGetArrayElementSyntaxKind_RecognizesSupportedArrayAndRejectsScalar()
        {
            SourceDefinition definition = new();
            definition.Columns["vector"] = new ColumnDefinition(typeof(float[]))
            {
                IsArrayType = true,
                ElementSystemType = typeof(float)
            };
            definition.Columns["title"] = new ColumnDefinition(typeof(string));
            MsSqlMetadataProvider provider = CreateProvider(
                new DatabaseTable("dbo", "books") { TableDefinition = definition },
                CreateEntity());
            Dictionary<string, Dictionary<string, string>> map = GetMap(provider, "EntityExposedNamesToBackingColumnNames");
            map["Book"] = new() { ["vector"] = "vector", ["title"] = "title" };

            Assert.IsTrue(provider.TryGetArrayElementSyntaxKind("Book", "vector", out SyntaxKind kind));
            Assert.AreEqual(SyntaxKind.FloatValue, kind);
            Assert.IsFalse(provider.TryGetArrayElementSyntaxKind("Book", "title", out _));
        }

        [TestMethod]
        public void GetEntityName_ResolvesEntityAndSingularNameAndRejectsUnknownType()
        {
            MsSqlMetadataProvider provider = CreateProvider(entity: CreateEntity(singular: "Volume"));

            Assert.AreEqual("Book", provider.GetEntityName("Book"));
            Assert.AreEqual("Book", provider.GetEntityName("Volume"));
            Assert.ThrowsException<DataApiBuilderException>(() => provider.GetEntityName("Missing"));
        }

        [TestMethod]
        public void ParseSchemaAndDbTableName_HandlesDefaultAndExplicitMsSqlCases()
        {
            MsSqlMetadataProvider provider = CreateProvider();
            Assert.AreEqual(("dbo", "books"), provider.ParseSchemaAndDbTableName("books"));
            Assert.AreEqual(("custom", "books"), provider.ParseSchemaAndDbTableName("custom.books"));
        }

        [TestMethod]
        public void PopulateColumnDefinitionWithHasDefaultAndDbType_MapsMsSqlMetadata()
        {
            SourceDefinition definition = new();
            definition.Columns["title"] = new ColumnDefinition(typeof(string));
            definition.Columns["published"] = new ColumnDefinition(typeof(DateTime));
            definition.Columns["embedding"] = new ColumnDefinition(typeof(SqlVector<Single>));

            DataTable columns = new();
            columns.Columns.Add("COLUMN_NAME", typeof(string));
            columns.Columns.Add("COLUMN_DEFAULT", typeof(object));
            columns.Columns.Add("DATA_TYPE", typeof(string));
            columns.Rows.Add("title", DBNull.Value, "nvarchar");
            columns.Rows.Add("published", "getdate()", "date");
            columns.Rows.Add("embedding", DBNull.Value, "varbinary");
            columns.Rows.Add("not_configured", DBNull.Value, "int");

            MsSqlMetadataProvider provider = CreateProvider();
            GetMsSqlMethod("PopulateColumnDefinitionWithHasDefaultAndDbType")
                .Invoke(provider, new object[] { definition, columns });

            ColumnDefinition title = definition.Columns["title"];
            Assert.IsFalse(title.HasDefault);
            Assert.IsNull(title.DefaultValue);
            Assert.AreEqual(DbType.String, title.DbType);
            Assert.AreEqual(SqlDbType.NVarChar, title.SqlDbType);

            ColumnDefinition published = definition.Columns["published"];
            Assert.IsTrue(published.HasDefault);
            Assert.AreEqual("getdate()", published.DefaultValue);
            Assert.AreEqual(DbType.Date, published.DbType);
            Assert.AreEqual(SqlDbType.Date, published.SqlDbType);

            ColumnDefinition embedding = definition.Columns["embedding"];
            Assert.AreEqual(typeof(float[]), embedding.SystemType);
            Assert.AreEqual(typeof(float), embedding.ElementSystemType);
            Assert.IsTrue(embedding.IsArrayType);
            Assert.AreEqual(DbType.Single, embedding.DbType);
            Assert.AreEqual(SqlDbType.Vector, embedding.SqlDbType);
        }

        [TestMethod]
        public void PopulateMetadataForLinkingObject_MultipleCreateDisabledReturnsWithoutChanges()
        {
            MsSqlMetadataProvider provider = CreateProvider();
            Dictionary<string, DatabaseObject> sourceObjects = new();

            GetMsSqlMethod("PopulateMetadataForLinkingObject").Invoke(provider, new object[]
            {
                "Book", "Author", "dbo.book_authors", sourceObjects
            });

            Assert.AreEqual(0, sourceObjects.Count);
        }

        [TestMethod]
        public void TryResolveDbType_UnknownSqlTypeReturnsFalse()
        {
            MsSqlMetadataProvider provider = CreateProvider();
            object?[] arguments = new object?[] { "future_datetime", null };

            bool result = (bool)GetMsSqlMethod("TryResolveDbType").Invoke(provider, arguments)!;

            Assert.IsFalse(result);
            Assert.AreEqual((DbType)0, arguments[1]);
        }

        [TestMethod]
        public async System.Threading.Tasks.Task GenerateAutoentitiesIntoEntities_NullConfigurationReturns()
        {
            MsSqlMetadataProvider provider = CreateProvider();

            System.Threading.Tasks.Task task = (System.Threading.Tasks.Task)GetMsSqlMethod("GenerateAutoentitiesIntoEntities")
                .Invoke(provider, new object?[] { null })!;

            await task;
        }

        [TestMethod]
        public async System.Threading.Tasks.Task GenerateAutoentitiesIntoEntities_NullResultObjectThrows()
        {
            MsSqlMetadataProvider provider = CreateProvider();
            ConfigureAutoentityQuery(provider, new JsonArray((JsonNode?)null));
            IReadOnlyDictionary<string, Autoentity> autoentities = new Dictionary<string, Autoentity>
            {
                ["all"] = new Autoentity(null, null, null)
            };

            System.Threading.Tasks.Task task = (System.Threading.Tasks.Task)GetMsSqlMethod("GenerateAutoentitiesIntoEntities")
                .Invoke(provider, new object?[] { autoentities })!;

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() => task);
            Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
        }

        [TestMethod]
        public async System.Threading.Tasks.Task GenerateAutoentitiesIntoEntities_IncompleteResultObjectIsSkipped()
        {
            MsSqlMetadataProvider provider = CreateProvider();
            ConfigureAutoentityQuery(provider, new JsonArray(new JsonObject
            {
                ["entity_name"] = "Book",
                ["object"] = "books"
            }));
            IReadOnlyDictionary<string, Autoentity> autoentities = new Dictionary<string, Autoentity>
            {
                ["all"] = new Autoentity(null, null, null)
            };

            System.Threading.Tasks.Task task = (System.Threading.Tasks.Task)GetMsSqlMethod("GenerateAutoentitiesIntoEntities")
                .Invoke(provider, new object?[] { autoentities })!;

            await task;
            Assert.AreEqual(0, provider.EntityToDatabaseObject.Count);
        }

        [TestMethod]
        public void RelationalOnlyUnsupportedMetadataMembersThrow()
        {
            MsSqlMetadataProvider provider = CreateProvider();

            Assert.ThrowsException<NotImplementedException>(() => provider.GetSchemaGraphQLFieldNamesForEntityName("Book"));
            Assert.ThrowsException<NotImplementedException>(() => provider.GetSchemaGraphQLFieldTypeFromFieldName("Book", "id"));
            Assert.ThrowsException<NotImplementedException>(() => provider.GetSchemaGraphQLFieldFromFieldName("Book", "id"));
            Assert.ThrowsException<NotImplementedException>(() => provider.GetPartitionKeyPath("db", "container"));
            Assert.ThrowsException<NotImplementedException>(() => provider.SetPartitionKeyPath("db", "container", "/id"));
        }

        [TestMethod]
        public void VerifyForeignKeyExistsInDb_ChecksBothDirectionsAndNullMetadata()
        {
            MsSqlMetadataProvider provider = CreateProvider();
            DatabaseTable first = new("dbo", "books");
            DatabaseTable second = new("dbo", "authors");

            provider.PairToFkDefinition = null;
            Assert.IsFalse(provider.VerifyForeignKeyExistsInDB(first, second));

            RelationShipPair reverse = new(second, first);
            provider.PairToFkDefinition = new() { [reverse] = new ForeignKeyDefinition { Pair = reverse } };
            Assert.IsTrue(provider.VerifyForeignKeyExistsInDB(first, second));
        }

        [TestMethod]
        public void TryGetFkDefinition_MissingEntitiesReturnsFalse()
        {
            MsSqlMetadataProvider provider = CreateProvider();

            Assert.IsFalse(provider.TryGetFKDefinition("Source", "Target", "Source", "Target", out ForeignKeyDefinition? definition));
            Assert.IsNull(definition);
        }

        [TestMethod]
        public void InitializeAsync_ReplacesMapsAndGeneratesFieldMappings()
        {
            SourceDefinition definition = new();
            definition.Columns["title"] = new ColumnDefinition(typeof(string));
            Dictionary<string, DatabaseObject> databaseObjects = new()
            {
                ["Book"] = new DatabaseTable("dbo", "books") { TableDefinition = definition }
            };
            Dictionary<string, string> procedures = new() { ["getBooks"] = "Book" };
            MsSqlMetadataProvider provider = CreateProvider(entity: CreateEntity());

            provider.InitializeAsync(databaseObjects, procedures);

            Assert.AreSame(databaseObjects, provider.EntityToDatabaseObject);
            Assert.AreSame(procedures, provider.GraphQLStoredProcedureExposedNameToEntityNameMap);
            Assert.IsTrue(provider.TryGetExposedColumnName("Book", "title", out string? exposed));
            Assert.AreEqual("title", exposed);
        }

        [TestMethod]
        public void BaseVirtualMetadataOperations_UseDefaultBehavior()
        {
            BaseBehaviorMetadataProvider provider =
                (BaseBehaviorMetadataProvider)RuntimeHelpers.GetUninitializedObject(typeof(BaseBehaviorMetadataProvider));

            Assert.ThrowsException<NotSupportedException>(() => provider.GetDefaultSchemaName());
            Assert.ThrowsException<NotImplementedException>(
                () => provider.PopulateTriggerMetadataForTable("Book", "dbo", "books", new SourceDefinition()));

            MethodInfo populateLinkingObject = GetBaseMethod("PopulateMetadataForLinkingObject");
            populateLinkingObject.Invoke(provider, new object[]
            {
                "Book", "Author", "dbo.book_authors", new Dictionary<string, DatabaseObject>()
            });

            MethodInfo generateAutoentities = GetBaseMethod("GenerateAutoentitiesIntoEntities");
            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(
                () => generateAutoentities.Invoke(provider, new object?[] { null }));
            Assert.IsInstanceOfType<NotSupportedException>(exception.InnerException);
        }

        [TestMethod]
        public void GetForeignKeyQueryParams_GeneratesSchemaAndTableParameters()
        {
            MsSqlMetadataProvider provider = CreateProvider();
            MethodInfo method = GetBaseMethod("GetForeignKeyQueryParams");

            Dictionary<string, DbConnectionParam> parameters =
                (Dictionary<string, DbConnectionParam>)method.Invoke(provider, new object[]
                {
                    new[] { "dbo", "sales" }, new[] { "books", "orders" }
                })!;

            Assert.AreEqual(4, parameters.Count);
            CollectionAssert.AreEquivalent(
                new object[] { "dbo", "sales", "books", "orders" },
                new List<object>(System.Linq.Enumerable.Select(parameters.Values, parameter => parameter.Value)));
        }

        [TestMethod]
        public async System.Threading.Tasks.Task FillSchemaForStoredProcedureAsync_TranslatesOfflineFailure()
        {
            BaseBehaviorMetadataProvider provider =
                (BaseBehaviorMetadataProvider)RuntimeHelpers.GetUninitializedObject(typeof(BaseBehaviorMetadataProvider));
            MethodInfo method = GetBaseMethod("FillSchemaForStoredProcedureAsync");
            Entity procedure = new(
                Source: new EntitySource("dbo.get_books", EntitySourceType.StoredProcedure, null, null),
                GraphQL: null,
                Fields: null,
                Rest: null,
                Permissions: Array.Empty<EntityPermission>(),
                Mappings: null,
                Relationships: null);

            System.Threading.Tasks.Task task = (System.Threading.Tasks.Task)method.Invoke(provider, new object[]
            {
                procedure, "Book", "dbo", "get_books", new StoredProcedureDefinition()
            })!;

            DataApiBuilderException exception =
                await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() => task);
            StringAssert.Contains(exception.Message, "Cannot obtain Schema for entity Book");
        }

        [TestMethod]
        public void LogPrimaryKeys_RecordsEntityFailureDuringValidation()
        {
            MsSqlMetadataProvider provider = CreateProvider(entity: CreateEntity());
            SetBaseField(provider, "_isValidateOnly", true);
            SetBaseAutoProperty(provider, "SqlMetadataExceptions", new List<Exception>());

            GetBaseMethod("LogPrimaryKeys").Invoke(provider, null);

            Assert.AreEqual(1, provider.SqlMetadataExceptions.Count);
            Assert.IsInstanceOfType<DataApiBuilderException>(provider.SqlMetadataExceptions[0]);
        }

        [TestMethod]
        public void GenerateRestPathToEntityMap_RecordsConflictingPathDuringValidation()
        {
            Entity entity = new(
                Source: new EntitySource("dbo.books", EntitySourceType.Table, null, null),
                GraphQL: null,
                Fields: null,
                Rest: new EntityRestOptions(Enabled: true, Path: "/graphql"),
                Permissions: Array.Empty<EntityPermission>(),
                Mappings: null,
                Relationships: null);
            MsSqlMetadataProvider provider = CreateProvider(entity: entity);
            SetBaseField(provider, "_isValidateOnly", true);
            SetBaseAutoProperty(provider, "SqlMetadataExceptions", new List<Exception>());

            GetBaseMethod("GenerateRestPathToEntityMap").Invoke(provider, null);

            Assert.AreEqual(1, provider.SqlMetadataExceptions.Count);
            Assert.IsInstanceOfType<DataApiBuilderException>(provider.SqlMetadataExceptions[0]);
        }

        private static MsSqlMetadataProvider CreateProvider(
            DatabaseObject? databaseObject = null,
            Entity? entity = null)
        {
            MsSqlMetadataProvider provider = (MsSqlMetadataProvider)RuntimeHelpers.GetUninitializedObject(typeof(MsSqlMetadataProvider));
            provider.EntityToDatabaseObject = new Dictionary<string, DatabaseObject>(StringComparer.InvariantCulture);
            if (databaseObject is not null)
            {
                provider.EntityToDatabaseObject.Add("Book", databaseObject);
            }

            SetBaseField(provider, "_databaseType", DatabaseType.MSSQL);
            SetBaseField(provider, "_linkingEntities", new Dictionary<string, Entity>());
            SetBaseAutoProperty(provider, "EntityBackingColumnsToExposedNames", new Dictionary<string, Dictionary<string, string>>());
            SetBaseAutoProperty(provider, "EntityExposedNamesToBackingColumnNames", new Dictionary<string, Dictionary<string, string>>());
            SetBaseField(provider, "_logger", Microsoft.Extensions.Logging.Abstractions.NullLogger<ISqlMetadataProvider>.Instance);

            Dictionary<string, Entity> entities = entity is null
                ? new Dictionary<string, Entity>()
                : new Dictionary<string, Entity> { ["Book"] = entity };
            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty),
                Entities: new RuntimeEntities(entities));
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            SetBaseField(provider, "_runtimeConfigProvider", configProvider);
            typeof(MsSqlMetadataProvider).GetField("_runtimeConfigProvider", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(provider, configProvider);
            SetBaseField(provider, "_dataSourceName", configProvider.GetConfig().DefaultDataSourceName);
            return provider;
        }

        private static Entity CreateEntity(List<FieldMetadata>? fields = null, string singular = "Book") =>
            new(
                Source: new EntitySource("dbo.books", EntitySourceType.Table, null, null),
                GraphQL: new EntityGraphQLOptions(singular, "Books"),
                Fields: fields,
                Rest: new EntityRestOptions(Enabled: true),
                Permissions: Array.Empty<EntityPermission>(),
                Mappings: null,
                Relationships: null);

        private static Dictionary<string, Dictionary<string, string>> GetMap(MsSqlMetadataProvider provider, string propertyName) =>
            (Dictionary<string, Dictionary<string, string>>)typeof(MsSqlMetadataProvider).BaseType!
                .GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(provider)!;

        private static MethodInfo GetBaseMethod(string methodName) =>
            typeof(MsSqlMetadataProvider).BaseType!.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static MethodInfo GetMsSqlMethod(string methodName) =>
            typeof(MsSqlMetadataProvider).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static void ConfigureAutoentityQuery(MsSqlMetadataProvider provider, JsonArray result)
        {
            Mock<IQueryExecutor> queryExecutor = new();
            queryExecutor.Setup(x => x.ExecuteQueryAsync(
                    It.IsAny<string>(),
                    It.IsAny<IDictionary<string, DbConnectionParam>>(),
                    It.IsAny<Func<System.Data.Common.DbDataReader, List<string>?, System.Threading.Tasks.Task<JsonArray>>>(),
                    It.IsAny<string>(),
                    It.IsAny<Microsoft.AspNetCore.Http.HttpContext>(),
                    It.IsAny<List<string>>()))
                .ReturnsAsync(result);
            Mock<IQueryBuilder> queryBuilder = new();
            queryBuilder.Setup(x => x.BuildGetAutoentitiesQuery()).Returns("SELECT autoentities");
            SetBaseAutoProperty(provider, "QueryExecutor", queryExecutor.Object);
            SetBaseAutoProperty(provider, "SqlQueryBuilder", queryBuilder.Object);
        }

        private static void SetBaseField(MsSqlMetadataProvider provider, string fieldName, object value) =>
            typeof(MsSqlMetadataProvider).BaseType!.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .SetValue(provider, value);

        private static void SetBaseAutoProperty(MsSqlMetadataProvider provider, string propertyName, object value) =>
            typeof(MsSqlMetadataProvider).BaseType!.GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(provider, value);

        private sealed class BaseBehaviorMetadataProvider : SqlMetadataProvider<SqlConnection, SqlDataAdapter, SqlCommand>
        {
            public BaseBehaviorMetadataProvider(
                RuntimeConfigProvider runtimeConfigProvider,
                RuntimeConfigValidator runtimeConfigValidator,
                IAbstractQueryManagerFactory engineFactory,
                ILogger<ISqlMetadataProvider> logger,
                string dataSourceName)
                : base(runtimeConfigProvider, runtimeConfigValidator, engineFactory, logger, dataSourceName)
            {
            }

            public override Type SqlToCLRType(string sqlType) => typeof(string);
        }
    }
}
