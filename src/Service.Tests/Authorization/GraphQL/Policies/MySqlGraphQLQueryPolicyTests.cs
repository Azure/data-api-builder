// **************************************
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// @file: MySqlGraphQLQueryPolicyTests.cs
// **************************************

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL.Policies
{
    /// <summary>
    /// Tests Database Authorization Policies applied to GraphQL Queries
    /// </summary>
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGraphQLQueryPolicyTests : GraphQLQueryDatabasePolicyTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MYSQL;
            await InitializeTestFixture(context);
        }

        /// <summary>
        /// Tests Authenticated GraphQL Queries which trigger
        /// policy processing. Tests QueryByPK with policies that
        /// filter results:
        /// - To 0 records to detect expected null result
        /// - To 1 record to validate result returns as expected.
        /// </summary>
        [TestMethod]
        public async Task QueryByPK_Policy()
        {
            string dbQuery = @"
            SELECT JSON_OBJECT('id', `subq`.`id`, 'title', `subq`.`title`) AS `data` 
            FROM ( 
                SELECT `table0`.`id` AS `id`, `table0`.`title` AS `title` 
                FROM `books` AS `table0` 
                WHERE (`title` = 'Policy-Test-01') AND `table0`.`id` = 9 
                ORDER BY `table0`.`id` ASC LIMIT 1 
            ) AS `subq`
            ";

            await QueryByPK_Policy(dbQuery);
        }

        /// <summary>
        /// Tests a GraphQL query that may fetch multiple result records,
        /// but does not include any nested queries.
        /// When a policy is applied to such top-level query, results are restricted
        /// to the expected records.
        /// </summary>
        [TestMethod]
        public async Task QueryMany_Policy()
        {
            // Tests Book Read Policy: @item.title ne 'Policy-Test-01'
            // Due to restrictive book policy, expects all book records except:
            // id: 9 title: 'Policy-Test-01'
            string dbQuery = @"
            SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq4`.`id`, 'title', `subq4`.`title`)), JSON_ARRAY()) AS `data` FROM (
                SELECT `table0`.`id` AS `id`, `table0`.`title` AS `title` FROM `books` AS `table0` 
                WHERE (`title` != 'Policy-Test-01') 
            ORDER BY `table0`.`id` ASC LIMIT 100 ) AS `subq4`
            ";

            string clientRole = "policy_tester_02";
            await QueryMany_Policy(dbQuery, clientRole);

            // Tests Book Read Policy: @item.title eq 'Policy-Test-01'
            // Due to restrictive book policy, expects one book result:
            // id: 9 title: 'Policy-Test-01'
            string dbQuery_restrictToOneResult = @"
            SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq4`.`id`, 'title', `subq4`.`title`)), JSON_ARRAY()) AS `data` FROM (
                SELECT `table0`.`id` AS `id`, `table0`.`title` AS `title` FROM `books` AS `table0` 
                WHERE (`title` = 'Policy-Test-01') 
            ORDER BY `table0`.`id` ASC LIMIT 100 ) AS `subq4`
            ";

            clientRole = "policy_tester_01";
            await QueryMany_Policy(dbQuery_restrictToOneResult, clientRole);
        }

        /// <summary>
        /// Tests a GraphQL query that may fetch multiple result records
        /// on a table with a nullable field, but does not include any nested queries.
        /// When a policy is applied to such top-level query, results are restricted
        /// to the expected records.
        /// </summary>
        [TestMethod]
        public async Task QueryMany_Policy_Nullable()
        {
            string dbQuery = @"
            SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('speciesid', `subq4`.`speciesid`, 'region', `subq4`.`region`)), JSON_ARRAY()) AS `data` FROM (
                SELECT `table0`.`speciesid` AS `speciesid`, `table0`.`region` AS `region` FROM `fungi` AS `table0` 
                WHERE (`region` != 'northeast') 
            ORDER BY `table0`.`speciesid` ASC LIMIT 100 ) AS `subq4`
            ";

            await QueryMany_Policy_Nullable(dbQuery);
        }

        [TestMethod]
        public async Task QueryMany_NestedRequest_Policy()
        {
            string dbQuery = @"
            SELECT COALESCE(JSON_ARRAYAGG(
            JSON_OBJECT('id', `subq10`.`id`, 'title', `subq10`.`title`, 'publishers', `subq10`.`publishers`)), JSON_ARRAY()) AS `data` FROM ( 
                SELECT `table0`.`id` AS `id`, `table0`.`title` AS `title`, `table1_subq`.`data` AS `publishers` 
                FROM `books` AS `table0` LEFT OUTER JOIN LATERAL (
                SELECT JSON_OBJECT('id', `subq9`.`id`, 'name', `subq9`.`name`) AS `data` FROM (
                    SELECT `table1`.`id` AS `id`, `table1`.`name` AS `name` 
                    FROM `publishers` AS `table1` 
                    WHERE (`id` = 1940) AND `table0`.`publisher_id` = `table1`.`id` 
                    ORDER BY `table1`.`id` ASC LIMIT 1 ) AS `subq9`) AS `table1_subq` ON TRUE 
                WHERE (`title` = 'Policy-Test-01') 
                ORDER BY `table0`.`id` ASC LIMIT 100
            ) AS `subq10`
            ";

            // Tests Book Read Policy: @item.title eq 'Policy-Test-01'
            // Publisher Read Policy: @item.id ne 1940
            // Expects HotChocolate error since nested query fails to resolve
            // at least one publisher record due to restrictive policy.
            // Due to expecting error, the dbQuery is not run for result validation.
            await QueryMany_NestedRequest_Policy(
                dbQuery,
                roleName: "policy_tester_03",
                expectError: true);

            // Tests Book Read Policy: @item.title eq 'Policy-Test-01'
            // Publisher Read Policy: @item.id eq 1940
            // Target Record: id: 9, title: 'Policy-Test-01' publisher_id: 1940
            // The top-level book policy restricts this result to one record while
            // the nested query policy resolves at least one result, avoiding
            // resolving null for a non-nullable field.
            // DB Query is used for result validation.
            await QueryMany_NestedRequest_Policy(
                dbQuery,
                roleName: "policy_tester_01",
                expectError: false);
        }
    }
}
