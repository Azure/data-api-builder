using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL.Policies.Mutation.Delete
{
    /// <summary>
    /// Tests Database Authorization Policies applied to GraphQL Queries
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGraphQLDeleteMutationPolicyTests : GraphQLDeleteMutationDatabasePolicyTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture(context);
        }

        /// <summary>
        /// Tests Authenticated GraphQL Delete Mutation which triggers
        /// policy processing. Tests deleteBook with policy that
        /// allows/prevents operation.
        /// - Operation allowed: confirm record deleted.
        /// - Operation forbidden: confirm record not deleted.
        /// </summary>
        [TestMethod]
        public async Task DeleteMutation_Policy()
        {
            string dbQuery = @"
                SELECT TOP 1
                [table0].[id] AS [id],
                [table0].[title] AS [title]
                FROM [dbo].[books] AS [table0] 
                WHERE ([table0].[id] = 9 and [table0].[title] = 'Policy-Test-01') 
                ORDER BY [table0].[id] ASC 
                FOR JSON PATH, INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER";

            await DeleteMutation_Policy(dbQuery);
        }
    }
}
