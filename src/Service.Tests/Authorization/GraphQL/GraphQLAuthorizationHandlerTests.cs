// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL
{
    /// <summary>
    /// Tests Database Authorization Policies applied to GraphQL Queries
    /// These tests are DB Engine agnostic, though require a result to
    /// validate operation completion if no errors are expected.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class GraphQLAuthorizationHandlerTests : SqlTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Integration tests to validate GraphQLAuthorizationHandler functionality utilized by HotChocolate.
        /// </summary>
        /// <param name="isAuthenticated">Bool whether request is authenticated.</param>
        /// <param name="clientRoleHeader">string value to set as clientRoleHeader</param>
        /// <param name="errorExpected">bool whether an error is expected</param>
        /// <param name="expectedErrorMessageFragment">string value of message frament to search for in error.</param>
        /// <returns></returns>
        [TestMethod]
        [DataRow(false, "anonymous", true, "The current user is not authorized to access this resource.", DisplayName = "Unauthenticated request to field with @authorize directive")]
        [DataRow(true, "", true, "The current user is not authorized to access this resource.", DisplayName = "Authenticated, no client role header, accessing to field with @authorize directive")]
        [DataRow(true, "RoleNotDefinedForEntity", true, "The current user is not authorized to access this resource.", DisplayName = "Authenticated, clientRoleHeader does not match @authorize directive.")]
        [DataRow(true, "AuthorizationHandlerTester", false, "", DisplayName = "Authenticated access to field with @authorize directive, valid clientRoleHeader due to case insensitivity")]
        [DataRow(true, "authorizationHandlerTester", false, "", DisplayName = "Authenticated access to field with @authorize directive, valid clientRoleHeader")]
        public async Task FieldAuthorizationProcessing(bool isAuthenticated, string clientRoleHeader, bool errorExpected, string expectedErrorMessageFragment)
        {
            string graphQLQueryName = "journal_by_pk";
            string graphQLQuery = @"{
                journal_by_pk(id: 1) {
                    id,
                    journalname 
                }
                }";
            string expectedResult = @"{ ""id"":1,""journalname"":""Journal1""}";

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                query: graphQLQuery,
                queryName: graphQLQueryName,
                isAuthenticated: isAuthenticated,
                variables: null,
                clientRoleHeader: clientRoleHeader);

            if (!string.IsNullOrWhiteSpace(expectedErrorMessageFragment))
            {
                SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "The current user is not authorized to access this resource.",
                    path: @"[""journal_by_pk""]"
                );
            }
            else
            {
                SqlTestHelper.PerformTestEqualJsonStrings(expectedResult, actual.ToString());
            }
        }
    }
}
