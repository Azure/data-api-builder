// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLCollationTests
{
    [TestClass]
    public class GraphQLCollationTestBase : SqlTestBase
    {
        #region Tests

        /// <summary>
        /// Compares SQL Database Query with GraphQL Query
        /// To ensure that GraphQL is working as expected with case sensitive collations
        /// </summary>
        public async Task TestQueryingWithCaseSensitiveCollation(string objectType, string fieldName, string dbQuery, string defaultCollationQuery, string newCollationQuery)
        {
            string graphQLQueryName = objectType;
            string graphQLQuery = @"
                query { " +
                    objectType + @" (orderBy: {" + fieldName + @": ASC" + @"}) {
                        items { "
                            + fieldName + @"
                        }
                    }
                }
            ";

            //Change collation to be case sensitive before executing GraphQL test
            await GetDatabaseResultAsync(newCollationQuery);
            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: true);

            //Change collation back to default before executing expected test
            await GetDatabaseResultAsync(defaultCollationQuery);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }
        #endregion
    }
}
