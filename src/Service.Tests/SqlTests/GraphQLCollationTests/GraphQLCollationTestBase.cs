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
        /// </summary>
        public async Task CapitalizationResultQuery(string type, string item, string dbQuery)
        {
            string graphQLQueryName = type;
            string graphQLQuery = @"
                query { " +
                    type + @" (orderBy: {" + item + @": ASC" + @"}) {
                        items { "
                            + item + @"
                        }
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        #endregion
    }
}
