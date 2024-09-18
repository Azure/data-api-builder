// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLMutationTests
{

    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGraphQLMutationTests : GraphQLMutationTestBase
    {
        #region Test Fixture Setup
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
            string msSqlQuery = @"
                SELECT TOP 1 [table0].[id] AS [id],
                    [table0].[title] AS [title]
                FROM [books] AS [table0]
                WHERE [table0].[id] = 5001
                    AND [table0].[title] = 'My New Book'
                    AND [table0].[publisher_id] = 1234
                ORDER BY [id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await InsertMutation(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code> Inserts new row in a table containing default values as built_in methods.
        /// <code>Check: </code> Correctly inserts the row with columns having default values as built_in methods and returns the inserted row
        /// as GraphQL response.
        /// </summary>
        [TestMethod]
        public async Task InsertMutationWithDefaultBuiltInFunctions()
        {
            string msSqlQuery = @"
                SELECT *
                FROM [default_with_function_table] AS [table0]
                WHERE [table0].[id] = 5001
                    AND [table0].[user_value] = 1234
                ORDER BY [id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";
            await base.InsertMutationWithDefaultBuiltInFunctions(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code> Inserts new Publisher with name = 'New publisher'
        /// <code>Check: </code> Mutation fails because the database policy (@item.name ne 'New publisher') prohibits insertion of records with name = 'New publisher'.
        /// </summary>
        [TestMethod]
        public async Task InsertMutationFailingDatabasePolicy()
        {
            string errorMessage = "Could not insert row with given values for entity: Publisher";
            string msSqlQuery = @"
                SELECT count(*) as count
                   FROM [publishers]
                WHERE [name] = 'New publisher'
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await InsertMutationFailingDatabasePolicy(
                dbQuery: msSqlQuery,
                errorMessage: errorMessage,
                roleName: "database_policy_tester");
        }

        /// <summary>
        /// <code>Do: </code> Inserts new book using variables to set its title and publisher_id
        /// <code>Check: </code> If book with the expected values of the new book is present in the database and
        /// if the mutation query has returned the correct information
        /// </summary>
        [TestMethod]
        public async Task InsertMutationWithVariables()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [table0].[id] AS [id],
                    [table0].[title] AS [title]
                FROM [books] AS [table0]
                WHERE [table0].[id] = 5001
                    AND [table0].[title] = 'My New Book'
                    AND [table0].[publisher_id] = 1234
                ORDER BY [id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await InsertMutationWithVariables(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code> Inserts new sale item into sales table that automatically calculates the total price
        /// based on subtotal and tax.
        /// <code>Check: Calculated column is persisted successfully with correct calculated result. </code>
        /// </summary>
        [TestMethod]
        public async Task InsertMutationForComputedColumns()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [table0].[id] AS [id],
                    [table0].[item_name] AS [item_name],
                    [table0].[subtotal] AS [subtotal],
                    [table0].[tax] AS [tax],
                    [table0].[total] AS [total]
                FROM [sales] AS [table0]
                WHERE [table0].[id] = 5001
                    AND [table0].[item_name] = 'headphones'
                    AND [table0].[subtotal] = 195.00
                    AND [table0].[tax] = 10.33
                    AND [table0].[total] = 205.33
                ORDER BY [id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await InsertMutationForComputedColumns(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code> Inserts new review with default content for a Review and return its id and content
        /// <code>Check: </code> If book with the given id is present in the database then
        /// the mutation query will return the review Id with the content of the review added
        /// </summary>
        [TestMethod]
        public async Task InsertMutationForConstantdefaultValue()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [table0].[id] AS [id],
                    [table0].[content] AS [content]
                FROM [reviews] AS [table0]
                WHERE [table0].[id] = 5001
                    AND [table0].[content] = 'Its a classic'
                    AND [table0].[book_id] = 1
                ORDER BY [id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await InsertMutationForConstantdefaultValue(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>insert new Book and return nothing
        /// <code>Check: </code>if the intended book is inserted in books table
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureMutationForInsertion()
        {
            string msSqlQuery = @"
                SELECT COUNT(*) AS [count]
                FROM [books] AS [table0]
                WHERE [table0].[title] = 'Random Book'
                    AND [table0].[publisher_id] = 1234
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await TestStoredProcedureMutationForInsertion(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>deletes a Book and return nothing
        /// <code>Check: </code>the intended book is deleted
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureMutationForDeletion()
        {
            string dbQueryToVerifyDeletion = @"
                SELECT MAX(table0.id) AS [maxId]
                FROM [books] AS [table0]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await TestStoredProcedureMutationForDeletion(dbQueryToVerifyDeletion);
        }

        /// <summary>
        /// Test to validate that when an insert DML trigger is enabled on a table, we still return the
        /// latest data as it is present after the trigger gets executed. To validate that the data is returned
        /// as it is after the trigger is executed, we use the values which are updated by the trigger in the WHERE predicates of the verifying sql query.
        /// </summary>
        [TestMethod]
        public async Task InsertMutationWithTrigger()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [table0].*
                FROM [intern_data] AS [table0]
                WHERE [table0].[id] = 4
                    AND [table0].[months] = 2
                    AND [table0].[name] = 'Tommy'
                    AND [table0].[salary] = 30
                ORDER BY [id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            // Validate that insert operation succeeds when an insert DML trigger is enabled for a table
            // with non-autogenerated primary key. The trigger updates the salary to max(0,min(30,salary)).
            await InsertMutationOnTableWithTriggerWithNonAutoGenPK(msSqlQuery);

            msSqlQuery = @"
                SELECT TOP 1 [table0].*
                FROM [fte_data] AS [table0]
                WHERE [table0].[id] = 5001
                    AND [table0].[u_id] = 2
                    AND [table0].[name] = 'Joel'
                    AND [table0].[salary] = 100
                ORDER BY [id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            // Validate that insert operation succeeds when an insert DML trigger is enabled for a table
            // with autogenerated primary key. The trigger updates the salary to max(0,min(100,salary)).
            await InsertMutationOnTableWithTriggerWithAutoGenPK(msSqlQuery);
        }

        /// <summary>
        /// Test to validate that even when an update DML trigger is enabled on a table, we still return the
        /// latest data as it is present after the trigger gets executed. Whenever an update DML trigger is enabled,
        /// we use a subsequent SELECT query to get the data instead of using OUTPUT clause. To validate that the data is returned
        /// as it is after the trigger is executed, we use the values which are updated by the trigger in the WHERE predicates of the verifying sql query.
        /// </summary>
        [TestMethod]
        public async Task UpdateMutationWithTrigger()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [table0].*
                FROM [intern_data] AS [table0]
                WHERE [table0].[id] = 1
                    AND [table0].[months] = 3
                    AND [table0].[salary] = 50
                ORDER BY [id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            // Validate that update operation succeeds when an update DML trigger is enabled for a table
            // with non-autogenerated primary key. The trigger updates the salary to max(0,min(50,salary)).
            await UpdateMutationOnTableWithTriggerWithNonAutoGenPK(msSqlQuery);

            msSqlQuery = @"
                SELECT TOP 1 [table0].*
                FROM [fte_data] AS [table0]
                WHERE [table0].[id] = 1
                    AND [table0].[u_id] = 2
                    AND [table0].[salary] = 0
                ORDER BY [id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            // Validate that update operation succeeds when an update DML trigger is configured for a table
            // with autogenerated primary key. The trigger updates the salary to max(0,min(150,salary)).
            await UpdateMutationOnTableWithTriggerWithAutoGenPK(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>insert a new book and return all the books with same publisher
        /// <code>Check: </code>the intended book is inserted and all the books with same publisher
        /// are returned as response.
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureMutationNonEmptyResponse()
        {
            string dbQuery = @"
                SELECT [table0].id, [table0].title
                FROM [books] AS [table0]
                JOIN (
                    SELECT id
                    FROM [publishers]
                    WHERE name = 'Big Company') AS [table1]
                ON [table0].publisher_id = [table1].id
                ORDER BY [table0].id
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES
            ";

            await TestStoredProcedureMutationNonEmptyResponse(dbQuery);
        }

        /// <summary>
        /// <code>Do: </code>Book title updation and return the updated row
        /// <code>Check: </code>if the result returned from the mutation is correct
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureMutationForUpdate()
        {
            string dbQuery = @"
                SELECT id, title, publisher_id
                FROM [books] AS [table0]
                WHERE 
                    [table0].[id] = 14
                    AND [table0].[publisher_id] = 1234
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await TestStoredProcedureMutationForUpdate(dbQuery);
        }

        /// <inheritdoc/>
        [TestMethod]
        public async Task InsertMutationForVariableNotNullDefault()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [table0].[categoryid] AS [categoryid],
                    [table0].[pieceid] AS [pieceid]
                FROM [stocks_price] AS [table0]
                WHERE [table0].[categoryid] = 100
                    AND [table0].[pieceid] = 99
                ORDER BY [categoryid]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await InsertMutationForVariableNotNullDefault(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>Update book in database and return its updated fields
        /// <code>Check: </code>if the book with the id of the edited book and the new values exists in the database
        /// and if the mutation query has returned the values correctly
        /// </summary>
        [TestMethod]
        public async Task UpdateMutation()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [title],
                    [publisher_id]
                FROM [books]
                WHERE [books].[id] = 1
                    AND [books].[title] = 'Even Better Title'
                    AND [books].[publisher_id] = 2345
                ORDER BY [books].[id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await UpdateMutation(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>Update Sales in database and return its updated fields
        /// <code>Check: The calculated column has successfully been updated after updating the other fields </code>
        /// </summary>
        [TestMethod]
        public async Task UpdateMutationForComputedColumns()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [table0].[id] AS [id],
                    [table0].[item_name] AS [item_name],
                    [table0].[subtotal] AS [subtotal],
                    [table0].[tax] AS [tax],
                    [table0].[total] AS [total]
                FROM [sales] AS [table0]
                WHERE [table0].[id] = 2
                    AND [table0].[item_name] = 'phone'
                    AND [table0].[subtotal] = 495.00
                    AND [table0].[tax] = 30.33
                    AND [table0].[total] = 525.33
                ORDER BY [id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await UpdateMutationForComputedColumns(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>Delete book by id
        /// <code>Check: </code>if the mutation returned result is as expected and if book by that id has been deleted
        /// </summary>
        [TestMethod]
        public async Task DeleteMutation()
        {
            string msSqlQueryForResult = @"
                SELECT TOP 1 [title],
                    [publisher_id]
                FROM [books]
                WHERE [books].[id] = 1
                ORDER BY [books].[id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string msSqlQueryToVerifyDeletion = @"
                SELECT COUNT(*) AS count
                FROM [books]
                WHERE [id] = 1
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await DeleteMutation(msSqlQueryForResult, msSqlQueryToVerifyDeletion);
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
            string msSqlQuery = @"
                SELECT COUNT(*) AS count
                FROM [book_author_link]
                WHERE [book_author_link].[book_id] = 2
                    AND [book_author_link].[author_id] = 123
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await InsertMutationForNonGraphQLTypeTable(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>a new Book insertion and do a nested querying of the returned book
        /// <code>Check: </code>if the result returned from the mutation is correct
        /// </summary>
        [TestMethod]
        public async Task NestedQueryingInMutation()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [table0].[id] AS [id],
                    [table0].[title] AS [title],
                    JSON_QUERY([table1_subq].[data]) AS [publishers]
                FROM [books] AS [table0]
                OUTER APPLY (
                    SELECT TOP 1 [table1].[name] AS [name]
                    FROM [publishers] AS [table1]
                    WHERE [table0].[publisher_id] = [table1].[id]
                    ORDER BY [id] asc
                    FOR JSON PATH,
                        INCLUDE_NULL_VALUES,
                        WITHOUT_ARRAY_WRAPPER
                    ) AS [table1_subq]([data])
                WHERE [table0].[id] = 5001
                    AND [table0].[title] = 'My New Book'
                    AND [table0].[publisher_id] = 1234
                ORDER BY [id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await NestedQueryingInMutation(msSqlQuery);
        }

        /// <summary>
        /// Test explicitly inserting a null column
        /// </summary>
        [TestMethod]
        public async Task TestExplicitNullInsert()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [id],
                    [title],
                    [issue_number]
                FROM [foo].[magazines]
                WHERE [foo].[magazines].[id] = 800
                    AND [foo].[magazines].[title] = 'New Magazine'
                    AND [foo].[magazines].[issue_number] IS NULL
                ORDER BY [foo].[magazines].[id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await TestExplicitNullInsert(msSqlQuery);
        }

        /// <summary>
        /// Test implicitly inserting a null column
        /// </summary>
        [TestMethod]
        public async Task TestImplicitNullInsert()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [id],
                    [title],
                    [issue_number]
                FROM [foo].[magazines]
                WHERE [foo].[magazines].[id] = 801
                    AND [foo].[magazines].[title] = 'New Magazine 2'
                    AND [foo].[magazines].[issue_number] IS NULL
                ORDER BY [foo].[magazines].[id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await TestImplicitNullInsert(msSqlQuery);
        }

        /// <summary>
        /// Test updating a column to null
        /// </summary>
        [TestMethod]
        public async Task TestUpdateColumnToNull()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [id],
                    [issue_number]
                FROM [foo].[magazines]
                WHERE [foo].[magazines].[id] = 1
                    AND [foo].[magazines].[issue_number] IS NULL
                ORDER BY [foo].[magazines].[id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await TestUpdateColumnToNull(msSqlQuery);
        }

        /// <summary>
        /// Test updating a missing column in the update mutation will not be updated to null
        /// </summary>
        [TestMethod]
        public async Task TestMissingColumnNotUpdatedToNull()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [id],
                    [title],
                    [issue_number]
                FROM [foo].[magazines]
                WHERE [foo].[magazines].[id] = 1
                    AND [foo].[magazines].[title] = 'Newest Magazine'
                    AND [foo].[magazines].[issue_number] = 1234
                ORDER BY [foo].[magazines].[id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await TestMissingColumnNotUpdatedToNull(msSqlQuery);
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id and book_title are aliases used for corresponding query fields.
        /// The response for the query will use the alias instead of raw db column.
        /// </summary>
        [TestMethod]
        public async Task TestAliasSupportForGraphQLMutationQueryFields()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [table0].[id] AS [book_id],
                    [table0].[title] AS [book_title]
                FROM [books] AS [table0]
                WHERE [table0].[id] = 5001
                    AND [table0].[title] = 'My New Book'
                    AND [table0].[publisher_id] = 1234
                ORDER BY [id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await TestAliasSupportForGraphQLMutationQueryFields(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>insert into a simple view
        /// <code>Check: </code> that the new entry is in the view
        /// </summary>
        [TestMethod]
        public async Task InsertIntoSimpleView()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [table0].[id] AS [id],
                    [table0].[title] AS [title]
                FROM [books_view_all] AS [table0]
                WHERE [table0].[id] = 5001
                    AND [table0].[title] = 'Book View'
                    AND [table0].[publisher_id] = 1234
                ORDER BY [id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await InsertIntoSimpleView(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>Update a simple view
        /// <code>Check: </code> the updated entry is present in the view
        /// </summary>
        [TestMethod]
        public async Task UpdateSimpleView()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [id], [title]
                FROM [books_view_all]
                WHERE [id] = 1
                ORDER BY [id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await UpdateSimpleView(msSqlQuery);
        }

        [TestMethod]
        public async Task InsertMutationWithVariablesAndMappings()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [__column1] AS [column1], [__column2] AS [column2]
                FROM [GQLmappings]
                WHERE [__column1] = 2
                ORDER BY [__column1]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await InsertMutationWithVariablesAndMappings(msSqlQuery);
        }

        /// <summary>
        /// Demonstrates that using mapped column names for fields within the GraphQL mutation results in successful engine processing
        /// of the column2 value update for the record where column1 = $id.
        /// </summary>
        [TestMethod]
        public async Task UpdateMutationWithVariablesAndMappings()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [__column1] AS [column1], [__column2] AS [column2]
                FROM [GQLmappings]
                WHERE [GQLmappings].[__column1] = 3
                    AND [GQLmappings].[__column2] = 'Updated Value of Mapped Column'
                ORDER BY [__column1]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await UpdateMutationWithVariablesAndMappings(msSqlQuery);
        }

        /// <summary>
        /// Demonstrates that using mapped column names for fields within the GraphQL mutation results in successful engine processing
        /// of removal of the record where column1 = $id and the returned object representing the deleting record utilizes the mapped column values.
        /// </summary>
        [TestMethod]
        public async Task DeleteMutationWithVariablesAndMappings()
        {
            string msSqlQueryToVerifyDeletion = @"
                SELECT COUNT(*) AS count
                FROM [GQLmappings]
                WHERE [__column1] = 4
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string msSqlQueryForResult = @"
                SELECT TOP 1 [__column1] AS [column1], [__column2] AS [column2]
                FROM [GQLmappings]
                WHERE [__column1] = 4
                ORDER BY [__column1]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await DeleteMutationWithVariablesAndMappings(msSqlQueryForResult, msSqlQueryToVerifyDeletion);
        }

        /// <summary>
        /// <code>Do: </code>Delete an entry from a simple view
        /// <code>Check: </code>if the mutation returned result is as expected and if the entry that id has been deleted
        /// </summary>
        [TestMethod]
        public async Task DeleteFromSimpleView()
        {
            string msSqlQueryForResult = @"
                SELECT TOP 1 [id], [title]
                FROM [books_view_all]
                WHERE [id] = 1
                ORDER BY [id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string msSqlQueryToVerifyDeletion = @"
                SELECT COUNT(*) AS count
                FROM [books_view_all]
                WHERE [id] = 1
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await DeleteFromSimpleView(msSqlQueryForResult, msSqlQueryToVerifyDeletion);
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
            string msSqlQuery = @"
                SELECT TOP 1 [table0].[id] AS [id],
                    [table0].[title] AS [title],
                    [table0].[publisher_id] AS [publisher_id]
                FROM [books_publishers_view_composite_insertable] AS [table0]
                WHERE [table0].[id] = 5001
                    AND [table0].[title] = 'Book Complex View'
                    AND [table0].[publisher_id] = 1234
                ORDER BY [id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await InsertIntoInsertableComplexView(msSqlQuery);
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
                SELECT TOP 1 [table0].[id] AS [id],
                    [table0].[item_name] AS [item_name],
                    [table0].[subtotal] AS [subtotal],
                    [table0].[tax] AS [tax]
                FROM [dbo].[sales] AS [table0]
                WHERE [table0].[item_name] = 'test_name'
                ORDER BY [table0].[id] ASC
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await CanCreateItemWithCultureInvariant(cultureInfo, msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code> Execute a stored procedure and return the typename of the SP entity
        /// <code>Check :</code>if the mutation executed successfully and returned the correct typename
        /// </summary>
        [TestMethod]
        public async override Task ExecuteMutationWithOnlyTypenameInSelectionSet()
        {
            await base.ExecuteMutationWithOnlyTypenameInSelectionSet();
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
            string expectedExceptionMessageSubString = "The INSERT statement conflicted";
            string msSqlQuery = @"
                SELECT COUNT(*) AS count
                FROM [books]
                WHERE [books].[publisher_id] = - 1
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await InsertWithInvalidForeignKey(msSqlQuery, expectedExceptionMessageSubString);
        }

        /// <summary>
        /// <code>Do: </code>edit a book with an invalid foreign key
        /// <code>Check: </code>that GraphQL returns an error and the book has not been editted
        /// </summary>
        [TestMethod]
        public async Task UpdateWithInvalidForeignKey()
        {
            string expectedErrorMessageSubString = "The UPDATE statement conflicted with the FOREIGN KEY constraint";
            string msSqlQuery = @"
                SELECT COUNT(*) AS count
                FROM [books]
                WHERE [books].[id] = 1
                    AND [books].[publisher_id] = - 1
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await UpdateWithInvalidForeignKey(msSqlQuery, expectedErrorMessageSubString);
        }

        /// <summary>
        /// <code>Do: </code>use an update mutation without passing any of the optional new values to update
        /// <code>Check: </code>check that GraphQL returns an appropriate exception to the user
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
            string expectedErrorMessageSubString = "Violation of UNIQUE KEY constraint";
            await TestViolatingOneToOneRelationship(expectedErrorMessageSubString);
        }
        #endregion
    }
}
