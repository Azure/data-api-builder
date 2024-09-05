// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLMutationTests
{

    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGraphQLMutationTests : GraphQLMutationTestBase
    {
        private static string _invalidForeignKeyError =
            "Cannot add or update a child row: a foreign key constraint fails";

        #region Test Fixture Setup

        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MYSQL;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Runs after every test to reset the database state
        /// </summary>
        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        #endregion

        #region  Positive Tests

        /// <summary>
        /// <code>Do: </code> Inserts new book and return its id and title
        /// <code>Check: </code> If book with the expected values of the new book is present in the database and
        /// if the mutation query has returned the correct information
        /// </summary>
        [TestMethod]
        public async Task InsertMutation()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq`.`id`, 'title', `subq`.`title`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`
                    FROM `books` AS `table0`
                    WHERE `id` = 5001
                        AND `title` = 'My New Book'
                        AND `publisher_id` = 1234
                    ORDER BY `id` asc LIMIT 1
                    ) AS `subq`
            ";

            await InsertMutation(mySqlQuery);
        }

        /// <summary>
        /// <code>Do: </code> Inserts a new sale item into the sales table that automatically calculates the total price
        /// based on subtotal and tax.
        /// <code>Check: </code> Calculated column is persisted successfully with correct calculated result.
        /// </summary>
        [TestMethod]
        public async Task InsertMutationForComputedColumns()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT(
                    'id', `subq`.`id`, 'item_name', `subq`.`item_name`,
                    'subtotal', `subq`.`subtotal`, 'tax', `subq`.`tax`,
                    'total', `subq`.`total`
                    ) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`item_name` AS `item_name`,
                        `table0`.`subtotal` AS `subtotal`,
                        `table0`.`tax` AS `tax`,
                        `table0`.`total` AS `total`
                    FROM `sales` AS `table0`
                    WHERE `id` = 5001
                        AND `item_name` = 'headphones'
                        AND `subtotal` = 195.00
                        AND `tax` = 10.33
                        AND `total` = 205.33
                    ORDER BY `id` asc LIMIT 1
                    ) AS `subq`
            ";

            await InsertMutationForComputedColumns(mySqlQuery);
        }

        /// <summary>
        /// <code>Do: </code> Inserts new row in a table containing default values as built_in methods.
        /// <code>Check: </code> Correctly inserts the row with columns having default values as built_in methods and returns the inserted row
        /// as GraphQL response.
        /// </summary>
        [TestMethod]
        [Ignore]
        public Task InsertMutationWithDefaultBuiltInFunctions()
        {
            // FIXME: This test is failing because of incorrect SQL query. Issue: https://github.com/Azure/data-api-builder/issues/1696
            throw new NotImplementedException();
        }

        /// <summary>
        /// <code>Do: </code> Inserts new book using variables to set its title and publisher_id
        /// <code>Check: </code> If book with the expected values of the new book is present in the database and
        /// if the mutation query has returned the correct information
        /// </summary>
        [TestMethod]
        public async Task InsertMutationWithVariables()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq`.`id`, 'title', `subq`.`title`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`
                    FROM `books` AS `table0`
                    WHERE `id` = 5001
                        AND `title` = 'My New Book'
                        AND `publisher_id` = 1234
                    ORDER BY `id` asc LIMIT 1
                    ) AS `subq`
            ";

            await InsertMutationWithVariables(mySqlQuery);
        }

        /// <summary>
        /// Demonstrates that using mapped column names for fields within the GraphQL mutation results in successful engine processing.
        /// </summary>
        [TestMethod]
        public async Task InsertMutationWithVariablesAndMappings()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('column1', `subq`.`column1`, 'column2', `subq`.`column2`) AS `data`
                FROM (
                    SELECT `table0`.`__column1` AS `column1`,
                        `table0`.`__column2` AS `column2`
                    FROM `GQLmappings` AS `table0`
                    WHERE `table0`.`__column1` = 2
                    ORDER BY `table0`.`__column1` asc LIMIT 1
                    ) AS `subq`
            ";

            await InsertMutationWithVariablesAndMappings(mySqlQuery);
        }

        /// <summary>
        /// <code>Do: </code> Inserts new review with default content for a Review and return its id and content
        /// <code>Check: </code> If book with the given id is present in the database then
        /// the mutation query will return the review Id with the content of the review added
        /// </summary>
        [TestMethod]
        public async Task InsertMutationForConstantdefaultValue()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq`.`id`, 'content', `subq`.`content`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`content` AS `content`
                    FROM `reviews` AS `table0`
                    WHERE `id` = 5001
                        AND `content` = 'Its a classic'
                        AND `book_id` = 1
                    ORDER BY `id` asc LIMIT 1
                    ) AS `subq`
            ";

            await InsertMutationForConstantdefaultValue(mySqlQuery);
        }

        /// <inheritdoc/>
        [TestMethod]
        public async Task InsertMutationForVariableNotNullDefault()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('categoryid', `subq`.`categoryid`, 'pieceid', `subq`.`pieceid`) AS `data`
                FROM (
                    SELECT `table0`.`categoryid` AS `categoryid`,
                           `table0`.`pieceid` AS `pieceid`
                    FROM `stocks_price` AS `table0`
                    WHERE `categoryid` = 100
                        AND `pieceid` = 99
                    ORDER BY `categoryid` LIMIT 1
                    ) AS `subq`
            ";

            await InsertMutationForVariableNotNullDefault(mySqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>Update book in database and return its updated fields
        /// <code>Check: </code>if the book with the id of the edited book and the new values exists in the database
        /// and if the mutation query has returned the values correctly
        /// </summary>
        [TestMethod]
        public async Task UpdateMutation()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('title', `subq2`.`title`, 'publisher_id', `subq2`.`publisher_id`) AS `data`
                FROM (
                    SELECT `table0`.`title` AS `title`,
                        `table0`.`publisher_id` AS `publisher_id`
                    FROM `books` AS `table0`
                    WHERE `table0`.`id` = 1
                    ORDER BY `table0`.`id` asc LIMIT 1
                    ) AS `subq2`
            ";

            await UpdateMutation(mySqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>Update Sales in database and return its updated fields
        /// <code>Check: The calculated column has successfully been updated after updating the other fields </code>
        /// </summary>
        [TestMethod]
        // IGNORE FOR NOW, SEE: Issue #1001
        [Ignore]
        public async Task UpdateMutationForComputedColumns()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT(
                    'id', `subq2`.`id`, 'item_name', `subq2`.`item_name`,
                    'subtotal', `subq2`.`subtotal`, 'tax', `subq2`.`tax`,
                    'total', `subq2`.`total`
                    ) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`item_name` AS `item_name`,
                        `table0`.`subtotal` AS `subtotal`,
                        `table0`.`tax` AS `tax`,
                        `table0`.`total` AS `total`
                    FROM `sales` AS `table0`
                    WHERE `id` = 2
                    ORDER BY `id` asc LIMIT 1
                    ) AS `subq2`
            ";

            await UpdateMutationForComputedColumns(mySqlQuery);
        }

        /// <summary>
        /// Demonstrates that using mapped column names for fields within the GraphQL mutation results in successful engine processing
        /// of the column2 value update for the record where column1 = $id.
        /// </summary>
        [TestMethod]
        public async Task UpdateMutationWithVariablesAndMappings()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('column1', `subq2`.`column1`, 'column2', `subq2`.`column2`) AS `data`
                FROM (
                    SELECT `table0`.`__column1` AS `column1`,
                        `table0`.`__column2` AS `column2`
                    FROM `GQLmappings` AS `table0`
                    WHERE `table0`.`__column1` = 3
                    AND `table0`.`__column2` = 'Updated Value of Mapped Column'
                    ORDER BY `table0`.`__column1` asc LIMIT 1
                    ) AS `subq2`
            ";

            await UpdateMutationWithVariablesAndMappings(mySqlQuery);
        }

        /// <summary>
        /// Demonstrates that using mapped column names for fields within the GraphQL mutation results in successful engine processing
        /// of removal of the record where column1 = $id and the returned object representing the deleting record utilizes the mapped column values.
        /// </summary>
        [TestMethod]
        public async Task DeleteMutationWithVariablesAndMappings()
        {
            string mySqlQueryForResult = @"
                SELECT JSON_OBJECT('column1', `subq2`.`column1`, 'column2', `subq2`.`column2`) AS `data`
                FROM (
                    SELECT `table0`.`__column1` AS `column1`,
                        `table0`.`__column2` AS `column2`
                    FROM `GQLmappings` AS `table0`
                    WHERE `table0`.`__column1` = 4
                    ORDER BY `table0`.`__column1` asc LIMIT 1
                    ) AS `subq2`
            ";

            string mySqlQueryToVerifyDeletion = @"
                SELECT JSON_OBJECT('count', `subq`.`count`) AS `data`
                FROM (
                    SELECT COUNT(*) AS `count`
                    FROM `GQLmappings` AS `table0`
                    WHERE `__column1` = 4
                    ) AS `subq`
            ";

            await DeleteMutationWithVariablesAndMappings(mySqlQueryForResult, mySqlQueryToVerifyDeletion);
        }

        /// <summary>
        /// <code>Do: </code>Delete book by id
        /// <code>Check: </code>if the mutation returned result is as expected and if book by that id has been deleted
        /// </summary>
        [TestMethod]
        public async Task DeleteMutation()
        {
            string mySqlQueryForResult = @"
                SELECT JSON_OBJECT('title', `subq2`.`title`, 'publisher_id', `subq2`.`publisher_id`) AS `data`
                FROM (
                    SELECT `table0`.`title` AS `title`,
                        `table0`.`publisher_id` AS `publisher_id`
                    FROM `books` AS `table0`
                    WHERE `table0`.`id` = 1
                    ORDER BY `table0`.`id` asc LIMIT 1
                    ) AS `subq2`
            ";

            string mySqlQueryToVerifyDeletion = @"
                SELECT JSON_OBJECT('count', `subq`.`count`) AS `data`
                FROM (
                    SELECT COUNT(*) AS `count`
                    FROM `books` AS `table0`
                    WHERE `id` = 1
                    ) AS `subq`
            ";

            await DeleteMutation(mySqlQueryForResult, mySqlQueryToVerifyDeletion);
        }

        /// <summary>
        /// <code>Do: </code>run a mutation which mutates a relationship instead of a graphql type
        /// <code>Check: </code>that the insertion of the entry in the appropriate link table was successful
        /// </summary>
        [TestMethod]
        // IGNORE FOR NOW, SEE: Issue #285
        [Ignore]
        public async Task InsertMutationForNonGraphQLTypeTable()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('count', `subq`.`count`) AS DATA
                FROM
                  (SELECT COUNT(*) AS `count`
                   FROM book_author_link
                   WHERE book_id = 2
                     AND author_id = 123) AS subq
            ";

            await InsertMutationForNonGraphQLTypeTable(mySqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>a new Book insertion and do a nested querying of the returned book
        /// <code>Check: </code>if the result returned from the mutation is correct
        /// </summary>
        [TestMethod]
        public async Task NestedQueryingInMutation()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq4`.`id`, 'title', `subq4`.`title`, 'publishers', JSON_EXTRACT(`subq4`.
                            `publishers`, '$')) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`,
                        `table1_subq`.`data` AS `publishers`
                    FROM `books` AS `table0`
                    LEFT OUTER JOIN LATERAL(SELECT JSON_OBJECT('name', `subq3`.`name`) AS `data` FROM (
                            SELECT `table1`.`name` AS `name`
                            FROM `publishers` AS `table1`
                            WHERE `table0`.`publisher_id` = `table1`.`id`
                            ORDER BY `table1`.`id` asc LIMIT 1
                            ) AS `subq3`) AS `table1_subq` ON TRUE
                    WHERE `table0`.`id` = 5001
                    ORDER BY `table0`.`id` asc LIMIT 1
                    ) AS `subq4`
            ";

            await NestedQueryingInMutation(mySqlQuery);
        }

        /// <summary>
        /// Test explicitly inserting a null column
        /// </summary>
        [TestMethod]
        public async Task TestExplicitNullInsert()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq2`.`id`, 'title', `subq2`.`title`, 'issue_number', `subq2`.`issue_number`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`,
                        `table0`.`issue_number` AS `issue_number`
                    FROM `magazines` AS `table0`
                    WHERE `table0`.`id` = 800
                        AND `table0`.`title` = 'New Magazine'
                        AND `table0`.`issue_number` IS NULL
                    ORDER BY `table0`.`id` asc LIMIT 1
                    ) AS `subq2`
            ";

            await TestExplicitNullInsert(mySqlQuery);
        }

        /// <summary>
        /// Test implicitly inserting a null column
        /// </summary>
        [TestMethod]
        public async Task TestImplicitNullInsert()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq2`.`id`, 'title', `subq2`.`title`, 'issue_number', `subq2`.`issue_number`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`,
                        `table0`.`issue_number` AS `issue_number`
                    FROM `magazines` AS `table0`
                    WHERE `table0`.`id` = 801
                        AND `table0`.`title` = 'New Magazine 2'
                        AND `table0`.`issue_number` IS NULL
                    ORDER BY `table0`.`id` asc LIMIT 1
                    ) AS `subq2`
            ";

            await TestImplicitNullInsert(mySqlQuery);
        }

        /// <summary>
        /// Test updating a column to null
        /// </summary>
        [TestMethod]
        public async Task TestUpdateColumnToNull()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq2`.`id`, 'issue_number', `subq2`.`issue_number`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`issue_number` AS `issue_number`
                    FROM `magazines` AS `table0`
                    WHERE `table0`.`id` = 1
                        AND `table0`.`issue_number` IS NULL
                    ORDER BY `table0`.`id` asc LIMIT 1
                    ) AS `subq2`
            ";

            await TestUpdateColumnToNull(mySqlQuery);
        }

        /// <summary>
        /// Test updating a missing column in the update mutation will not be updated to null
        /// </summary>
        [TestMethod]
        public async Task TestMissingColumnNotUpdatedToNull()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq2`.`id`, 'title', `subq2`.`title`, 'issue_number', `subq2`.`issue_number`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`,
                        `table0`.`issue_number` AS `issue_number`
                    FROM `magazines` AS `table0`
                    WHERE `table0`.`id` = 1
                        AND `table0`.`title` = 'Newest Magazine'
                        AND `table0`.`issue_number` = 1234
                    ORDER BY `table0`.`id` asc LIMIT 1
                    ) AS `subq2`
            ";

            await TestMissingColumnNotUpdatedToNull(mySqlQuery);
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id and book_title are aliases used for corresponding query fields.
        /// The response for the query will use the alias instead of raw db column.
        /// </summary>
        [TestMethod]
        public async Task TestAliasSupportForGraphQLMutationQueryFields()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('book_id', `subq`.`book_id`, 'book_title', `subq`.`book_title`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `book_id`,
                        `table0`.`title` AS `book_title`
                    FROM `books` AS `table0`
                    WHERE `id` = 5001
                        AND `title` = 'My New Book'
                        AND `publisher_id` = 1234
                    ORDER BY `id` asc LIMIT 1
                    ) AS `subq`
            ";

            await TestAliasSupportForGraphQLMutationQueryFields(mySqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>insert into a simple view
        /// <code>Check: </code> that the new entry is in the view
        /// </summary>
        [TestMethod]
        public async Task InsertIntoSimpleView()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq`.`id`, 'title', `subq`.`title`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`
                    FROM `books_view_all` AS `table0`
                    WHERE `id` = 5001
                        AND `title` = 'Book View'
                        AND `publisher_id` = 1234
                    ORDER BY `id` LIMIT 1
                    ) AS `subq`
            ";

            await InsertIntoSimpleView(mySqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>Update a simple view
        /// <code>Check: </code> the updated entry is present in the view
        /// </summary>
        [TestMethod]
        [Ignore]
        public async Task UpdateSimpleView()
        {
            // update on a simple view should succeed in MySql: https://dev.mysql.com/doc/refman/8.0/en/view-updatability.html
            // but it fails with: The target table books_view_all of the UPDATE is not updatable
            // I suspect it may have to do with the update query generated for mysql
            // ignore for now
            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq2`.`id`, 'title', `subq2`.`title`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`
                    FROM `books_view_all` AS `table0`
                    WHERE `table0`.`id` = 1
                    ORDER BY `table0`.`id` LIMIT 1
                    ) AS `subq2`
            ";

            await UpdateSimpleView(mySqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>Delete an entry from a simple view
        /// <code>Check: </code>if the mutation returned result is as expected and if the entry that id has been deleted
        /// </summary>
        [TestMethod]
        public async Task DeleteFromSimpleView()
        {
            string mySqlQueryForResult = @"
                SELECT JSON_OBJECT('id', `subq2`.`id`, 'title', `subq2`.`title`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`
                    FROM `books_view_all` AS `table0`
                    WHERE `table0`.`id` = 1
                    ORDER BY `table0`.`id` LIMIT 1
                    ) AS `subq2`
            ";

            string mySqlQueryToVerifyDeletion = @"
                SELECT JSON_OBJECT('count', `subq`.`count`) AS `data`
                FROM (
                    SELECT COUNT(*) AS `count`
                    FROM `books_view_all` AS `table0`
                    WHERE `id` = 1
                    ) AS `subq`
            ";

            await DeleteFromSimpleView(mySqlQueryForResult, mySqlQueryToVerifyDeletion);
        }

        /// <summary>
        /// <code>Do: </code>insert into an "insertable" complex view
        /// <code>Check: </code> that the new entry is in the view
        /// </summary>
        [TestMethod]
        [Ignore]
        public async Task InsertIntoInsertableComplexView()
        {
            // this view does not have the necessary trigger
            // implemented yet
            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq`.`id`, 'title', `subq`.`title`, 'publisher_id', `subq`.`publisher_id`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`,
                        `table0`.`publisher_id` AS `publisher_id`
                    FROM `books_publishers_view_composite_insertable` AS `table0`
                    WHERE `id` = 5001
                        AND `title` = 'Book Complex View'
                        AND `publisher_id` = 1234
                    ORDER BY `id` LIMIT 1
                    ) AS `subq`
            ";

            await InsertIntoInsertableComplexView(mySqlQuery);
        }

        /// <summary>
        /// <code>Do: </code> create an entry with a type Float
        /// <code>Check: </code> that the type Float of new entry can be correctly viewed in different regional format settings
        /// </summary>
        [TestMethod]
        [DataRow("en-US")]
        [DataRow("en-DE")]
        public async Task CanCreateItemWithCultureInvariant(string cultureInfo)
        {
            string msSqlQuery = @"
                SELECT JSON_OBJECT('id', `subq`.`id`, 'item_name', `subq`.`item_name`, 'subtotal', `subq`.`subtotal`, 'tax', `subq`.`tax`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`item_name` AS `item_name`,
                        `table0`.`subtotal` AS `subtotal`,
                        `table0`.`tax` AS `tax`
                    FROM `sales` AS `table0`
                    WHERE `table0`.`item_name` = 'test_name'
                    ORDER BY `table0`.`id` ASC LIMIT 1
                    ) AS `subq`
            ";

            await CanCreateItemWithCultureInvariant(cultureInfo, msSqlQuery);
        }

        /// <inheritdoc/>
        [TestMethod]
        [Ignore]
        public override Task ExecuteMutationWithOnlyTypenameInSelectionSet()
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// <code>Do: </code>insert a new Book with an invalid foreign key
        /// <code>Check: </code>that GraphQL returns an error and that the book has not actually been added
        /// </summary>
        [TestMethod]
        public async Task InsertWithInvalidForeignKey()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('count', `subq`.`count`) AS `data`
                FROM (
                    SELECT COUNT(*) AS `count`
                    FROM `books`
                    WHERE `publisher_id` = - 1
                    ) AS `subq`
            ";

            await InsertWithInvalidForeignKey(mySqlQuery, _invalidForeignKeyError);
        }

        /// <summary>
        /// <code>Do: </code>edit a book with an invalid foreign key
        /// <code>Check: </code>that GraphQL returns an error and the book has not been editted
        /// </summary>
        [TestMethod]
        public async Task UpdateWithInvalidForeignKey()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('count', `subq`.`count`) AS `data`
                FROM (
                    SELECT COUNT(*) AS `count`
                    FROM `books`
                    WHERE `id` = 1
                    AND `publisher_id` = - 1
                    ) AS `subq`
            ";

            await UpdateWithInvalidForeignKey(mySqlQuery, _invalidForeignKeyError);
        }

        /// <summary>
        /// <code>Do: </code>use an update mutation without passing any of the optional new values to update
        /// <code>Check: </code>check that GraphQL returns the appropriate message to the user
        /// </summary>
        [TestMethod]
        public override async Task UpdateWithNoNewValues()
        {
            await base.UpdateWithNoNewValues();
        }

        /// <summary>
        /// <code>Do: </code>use an update mutation with an invalid id to update
        /// <code>Check: </code>check that GraphQL returns an appropriate exception to the user
        /// </summary>
        [TestMethod]
        public override async Task UpdateWithInvalidIdentifier()
        {
            await base.UpdateWithInvalidIdentifier();
        }

        /// <summary>
        /// Test adding a website placement to a book which already has a website
        /// placement
        /// </summary>
        [TestMethod]
        public async Task TestViolatingOneToOneRelationship()
        {
            string errorMessage = "Duplicate entry '1' for key " +
                                  "'book_website_placements.book_id'";
            await TestViolatingOneToOneRelationship(errorMessage);
        }

        [TestMethod]
        [Ignore]
        /// <inheritdoc/>
        public override Task TestDbPolicyForCreateOperationReferencingFieldAbsentInRequest()
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
