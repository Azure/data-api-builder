// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Verifies that insert-capable relational upserts serialize the existence decision before
    /// choosing between UPDATE and INSERT.
    /// </summary>
    [TestClass]
    public class SqlUpsertConcurrencyQueryBuilderTests
    {
        /// <summary>
        /// SQL Server must take a held update/key-range lock before it checks whether the PK exists.
        /// </summary>
        [TestMethod]
        public void MsSqlUpsertSerializesPrimaryKeyExistenceCheck()
        {
            SqlUpsertQueryStructure structure = SqlUpsertQueryStructureTestHelper.Create(DatabaseType.MSSQL);

            string query = new MsSqlQueryBuilder().Build(structure);

            AssertLockPrecedesBranchDecision(query, "WITH (UPDLOCK, HOLDLOCK)");
            AssertUpdatePolicyIsPreserved(structure, "[piecesAvailable] > 0");
        }

        /// <summary>
        /// PostgreSQL must acquire a transaction-level advisory lock in a separate statement before
        /// taking the existence-check snapshot. The key must include all PK parameters and no value
        /// that belongs only to the update payload.
        /// </summary>
        [TestMethod]
        public void PostgreSqlUpsertLocksCompositePrimaryKeyBeforeExistenceCheck()
        {
            SqlUpsertQueryStructure structure = SqlUpsertQueryStructureTestHelper.Create(DatabaseType.PostgreSQL);
            const string updatePolicy = "\"piecesAvailable\" > 0";
            structure.DbPolicyPredicatesForOperations[EntityActionOperation.Update] = updatePolicy;

            string query = new PostgresQueryBuilder().Build(structure);

            int lockIndex = query.IndexOf("pg_advisory_xact_lock", StringComparison.Ordinal);
            Assert.IsTrue(lockIndex >= 0, $"Expected a PostgreSQL transaction advisory lock. Query: {query}");

            int firstStatementEnd = query.IndexOf(';', lockIndex);
            int countIndex = query.IndexOf(PostgresQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK, StringComparison.Ordinal);
            Assert.IsTrue(firstStatementEnd > lockIndex, $"The advisory lock must be a separate statement. Query: {query}");
            Assert.IsTrue(countIndex > firstStatementEnd, $"The existence check must run after lock acquisition. Query: {query}");
            Assert.IsTrue(
                query[..firstStatementEnd].Contains(PostgresQueryBuilder.UPSERT_LOCK_RESULT, StringComparison.Ordinal),
                $"The lock result must use the executor's expected alias. Query: {query}");

            string lockStatement = query[..firstStatementEnd];
            Assert.IsTrue(lockStatement.Contains("'dbo'", StringComparison.Ordinal), $"Lock key must include the schema. Query: {query}");
            Assert.IsTrue(lockStatement.Contains("'stocks'", StringComparison.Ordinal), $"Lock key must include the source. Query: {query}");
            Assert.IsTrue(lockStatement.Contains("@param0", StringComparison.Ordinal), $"Lock key must include the first PK parameter. Query: {query}");
            Assert.IsTrue(lockStatement.Contains("@param1", StringComparison.Ordinal), $"Lock key must include the second PK parameter. Query: {query}");
            Assert.IsFalse(lockStatement.Contains("@param2", StringComparison.Ordinal), $"Lock key must exclude non-PK payload parameters. Query: {query}");
            Assert.IsFalse(lockStatement.Contains("@param3", StringComparison.Ordinal), $"Lock key must exclude non-PK payload parameters. Query: {query}");
            Assert.IsTrue(query.Contains(updatePolicy, StringComparison.Ordinal), $"Serialization must not remove the update database policy. Query: {query}");
        }

        /// <summary>
        /// Data Warehouse SQL cannot rely on an enforced unique key, so insert-capable upserts must
        /// hold an exclusive source-table lock across the decision and mutation.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.DWSQL)]
        public void DwSqlInsertCapableUpsertSerializesSourceTable()
        {
            SqlUpsertQueryStructure structure = SqlUpsertQueryStructureTestHelper.Create(DatabaseType.DWSQL);

            string query = new DwSqlQueryBuilder().Build(structure);

            AssertLockPrecedesBranchDecision(query, "WITH (TABLOCKX, HOLDLOCK)");
            AssertUpdatePolicyIsPreserved(structure, "[piecesAvailable] > 0");
        }

        /// <summary>
        /// An update-only Data Warehouse SQL request cannot create a duplicate and should not take
        /// the deliberately broad exclusive table lock.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.DWSQL)]
        public void DwSqlUpdateOnlyFallbackDoesNotTakeExclusiveTableLock()
        {
            SqlUpsertQueryStructure structure = SqlUpsertQueryStructureTestHelper.Create(
                DatabaseType.DWSQL,
                hasAutoGeneratedPrimaryKey: true);

            string query = new DwSqlQueryBuilder().Build(structure);

            Assert.IsTrue(structure.IsFallbackToUpdate, "The test structure must use the update-only path.");
            Assert.IsFalse(
                query.Contains("TABLOCKX", StringComparison.Ordinal),
                $"Update-only fallback should not serialize the entire source table. Query: {query}");
        }

        private static void AssertLockPrecedesBranchDecision(string query, string expectedLockHint)
        {
            int lockIndex = query.IndexOf(expectedLockHint, StringComparison.Ordinal);
            int branchIndex = query.IndexOf("IF @ROWS_TO_UPDATE = 1", StringComparison.Ordinal);

            Assert.IsTrue(lockIndex >= 0, $"Expected lock hint '{expectedLockHint}'. Query: {query}");
            Assert.IsTrue(branchIndex > lockIndex, $"The lock must be taken before the insert/update decision. Query: {query}");
        }

        private static void AssertUpdatePolicyIsPreserved(
            SqlUpsertQueryStructure structure,
            string updatePolicy)
        {
            structure.DbPolicyPredicatesForOperations[EntityActionOperation.Update] = updatePolicy;

            string rebuiltQuery = structure.MetadataProvider.GetDatabaseType() switch
            {
                DatabaseType.MSSQL => new MsSqlQueryBuilder().Build(structure),
                DatabaseType.DWSQL => new DwSqlQueryBuilder().Build(structure),
                _ => throw new AssertFailedException("This assertion supports only T-SQL builders.")
            };

            int updateIndex = rebuiltQuery.IndexOf("UPDATE", StringComparison.Ordinal);
            int updateBranchEnd = rebuiltQuery.IndexOf("END", updateIndex, StringComparison.Ordinal);
            Assert.IsTrue(updateIndex >= 0 && updateBranchEnd > updateIndex, $"Expected an UPDATE branch. Query: {rebuiltQuery}");

            string updateBranch = rebuiltQuery[updateIndex..updateBranchEnd];
            Assert.IsTrue(
                updateBranch.Contains(updatePolicy, StringComparison.Ordinal),
                $"Serialization must not remove the update database policy. Query: {rebuiltQuery}");
        }
    }
}
