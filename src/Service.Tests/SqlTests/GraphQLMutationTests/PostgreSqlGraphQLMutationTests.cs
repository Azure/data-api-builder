// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLMutationTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGraphQLMutationTests : GraphQLMutationTestBase
    {
        private static string _invalidForeignKeyError =
            "23503: insert or update on table \\\"books\\\" " +
            "violates foreign key constraint \\\"book_publisher_fk\\\"";

        #region Test Fixture Setup
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
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
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.title AS title
                   FROM books AS table0
                   WHERE id = 5001
                     AND title = 'My New Book'
                     AND publisher_id = 1234
                   ORDER BY id asc
                   LIMIT 1) AS subq
            ";

            await InsertMutation(postgresQuery);
        }

        /// <summary>
        /// Demonstrates that using mapped column names for fields within the GraphQL mutation results in successful engine processing.
        /// </summary>
        [TestMethod]
        public async Task InsertMutationWithVariablesAndMappings()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.__column1 AS column1,
                          table0.__column2 AS column2
                   FROM GQLmappings AS table0
                   WHERE __column1 = 2
                   ORDER BY __column1 asc
                   LIMIT 1) AS subq
            ";

            await InsertMutationWithVariablesAndMappings(postgresQuery);
        }

        /// <summary>
        /// <code>Do: </code> Inserts new sale item into sales table that automatically calculates the total price
        /// based on subtotal and tax.
        /// <code>Check: Calculated column is persisted successfully with correct calculated result. </code>
        /// </summary>
        [TestMethod]
        public async Task InsertMutationForComputedColumns()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.item_name AS item_name,
                          table0.subtotal AS subtotal,
                          table0.tax AS tax,
                          table0.total AS total
                   FROM sales AS table0
                   WHERE id = 5001
                     AND item_name = 'headphones'
                     AND subtotal = 195.00
                     AND tax = 10.33
                     AND total = 205.33
                   ORDER BY id asc
                   LIMIT 1) AS subq
            ";

            await InsertMutationForComputedColumns(postgresQuery);
        }

        /// <summary>
        /// <code>Do: </code> Inserts new row in a table containing default values as built_in methods.
        /// <code>Check: </code> Correctly inserts the row with columns having default values as built_in methods and returns the inserted row
        /// as GraphQL response.
        /// </summary>
        [TestMethod]
        public async Task InsertMutationWithDefaultBuiltInFunctions()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT *
                   FROM default_with_function_table AS table0
                   WHERE id = 5001
                     AND user_value = 1234
                   ORDER BY id asc
                   LIMIT 1) AS subq
            ";

            await base.InsertMutationWithDefaultBuiltInFunctions(postgresQuery);
        }

        /// <summary>
        /// <code>Do: </code> Inserts new book using variables to set its title and publisher_id
        /// <code>Check: </code> If book with the expected values of the new book is present in the database and
        /// if the mutation query has returned the correct information
        /// </summary>
        [TestMethod]
        public async Task InsertMutationWithVariables()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.title AS title
                   FROM books AS table0
                   WHERE id = 5001
                     AND title = 'My New Book'
                     AND publisher_id = 1234
                   ORDER BY id asc
                   LIMIT 1) AS subq
            ";

            await InsertMutationWithVariables(postgresQuery);
        }

        /// <summary>
        /// <code>Do: </code> Inserts new review with default content for a Review and return its id and content
        /// <code>Check: </code> If book with the given id is present in the database then
        /// the mutation query will return the review Id with the content of the review added
        /// </summary>
        [TestMethod]
        public async Task InsertMutationForConstantdefaultValue()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.content AS content
                   FROM reviews AS table0
                   WHERE id = 5001
                     AND content = 'Its a classic'
                     AND book_id = 1
                   ORDER BY id asc
                   LIMIT 1) AS subq
            ";

            await InsertMutationForConstantdefaultValue(postgresQuery);
        }

        /// <summary>
        /// <code>Do: </code> Inserts new review with default content for a Review and return its id and content
        /// <code>Check: </code> If book with the given id is present in the database then
        /// the mutation query will return the review Id with the content of the review added
        /// </summary>
        [TestMethod]
        public async Task InsertMutationForVariableNotNullDefault()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.categoryid AS categoryid,
                          table0.pieceid AS pieceid
                   FROM stocks_price AS table0
                   WHERE categoryid = 100
                     AND pieceid = 99
                   ORDER BY categoryid
                   LIMIT 1) AS subq
            ";

            await InsertMutationForVariableNotNullDefault(postgresQuery);
        }

        /// <summary>
        /// <code>Do: </code>Update book in database and return its updated fields
        /// <code>Check: </code>if the book with the id of the edited book and the new values exists in the database
        /// and if the mutation query has returned the values correctly
        /// </summary>
        [TestMethod]
        public async Task UpdateMutation()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.title AS title,
                          table0.publisher_id AS publisher_id
                   FROM books AS table0
                   WHERE id = 1
                   ORDER BY id asc
                   LIMIT 1) AS subq
            ";

            await UpdateMutation(postgresQuery);
        }

        /// <summary>
        /// <code>Do: </code>Update Sales in database and return its updated fields
        /// <code>Check: The calculated column has successfully been updated after updating the other fields </code>
        /// </summary>
        [TestMethod]
        public async Task UpdateMutationForComputedColumns()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.item_name AS item_name,
                          table0.subtotal AS subtotal,
                          table0.tax AS tax,
                          table0.total AS total
                   FROM sales AS table0
                   WHERE id = 2
                     AND item_name = 'phone'
                     AND subtotal = 495.00
                     AND tax = 30.33
                     AND total = 525.33
                   ORDER BY id asc
                   LIMIT 1) AS subq
            ";

            await UpdateMutationForComputedColumns(postgresQuery);
        }

        /// <summary>
        /// Demonstrates that using mapped column names for fields within the GraphQL mutation results in successful engine processing
        /// of the column2 value update for the record where column1 = $id.
        /// </summary>
        [TestMethod]
        public async Task UpdateMutationWithVariablesAndMappings()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.__column1 AS column1,
                          table0.__column2 AS column2
                   FROM GQLmappings AS table0
                   WHERE __column1 = 3
                     AND __column2 = 'Updated Value of Mapped Column'
                   ORDER BY __column1 asc
                   LIMIT 1) AS subq
            ";

            await UpdateMutationWithVariablesAndMappings(postgresQuery);
        }

        /// <summary>
        /// Demonstrates that using mapped column names for fields within the GraphQL mutation results in successful engine processing
        /// of removal of the record where column1 = $id and the returned object representing the deleting record utilizes the mapped column values.
        /// </summary>
        [TestMethod]
        public async Task DeleteMutationWithVariablesAndMappings()
        {
            string postgresQueryForResult = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.__column1 AS column1,
                          table0.__column2 AS column2
                   FROM GQLmappings AS table0
                   WHERE __column1 = 4
                   ORDER BY __column1 asc
                   LIMIT 1) AS subq
            ";

            string postgresQueryToVerifyDeletion = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT COUNT(*) AS COUNT
                   FROM GQLmappings AS table0
                   WHERE __column1 = 4) AS subq
                ";

            await DeleteMutationWithVariablesAndMappings(postgresQueryForResult, postgresQueryToVerifyDeletion);
        }

        /// <summary>
        /// <code>Do: </code>Delete book by id
        /// <code>Check: </code>if the mutation returned result is as expected and if book by that id has been deleted
        /// </summary>
        [TestMethod]
        public async Task DeleteMutation()
        {
            string postgresQueryForResult = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.title AS title,
                          table0.publisher_id AS publisher_id
                   FROM books AS table0
                   WHERE id = 1
                   ORDER BY id asc
                   LIMIT 1) AS subq
            ";

            string postgresQueryToVerifyDeletion = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT COUNT(*) AS COUNT
                   FROM books AS table0
                   WHERE id = 1) AS subq
            ";

            await DeleteMutation(postgresQueryForResult, postgresQueryToVerifyDeletion);
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
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT COUNT(*) AS COUNT
                   FROM book_author_link AS table0
                   WHERE book_id = 2
                     AND author_id = 123) AS subq
            ";

            await InsertMutationForNonGraphQLTypeTable(postgresQuery);
        }

        /// <summary>
        /// <code>Do: </code>a new Book insertion and do a nested querying of the returned book
        /// <code>Check: </code>if the result returned from the mutation is correct
        /// </summary>
        [TestMethod]
        public async Task NestedQueryingInMutation()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq3) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.title AS title,
                          table1_subq.data AS publishers
                   FROM books AS table0
                   LEFT OUTER JOIN LATERAL
                     (SELECT to_jsonb(subq2) AS DATA
                      FROM
                        (SELECT table1.name AS name
                         FROM publishers AS table1
                         WHERE table1.id = table0.publisher_id
                         ORDER BY table1.id asc
                         LIMIT 1) AS subq2) AS table1_subq ON TRUE
                   WHERE table0.id = 5001
                     AND table0.title = 'My New Book'
                     AND table0.publisher_id = 1234
                   ORDER BY table0.id asc
                   LIMIT 1) AS subq3
            ";

            await NestedQueryingInMutation(postgresQuery);
        }

        /// <summary>
        /// Test explicitly inserting a null column
        /// </summary>
        [TestMethod]
        public async Task TestExplicitNullInsert()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.title AS title,
                          table0.issue_number AS issue_number
                   FROM foo.magazines AS table0
                   WHERE id = 800
                     AND title = 'New Magazine'
                     AND issue_number IS NULL
                   ORDER BY id asc
                   LIMIT 1) AS subq
            ";

            await TestExplicitNullInsert(postgresQuery);
        }

        /// <summary>
        /// Test implicitly inserting a null column
        /// </summary>
        [TestMethod]
        public async Task TestImplicitNullInsert()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.title AS title,
                          table0.issue_number AS issue_number
                   FROM foo.magazines AS table0
                   WHERE id = 801
                     AND title = 'New Magazine 2'
                     AND issue_number IS NULL
                   ORDER BY id asc
                   LIMIT 1) AS subq
            ";

            await TestImplicitNullInsert(postgresQuery);
        }

        /// <summary>
        /// Test updating a column to null
        /// </summary>
        [TestMethod]
        public async Task TestUpdateColumnToNull()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.issue_number AS issue_number
                   FROM foo.magazines AS table0
                   WHERE id = 1
                     AND issue_number IS NULL
                   ORDER BY id asc
                   LIMIT 1) AS subq
            ";

            await TestUpdateColumnToNull(postgresQuery);
        }

        /// <summary>
        /// Test updating a missing column in the update mutation will not be updated to null
        /// </summary>
        [TestMethod]
        public async Task TestMissingColumnNotUpdatedToNull()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.title AS title,
                          table0.issue_number AS issue_number
                   FROM foo.magazines AS table0
                   WHERE id = 1
                     AND title = 'Newest Magazine'
                     AND issue_number = 1234
                   ORDER BY id asc
                   LIMIT 1) AS subq
            ";

            await TestMissingColumnNotUpdatedToNull(postgresQuery);
        }

        /// <summary>
        /// <code>Do: </code> Inserts new book and return its id and title with their aliases(arbitrarily set by user while making request)
        /// <code>Check: </code> If book with the expected values of the new book is present in the database and
        /// if the mutation query has returned the correct information with Aliases where provided.
        /// </summary>
        [TestMethod]
        public async Task TestAliasSupportForGraphQLMutationQueryFields()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.id AS book_id,
                          table0.title AS book_title
                   FROM books AS table0
                   WHERE id = 5001
                     AND title = 'My New Book'
                     AND publisher_id = 1234
                   ORDER BY id asc
                   LIMIT 1) AS subq
            ";

            await TestAliasSupportForGraphQLMutationQueryFields(postgresQuery);
        }

        /// <summary>
        /// <code>Do: </code>insert into a simple view
        /// <code>Check: </code> that the new entry is in the view
        /// </summary>
        [TestMethod]
        public async Task InsertIntoSimpleView()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.title AS title
                   FROM books_view_all AS table0
                   WHERE id = 5001
                     AND title = 'Book View'
                     AND publisher_id = 1234
                   ORDER BY id
                   LIMIT 1) AS subq
            ";

            await InsertIntoSimpleView(postgresQuery);
        }

        /// <summary>
        /// <code>Do: </code>Update a simple view
        /// <code>Check: </code> the updated entry is present in the view
        /// </summary>
        [TestMethod]
        public async Task UpdateSimpleView()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.title AS title
                   FROM books_view_all AS table0
                   WHERE id = 1
                   ORDER BY id
                   LIMIT 1) AS subq
            ";

            await UpdateSimpleView(postgresQuery);
        }

        /// <summary>
        /// <code>Do: </code>Delete an entry from a simple view
        /// <code>Check: </code>if the mutation returned result is as expected and if the entry that id has been deleted
        /// </summary>
        [TestMethod]
        public async Task DeleteFromSimpleView()
        {
            string postgresQueryForResult = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.title AS title
                   FROM books_view_all AS table0
                   WHERE id = 1
                   ORDER BY id
                   LIMIT 1) AS subq
            ";

            string postgresQueryToVerifyDeletion = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT COUNT(*) AS COUNT
                   FROM books_view_all AS table0
                   WHERE id = 1) AS subq
            ";

            await DeleteFromSimpleView(postgresQueryForResult, postgresQueryToVerifyDeletion);
        }

        /// <summary>
        /// <code>Do: </code>insert into an "insertable" complex view
        /// <code>Check: </code> that the new entry is in the view
        /// </summary>
        [TestMethod]
        public async Task InsertIntoInsertableComplexView()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT table0.id AS id,
                          table0.title AS title,
                          table0.publisher_id AS publisher_id
                   FROM books_publishers_view_composite_insertable AS table0
                   WHERE id = 5001
                     AND title = 'Book Complex View'
                     AND publisher_id = 1234
                   ORDER BY id
                   LIMIT 1) AS subq
            ";

            await InsertIntoInsertableComplexView(postgresQuery);
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
                SELECT to_jsonb(subq) AS data
                FROM
                    (SELECT table0.id AS id,
                        table0.item_name AS item_name,
                        table0.subtotal AS subtotal,
                        table0.tax AS tax
                    FROM public.sales AS table0
                    WHERE table0.item_name = 'test_name'
                    ORDER BY table0.id ASC
                    LIMIT 1) AS subq
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
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT COUNT(*) AS COUNT
                   FROM books
                   WHERE publisher_id = -1 ) AS subq
            ";

            await InsertWithInvalidForeignKey(postgresQuery, _invalidForeignKeyError);
        }

        /// <summary>
        /// <code>Do: </code>edit a book with an invalid foreign key
        /// <code>Check: </code>that GraphQL returns an error and the book has not been editted
        /// </summary>
        [TestMethod]
        public async Task UpdateWithInvalidForeignKey()
        {
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS DATA
                FROM
                  (SELECT COUNT(*) AS COUNT
                   FROM books
                   WHERE id = 1 AND publisher_id = -1 ) AS subq
            ";

            await UpdateWithInvalidForeignKey(postgresQuery, _invalidForeignKeyError);
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
            string errorMessage = "23505: duplicate key value violates unique constraint " +
                                  "\\\"book_website_placements_book_id_key\\\"";
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
