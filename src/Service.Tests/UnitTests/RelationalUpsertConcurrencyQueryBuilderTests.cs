// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Verifies that insert-capable relational upserts serialize the existence decision before choosing
    /// between UPDATE and INSERT. Update-only fallback queries do not need the additional serialization.
    /// </summary>
    [TestClass]
    public class RelationalUpsertConcurrencyQueryBuilderTests
    {
        private const string ENTITY_NAME = "Book";
        private const string SCHEMA_NAME = "dbo";
        private const string TABLE_NAME = "books";

        private delegate void TryGetColumnCallback(string entity, string field, out string? column);

        /// <summary>
        /// SQL Server must hold an update/key-range lock while deciding whether a composite-key row exists.
        /// </summary>
        [TestMethod]
        public void MsSqlInsertCapableUpsertLocksCompleteCompositeKeyBeforeExistenceCheck()
        {
            SqlUpsertQueryStructure structure = CreateUpsertStructure(DatabaseType.MSSQL, useCompositePrimaryKey: true);

            string query = new MsSqlQueryBuilder().Build(structure);

            const string lockingExistenceCheck =
                "FROM [dbo].[books] WITH (UPDLOCK, HOLDLOCK) WHERE [dbo].[books].[tenant_id] = @param0 AND [dbo].[books].[id] = @param1";
            Assert.IsTrue(
                query.Contains(lockingExistenceCheck, StringComparison.Ordinal),
                $"Expected the SQL Server existence check to lock the complete composite key. Query: {query}");
            AssertAppearsBefore(query, lockingExistenceCheck, "IF @ROWS_TO_UPDATE = 1");
        }

        /// <summary>
        /// PostgreSQL can use a key-scoped transaction lock for representation-stable key types. The complete
        /// composite key must be included in metadata order so upserts for different keys remain concurrent.
        /// </summary>
        [TestMethod]
        public void PostgreSqlRepresentationStableKeyUpsertLocksCompleteKeyBeforeExistenceCheck()
        {
            SqlUpsertQueryStructure structure = CreateUpsertStructure(DatabaseType.PostgreSQL, useCompositePrimaryKey: true);

            string query = new PostgresQueryBuilder().Build(structure);

            const string advisoryLock =
                "SELECT pg_advisory_xact_lock(hashtextextended(jsonb_build_array('dbo', 'books', 'id', @param1, 'tenant_id', @param0)::text, 0))";
            Assert.IsTrue(
                query.Contains(advisoryLock, StringComparison.Ordinal),
                $"Expected the PostgreSQL advisory lock to identify the complete representation-stable key. Query: {query}");
            AssertAppearsBefore(query, advisoryLock, "SELECT COUNT(*) AS cnt_rows_to_update");
        }

        /// <summary>
        /// PostgreSQL UUID values are converted to Guid before binding, so equivalent textual UUID forms
        /// produce the same key-scoped lock resource.
        /// </summary>
        [TestMethod]
        public void PostgreSqlGuidKeyUpsertUsesKeyScopedLock()
        {
            SqlUpsertQueryStructure structure = CreateUpsertStructure(
                DatabaseType.PostgreSQL,
                useCompositePrimaryKey: false,
                primaryKeySystemType: typeof(Guid));

            string query = new PostgresQueryBuilder().Build(structure);

            const string advisoryLock =
                "SELECT pg_advisory_xact_lock(hashtextextended(jsonb_build_array('dbo', 'books', 'id', @param0)::text, 0))";
            Assert.IsTrue(
                query.Contains(advisoryLock, StringComparison.Ordinal),
                $"Expected the PostgreSQL advisory lock to identify the UUID key. Query: {query}");
            AssertAppearsBefore(query, advisoryLock, "SELECT COUNT(*) AS cnt_rows_to_update");
        }

        /// <summary>
        /// PostgreSQL string equality can depend on the backing type and collation, so string keys must use
        /// the source-scoped fallback rather than deriving a lock from the request representation.
        /// </summary>
        [TestMethod]
        public void PostgreSqlStringKeyUpsertFallsBackToSourceLock()
        {
            SqlUpsertQueryStructure structure = CreateUpsertStructure(
                DatabaseType.PostgreSQL,
                useCompositePrimaryKey: false,
                primaryKeySystemType: typeof(string));

            string query = new PostgresQueryBuilder().Build(structure);

            const string advisoryLock =
                "SELECT pg_advisory_xact_lock(hashtextextended(jsonb_build_array('dbo', 'books')::text, 0))";
            Assert.IsTrue(
                query.Contains(advisoryLock, StringComparison.Ordinal),
                $"Expected the PostgreSQL advisory lock to fall back to source scope. Query: {query}");
            Assert.IsFalse(
                query[..query.IndexOf(';')].Contains("@param", StringComparison.Ordinal),
                $"PostgreSQL source lock identity must not depend on string key representations. Query: {query}");
            Assert.IsTrue(
                structure.Parameters["@param0"].UseDatabaseTypeInference,
                "PostgreSQL string upsert keys must use the backing column's native comparison semantics.");
            Assert.IsFalse(
                structure.Parameters["@param1"].UseDatabaseTypeInference,
                "Non-key string values must retain normal Npgsql parameter typing.");
            AssertAppearsBefore(query, advisoryLock, "SELECT COUNT(*) AS cnt_rows_to_update");
        }

        /// <summary>
        /// Data Warehouse SQL must hold an exclusive source-table lock before the existence decision because
        /// configured logical keys are not necessarily backed by an enforced unique constraint.
        /// </summary>
        [TestMethod]
        public void DwSqlInsertCapableUpsertLocksSourceTableBeforeExistenceCheck()
        {
            SqlUpsertQueryStructure structure = CreateUpsertStructure(DatabaseType.DWSQL, useCompositePrimaryKey: true);

            string query = new DwSqlQueryBuilder().Build(structure);

            const string lockingExistenceCheck =
                "FROM [dbo].[books] WITH (TABLOCKX, HOLDLOCK) WHERE [dbo].[books].[tenant_id] = @param0 AND [dbo].[books].[id] = @param1";
            Assert.IsTrue(
                query.Contains(lockingExistenceCheck, StringComparison.Ordinal),
                $"Expected the Data Warehouse SQL existence check to hold an exclusive source-table lock. Query: {query}");
            AssertAppearsBefore(query, lockingExistenceCheck, "IF @ROWS_TO_UPDATE = 1");
        }

        /// <summary>
        /// An autogenerated primary key makes the upsert update-only, so no insert race exists and the
        /// insert-path serialization primitives must not be emitted.
        /// </summary>
        [TestMethod]
        public void UpdateOnlyFallbackUpsertsDoNotAcquireInsertSerializationLocks()
        {
            SqlUpsertQueryStructure msSqlStructure = CreateUpsertStructure(DatabaseType.MSSQL, useCompositePrimaryKey: false, autoGeneratedPrimaryKey: true);
            SqlUpsertQueryStructure postgreSqlStructure = CreateUpsertStructure(DatabaseType.PostgreSQL, useCompositePrimaryKey: false, autoGeneratedPrimaryKey: true);
            SqlUpsertQueryStructure dwSqlStructure = CreateUpsertStructure(DatabaseType.DWSQL, useCompositePrimaryKey: false, autoGeneratedPrimaryKey: true);

            string msSqlQuery = new MsSqlQueryBuilder().Build(msSqlStructure);
            string postgreSqlQuery = new PostgresQueryBuilder().Build(postgreSqlStructure);
            string dwSqlQuery = new DwSqlQueryBuilder().Build(dwSqlStructure);

            Assert.IsFalse(msSqlQuery.Contains("UPDLOCK", StringComparison.Ordinal), $"Update-only SQL Server query should not acquire an insert serialization lock. Query: {msSqlQuery}");
            Assert.IsFalse(postgreSqlQuery.Contains("pg_advisory_xact_lock", StringComparison.Ordinal), $"Update-only PostgreSQL query should not acquire an insert serialization lock. Query: {postgreSqlQuery}");
            Assert.IsFalse(dwSqlQuery.Contains("TABLOCKX", StringComparison.Ordinal), $"Update-only Data Warehouse SQL query should not acquire an insert serialization lock. Query: {dwSqlQuery}");
        }

        private static void AssertAppearsBefore(string query, string first, string second)
        {
            int firstIndex = query.IndexOf(first, StringComparison.Ordinal);
            int secondIndex = query.IndexOf(second, StringComparison.Ordinal);

            Assert.IsTrue(firstIndex >= 0, $"Expected query fragment was not found: {first}. Query: {query}");
            Assert.IsTrue(secondIndex >= 0, $"Expected query fragment was not found: {second}. Query: {query}");
            Assert.IsTrue(firstIndex < secondIndex, $"Expected '{first}' to appear before '{second}'. Query: {query}");
        }

        private static SqlUpsertQueryStructure CreateUpsertStructure(
            DatabaseType databaseType,
            bool useCompositePrimaryKey,
            bool autoGeneratedPrimaryKey = false,
            Type? primaryKeySystemType = null)
        {
            primaryKeySystemType ??= typeof(int);
            DbType primaryKeyDbType = DbType.Int32;
            object primaryKeyValue = 42;
            if (primaryKeySystemType == typeof(string))
            {
                primaryKeyDbType = DbType.String;
                primaryKeyValue = "book-42";
            }
            else if (primaryKeySystemType == typeof(Guid))
            {
                primaryKeyDbType = DbType.Guid;
                primaryKeyValue = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            }

            SourceDefinition sourceDefinition = new()
            {
                PrimaryKey = useCompositePrimaryKey ? new() { "id", "tenant_id" } : new() { "id" }
            };
            sourceDefinition.Columns.Add("id", new ColumnDefinition
            {
                SystemType = primaryKeySystemType,
                DbType = primaryKeyDbType,
                IsAutoGenerated = autoGeneratedPrimaryKey
            });
            if (useCompositePrimaryKey)
            {
                sourceDefinition.Columns.Add("tenant_id", new ColumnDefinition
                {
                    SystemType = typeof(int),
                    DbType = DbType.Int32
                });
            }

            sourceDefinition.Columns.Add("title", new ColumnDefinition
            {
                SystemType = typeof(string),
                DbType = DbType.String,
                IsNullable = true
            });

            DatabaseTable dbTable = new(SCHEMA_NAME, TABLE_NAME)
            {
                TableDefinition = sourceDefinition,
                SourceType = EntitySourceType.Table
            };

            Dictionary<string, string> columnMapping = new()
            {
                { "id", "id" },
                { "title", "title" }
            };
            if (useCompositePrimaryKey)
            {
                columnMapping.Add("tenant_id", "tenant_id");
            }

            Mock<ISqlMetadataProvider> metadataProvider = new();
            metadataProvider.Setup(x => x.EntityToDatabaseObject)
                .Returns(new Dictionary<string, DatabaseObject> { { ENTITY_NAME, dbTable } });
            metadataProvider.Setup(x => x.GetSourceDefinition(ENTITY_NAME)).Returns(sourceDefinition);
            metadataProvider.Setup(x => x.GetDatabaseType()).Returns(databaseType);

            string? outColumn;
            metadataProvider.Setup(x => x.TryGetBackingColumn(It.IsAny<string>(), It.IsAny<string>(), out outColumn))
                .Callback(new TryGetColumnCallback((string entity, string field, out string? column)
                    => columnMapping.TryGetValue(field, out column)))
                .Returns((string entity, string field, string? column) => columnMapping.ContainsKey(field));

            string? outExposed;
            metadataProvider.Setup(x => x.TryGetExposedColumnName(It.IsAny<string>(), It.IsAny<string>(), out outExposed))
                .Callback(new TryGetColumnCallback((string entity, string field, out string? column)
                    => columnMapping.TryGetValue(field, out column)))
                .Returns((string entity, string field, string? column) => columnMapping.ContainsKey(field));

            Mock<IAuthorizationResolver> authorizationResolver = new();
            authorizationResolver
                .Setup(x => x.ResolveDBPolicy(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<EntityActionOperation>(),
                    It.IsAny<HttpContext>()))
                .Returns(ResolvedDatabasePolicy.Empty);

            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestHelper.GetRuntimeConfigLoader());
            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            GQLFilterParser gQLFilterParser = new(runtimeConfigProvider, metadataProviderFactory.Object);

            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = "authenticated";

            Dictionary<string, object?> mutationParams = useCompositePrimaryKey
                ? new()
                {
                    { "tenant_id", 7 },
                    { "id", primaryKeyValue },
                    { "title", "The Hobbit" }
                }
                : new()
                {
                    { "id", primaryKeyValue },
                    { "title", "The Hobbit" }
                };

            return new SqlUpsertQueryStructure(
                entityName: ENTITY_NAME,
                sqlMetadataProvider: metadataProvider.Object,
                authorizationResolver: authorizationResolver.Object,
                gQLFilterParser: gQLFilterParser,
                mutationParams: mutationParams,
                incrementalUpdate: false,
                httpContext: httpContext);
        }
    }
}
