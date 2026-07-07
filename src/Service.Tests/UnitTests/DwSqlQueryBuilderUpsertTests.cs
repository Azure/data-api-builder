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
    /// Regression tests for the Azure Synapse Analytics (dwsql) upsert query builder.
    /// These validate that the generated UPDATE branch of an upsert is always scoped by a
    /// WHERE clause targeting the primary key (and any configured update database policy).
    /// A missing WHERE clause would cause a single PUT upsert to overwrite every row in the
    /// target table (CWE-862).
    /// </summary>
    [TestClass]
    public class DwSqlQueryBuilderUpsertTests
    {
        private const string ENTITY_NAME = "Book";
        private const string SCHEMA_NAME = "dbo";
        private const string TABLE_NAME = "books";

        private delegate void TryGetColumnCallback(string entity, string field, out string? column);

        /// <summary>
        /// Maps exposed field names to backing column names (identity mapping for the test entity).
        /// </summary>
        private static readonly Dictionary<string, string> _columnMapping = new()
        {
            { "id", "id" },
            { "title", "title" },
            { "publisher_id", "publisher_id" }
        };

        /// <summary>
        /// Verifies that the dwsql upsert builder scopes the UPDATE branch with a WHERE clause
        /// containing the primary key predicate and the configured update database policy.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.DWSQL)]
        public void DwSqlUpsertUpdateBranchContainsWhereClauseScopedByPrimaryKeyAndPolicy()
        {
            // Arrange
            const string updateDbPolicy = "([publisher_id] != 0)";

            SqlUpsertQueryStructure structure = CreateUpsertStructure();

            // Simulate an update database policy being defined for the operation.
            structure.DbPolicyPredicatesForOperations[EntityActionOperation.Update] = updateDbPolicy;

            DwSqlQueryBuilder builder = new();

            // Act
            string query = builder.Build(structure);

            // Assert
            Assert.IsTrue(query.Contains("UPDATE", StringComparison.Ordinal), $"Expected an UPDATE statement. Query: {query}");

            // Isolate the UPDATE (IF @ROWS_TO_UPDATE = 1) branch so the assertion targets the UPDATE, not the INSERT.
            int updateIndex = query.IndexOf("UPDATE", StringComparison.Ordinal);
            int endIndex = query.IndexOf("END", updateIndex, StringComparison.Ordinal);
            Assert.IsTrue(endIndex > updateIndex, $"Expected the UPDATE branch to be closed by END. Query: {query}");
            string updateBranch = query.Substring(updateIndex, endIndex - updateIndex);

            Assert.IsTrue(
                updateBranch.Contains("WHERE", StringComparison.Ordinal),
                $"The dwsql upsert UPDATE branch MUST include a WHERE clause to scope the update. Query: {query}");

            Assert.IsTrue(
                updateBranch.Contains("[id]", StringComparison.Ordinal),
                $"The dwsql upsert UPDATE WHERE clause MUST scope by the primary key column. Query: {query}");

            Assert.IsTrue(
                updateBranch.Contains(updateDbPolicy, StringComparison.Ordinal),
                $"The dwsql upsert UPDATE WHERE clause MUST include the update database policy. Query: {query}");
        }

        /// <summary>
        /// Builds a minimal <see cref="SqlUpsertQueryStructure"/> for the test entity using a mocked
        /// metadata provider so no live database is required.
        /// </summary>
        private static SqlUpsertQueryStructure CreateUpsertStructure()
        {
            SourceDefinition sourceDefinition = new()
            {
                PrimaryKey = new() { "id" }
            };
            sourceDefinition.Columns.Add("id", new ColumnDefinition
            {
                SystemType = typeof(int),
                DbType = DbType.Int32
            });
            sourceDefinition.Columns.Add("title", new ColumnDefinition
            {
                SystemType = typeof(string),
                DbType = DbType.String,
                IsNullable = true
            });
            sourceDefinition.Columns.Add("publisher_id", new ColumnDefinition
            {
                SystemType = typeof(int),
                DbType = DbType.Int32
            });

            DatabaseTable dbTable = new(SCHEMA_NAME, TABLE_NAME)
            {
                TableDefinition = sourceDefinition,
                SourceType = EntitySourceType.Table
            };

            Mock<ISqlMetadataProvider> metadataProvider = new();
            metadataProvider.Setup(x => x.EntityToDatabaseObject)
                .Returns(new Dictionary<string, DatabaseObject> { { ENTITY_NAME, dbTable } });
            metadataProvider.Setup(x => x.GetSourceDefinition(ENTITY_NAME)).Returns(sourceDefinition);
            metadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.DWSQL);

            string outColumn;
            metadataProvider.Setup(x => x.TryGetBackingColumn(It.IsAny<string>(), It.IsAny<string>(), out outColumn))
                .Callback(new TryGetColumnCallback((string entity, string field, out string? column)
                    => _columnMapping.TryGetValue(field, out column)))
                .Returns((string entity, string field, string column) => _columnMapping.ContainsKey(field));

            string outExposed;
            metadataProvider.Setup(x => x.TryGetExposedColumnName(It.IsAny<string>(), It.IsAny<string>(), out outExposed))
                .Callback(new TryGetColumnCallback((string entity, string field, out string? column)
                    => _columnMapping.TryGetValue(field, out column)))
                .Returns((string entity, string field, string column) => _columnMapping.ContainsKey(field));

            // The update policy is injected directly onto the structure, so the resolver only needs
            // to return an empty policy (no throw) during construction.
            Mock<IAuthorizationResolver> authorizationResolver = new();
            authorizationResolver
                .Setup(x => x.ProcessDBPolicy(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>(), It.IsAny<HttpContext>()))
                .Returns(string.Empty);

            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestHelper.GetRuntimeConfigLoader());
            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            GQLFilterParser gQLFilterParser = new(runtimeConfigProvider, metadataProviderFactory.Object);

            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = "authenticated";

            Dictionary<string, object?> mutationParams = new()
            {
                { "id", 1 },
                { "title", "The Hobbit Returns to The Shire" },
                { "publisher_id", 1234 }
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
