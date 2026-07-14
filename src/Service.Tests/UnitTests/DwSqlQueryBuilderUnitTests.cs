// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="DwSqlQueryBuilder"/> query generation that can run without a
    /// live database by mocking the metadata provider.
    /// </summary>
    [TestClass]
    public class DwSqlQueryBuilderUnitTests
    {
        private const string ENTITY = "Stock";
        private const string SCHEMA = "dbo";
        private const string TABLE = "stocks";

        /// <summary>
        /// Validates that the UPDATE branch of a DwSql upsert (PUT) is constrained by a WHERE clause
        /// that combines the primary-key predicate with the update-operation database policy.
        ///
        /// Regression guard: previously the UPDATE branch emitted only "UPDATE ... SET ..." with no
        /// WHERE clause, which (a) applied the update to every row in the table and (b) dropped any
        /// configured row-level update policy. This test fails against that prior behavior and passes
        /// once the PK + update policy predicates are appended to the UPDATE statement.
        /// </summary>
        [TestMethod]
        public void DwSqlUpsert_UpdateBranch_AppliesPrimaryKeyAndUpdatePolicyPredicates()
        {
            ISqlMetadataProvider metadataProvider = CreateStocksMetadataProviderMock();

            // Mutation parameters for a PUT targeting an existing composite-PK row.
            Dictionary<string, object?> mutationParams = new()
            {
                { "categoryid", 100 },
                { "pieceid", 99 },
                { "categoryName", "SciFi" },
                { "piecesAvailable", 4 },
                { "piecesRequired", 5 },
            };

            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = "database_policy_tester";

            // The database policy processing (claims -> predicate) is exercised elsewhere. Here we return
            // an empty policy from the resolver and inject the resolved predicate directly (below) so the
            // test isolates the query builder's use of the predicate.
            Mock<IAuthorizationResolver> authorizationResolver = new();
            authorizationResolver
                .Setup(x => x.ProcessDBPolicy(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>(), It.IsAny<HttpContext>()))
                .Returns(string.Empty);

            SqlUpsertQueryStructure upsertStructure = new(
                entityName: ENTITY,
                sqlMetadataProvider: metadataProvider,
                authorizationResolver: authorizationResolver.Object,
                gQLFilterParser: null!,
                mutationParams: mutationParams,
                incrementalUpdate: false,
                httpContext: httpContext);

            // Simulate a resolved row-level database policy for the update operation,
            // e.g. config policy "@item.pieceid ne 1".
            const string updatePolicyPredicate = "([pieceid] != @pol_param0)";
            upsertStructure.DbPolicyPredicatesForOperations[EntityActionOperation.Update] = updatePolicyPredicate;

            DwSqlQueryBuilder builder = new();
            string query = builder.Build(upsertStructure);

            // The UPDATE branch must be filtered by a WHERE clause.
            Assert.IsTrue(query.Contains("UPDATE") && query.Contains("SET") && query.Contains("WHERE"),
                $"Generated upsert query is missing a WHERE clause on the UPDATE branch:{Environment.NewLine}{query}");

            // Primary-key columns must appear in the generated predicates.
            StringAssert.Contains(query, "[categoryid]");
            StringAssert.Contains(query, "[pieceid]");

            // The update database policy predicate must be included in the generated query.
            StringAssert.Contains(query, updatePolicyPredicate);

            // The SET clause must be followed by a WHERE before the IF/END block closes, i.e.
            // "UPDATE ... SET ... WHERE ..." rather than "UPDATE ... SET ... END".
            int updateIdx = query.IndexOf("UPDATE", StringComparison.Ordinal);
            int setIdx = query.IndexOf("SET", updateIdx, StringComparison.Ordinal);
            int whereIdx = query.IndexOf("WHERE", setIdx, StringComparison.Ordinal);
            int endIdx = query.IndexOf("END", updateIdx, StringComparison.Ordinal);
            Assert.IsTrue(setIdx > updateIdx && whereIdx > setIdx && (endIdx == -1 || whereIdx < endIdx),
                $"UPDATE branch must contain a WHERE clause after SET and before the IF/END block terminates:{Environment.NewLine}{query}");
        }

        /// <summary>
        /// Builds a mocked <see cref="ISqlMetadataProvider"/> describing a composite-primary-key
        /// "stocks" table so a <see cref="SqlUpsertQueryStructure"/> can be constructed without a database.
        /// </summary>
        private static ISqlMetadataProvider CreateStocksMetadataProviderMock()
        {
            SourceDefinition sourceDefinition = new();
            sourceDefinition.Columns.Add("categoryid", new ColumnDefinition { SystemType = typeof(int) });
            sourceDefinition.Columns.Add("pieceid", new ColumnDefinition { SystemType = typeof(int) });
            sourceDefinition.Columns.Add("categoryName", new ColumnDefinition { SystemType = typeof(string), IsNullable = true });
            sourceDefinition.Columns.Add("piecesAvailable", new ColumnDefinition { SystemType = typeof(int), IsNullable = true });
            sourceDefinition.Columns.Add("piecesRequired", new ColumnDefinition { SystemType = typeof(int), IsNullable = true });
            sourceDefinition.PrimaryKey.Add("categoryid");
            sourceDefinition.PrimaryKey.Add("pieceid");

            DatabaseTable databaseTable = new(SCHEMA, TABLE)
            {
                SourceType = EntitySourceType.Table,
                TableDefinition = sourceDefinition
            };

            Mock<ISqlMetadataProvider> metadataProvider = new();
            metadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.DWSQL);
            metadataProvider.Setup(x => x.EntityToDatabaseObject)
                .Returns(new Dictionary<string, DatabaseObject> { { ENTITY, databaseTable } });
            metadataProvider.Setup(x => x.GetSourceDefinition(ENTITY)).Returns(sourceDefinition);

            // Identity mapping between exposed names and backing columns.
            string? backingOut;
            metadataProvider
                .Setup(x => x.TryGetBackingColumn(ENTITY, It.IsAny<string>(), out backingOut))
                .Callback(new TryGetColumnCallback((string entity, string field, out string? backing) => backing = field))
                .Returns(true);

            string? exposedOut;
            metadataProvider
                .Setup(x => x.TryGetExposedColumnName(ENTITY, It.IsAny<string>(), out exposedOut))
                .Callback(new TryGetColumnCallback((string entity, string field, out string? exposed) => exposed = field))
                .Returns(true);

            return metadataProvider.Object;
        }

        private delegate void TryGetColumnCallback(string entity, string field, out string? result);
    }
}
