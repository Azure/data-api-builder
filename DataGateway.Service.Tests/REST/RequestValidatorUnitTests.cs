using System;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.REST
{
    /// <summary>
    /// Unit tests for RequestValidator.cs. Makes sure the proper primary key validation
    /// occurs for REST requests for FindOne().
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class RequestValidatorUnitTests
    {
        private static Mock<IMetadataStoreProvider> _metadataStore;

        [ClassInitialize]
        public static void InitializeTestFixture(TestContext context)
        {
            _metadataStore = new Mock<IMetadataStoreProvider>();
        }

        #region Positive Tests
        /// <summary>
        /// Simulated client request defines primary key column name and value
        /// aligning to DB schema primary key.
        /// </summary>
        [TestMethod]
        public void MatchingPrimaryKeyTest()
        {
            string[] primaryKeys = new string[] { "id" };
            TableDefinition tableDef = new();
            tableDef.PrimaryKey = new(primaryKeys);
            _metadataStore.Setup(x => x.GetTableDefinition(It.IsAny<string>())).Returns(tableDef);

            FindRequestContext findRequestContext = new(entityName: "entity", isList: false);
            string primaryKeyRoute = "id/1";
            RequestParser.ParsePrimaryKey(primaryKeyRoute, findRequestContext);

            PerformTest(findRequestContext, _metadataStore.Object, expectsException: false);
        }

        /// <summary>
        /// Simulated client request supplies all two columns of the composite primary key
        /// and supplies values for each.
        /// </summary>
        [TestMethod]
        public void MatchingCompositePrimaryKeyOrdered()
        {
            string[] primaryKeys = new string[] { "id", "isbn" };
            TableDefinition tableDef = new();
            tableDef.PrimaryKey = new(primaryKeys);
            _metadataStore.Setup(x => x.GetTableDefinition(It.IsAny<string>())).Returns(tableDef);
            FindRequestContext findRequestContext = new(entityName: "entity", isList: false);
            string primaryKeyRoute = "id/2/isbn/12345";
            RequestParser.ParsePrimaryKey(primaryKeyRoute, findRequestContext);

            PerformTest(findRequestContext, _metadataStore.Object, expectsException: false);
        }

        /// <summary>
        /// Simulated client request supplies all two columsn of composite primary key,
        /// although in a different order than what is defined in the DB schema. This is okay
        /// because the primary key columns are added to the where clause during query generation.
        /// </summary>
        [TestMethod]
        public void MatchingCompositePrimaryKeyNotOrdered()
        {
            string[] primaryKeys = new string[] { "id", "isbn" };
            TableDefinition tableDef = new();
            tableDef.PrimaryKey = new(primaryKeys);
            _metadataStore.Setup(x => x.GetTableDefinition(It.IsAny<string>())).Returns(tableDef);
            FindRequestContext findRequestContext = new(entityName: "entity", isList: false);
            string primaryKeyRoute = "isbn/12345/id/2";
            RequestParser.ParsePrimaryKey(primaryKeyRoute, findRequestContext);

            PerformTest(findRequestContext, _metadataStore.Object, expectsException: false);
        }
        #endregion
        #region Negative Tests
        /// <summary>
        /// Simluated client request contains matching number of Primary Key columns,
        /// but defines column that is NOT a primary key.
        /// </summary>
        [TestMethod]
        public void RequestWithInvalidPrimaryKeyTest()
        {
            string[] primaryKeys = new string[] { "id" };
            TableDefinition tableDef = new();
            tableDef.PrimaryKey = new(primaryKeys);
            _metadataStore.Setup(x => x.GetTableDefinition(It.IsAny<string>())).Returns(tableDef);
            FindRequestContext findRequestContext = new(entityName: "entity", isList: false);
            string primaryKeyRoute = "name/Catch22";
            RequestParser.ParsePrimaryKey(primaryKeyRoute, findRequestContext);

            PerformTest(findRequestContext, _metadataStore.Object, expectsException: true);
        }

        /// <summary>
        /// Simulated client request does not have matching number of primary key columns (too many).
        /// Should be invalid even though both request columns note a "valid" primary key column.
        /// </summary>
        [TestMethod]
        public void RequestWithDuplicatePrimaryKeyTest()
        {
            string[] primaryKeys = new string[] { "id" };
            TableDefinition tableDef = new();
            tableDef.PrimaryKey = new(primaryKeys);
            _metadataStore.Setup(x => x.GetTableDefinition(It.IsAny<string>())).Returns(tableDef);
            FindRequestContext findRequestContext = new(entityName: "entity", isList: false);
            string primaryKeyRoute = "id/1/id/1";
            RequestParser.ParsePrimaryKey(primaryKeyRoute, findRequestContext);

            PerformTest(findRequestContext, _metadataStore.Object, expectsException: true);
        }

        /// <summary>
        /// Simulated client request matches number of DB primary key columns, though
        /// duplicates one of the valid columns. This requires business logic to keep track
        /// of which request columns have been evaluated.
        /// </summary>
        [TestMethod]
        public void RequestWithDuplicatePrimaryKeyColumnAndCorrectColumnCountTest()
        {
            string[] primaryKeys = new string[] { "id", "name" };
            TableDefinition tableDef = new();
            tableDef.PrimaryKey = new(primaryKeys);
            _metadataStore.Setup(x => x.GetTableDefinition(It.IsAny<string>())).Returns(tableDef);
            FindRequestContext findRequestContext = new(entityName: "entity", isList: false);
            string primaryKeyRoute = "id/1/id/1";
            RequestParser.ParsePrimaryKey(primaryKeyRoute, findRequestContext);

            PerformTest(findRequestContext, _metadataStore.Object, expectsException: true);
        }

        /// <summary>
        /// Simulated client request does not match number of primary key columns in DB.
        /// The request only includes one of two columns of composite primary key.
        /// </summary>
        [TestMethod]
        public void RequestWithIncompleteCompositePrimaryKeyTest()
        {
            string[] primaryKeys = new string[] { "id", "name" };
            TableDefinition tableDef = new();
            tableDef.PrimaryKey = new(primaryKeys);
            _metadataStore.Setup(x => x.GetTableDefinition(It.IsAny<string>())).Returns(tableDef);
            FindRequestContext findRequestContext = new(entityName: "entity", isList: false);
            string primaryKeyRoute = "name/1";
            RequestParser.ParsePrimaryKey(primaryKeyRoute, findRequestContext);

            PerformTest(findRequestContext, _metadataStore.Object, expectsException: true);
        }

        /// <summary>
        /// Simulated client request for composit primary key, however one of two columns do
        /// not match DB schema.
        /// </summary>
        [TestMethod]
        public void IncompleteRequestCompositePrimaryKeyTest()
        {
            string[] primaryKeys = new string[] { "id", "isbn" };
            TableDefinition tableDef = new();
            tableDef.PrimaryKey = new(primaryKeys);
            _metadataStore.Setup(x => x.GetTableDefinition(It.IsAny<string>())).Returns(tableDef);
            FindRequestContext findRequestContext = new(entityName: "entity", isList: false);
            string primaryKeyRoute = "id/12345/name/2";
            RequestParser.ParsePrimaryKey(primaryKeyRoute, findRequestContext);

            PerformTest(findRequestContext, _metadataStore.Object, expectsException: true);
        }

        /// <summary>
        /// Simulated client request includes all matching DB primary key columns, but includes
        /// extraneous columns rendering the request invalid.
        /// </summary>
        [TestMethod]
        public void BloatedRequestCompositePrimaryKeyTest()
        {
            string[] primaryKeys = new string[] { "id", "isbn" };
            TableDefinition tableDef = new();
            tableDef.PrimaryKey = new(primaryKeys);
            _metadataStore.Setup(x => x.GetTableDefinition(It.IsAny<string>())).Returns(tableDef);
            FindRequestContext findRequestContext = new(entityName: "entity", isList: false);
            string primaryKeyRoute = "id/12345/isbn/2/name/TwoTowers";
            RequestParser.ParsePrimaryKey(primaryKeyRoute, findRequestContext);

            PerformTest(findRequestContext, _metadataStore.Object, expectsException: true);
        }
        #endregion
        #region Helper Methods
        /// <summary>
        /// Runs the Validation method to show success/failure. Extracted to separate helper method
        /// to avoid code duplication. Only attempt to catch DatagatewayException since
        /// that exception determines whether we encounter an expected validation failure in case
        /// of negative tests, vs downstream service failure.
        /// </summary>
        /// <param name="findRequestContext">Client simulated request</param>
        /// <param name="metadataStore">Mocked Config provider</param>
        /// <param name="expectsException">True/False whether we expect validation to fail.</param>
        public static void PerformTest(FindRequestContext findRequestContext, IMetadataStoreProvider metadataStore, bool expectsException)
        {
            try
            {
                RequestValidator.ValidateFindRequest(findRequestContext, _metadataStore.Object);

                //If expecting an exception, the code should not reach this point.
                if (expectsException)
                {
                    Assert.Fail();
                }
            }
            catch (DatagatewayException ex)
            {
                //If we are not expecting an exception, fail the test. Completing test method without
                //failure will pass the test, so no Assert.Pass() is necessary (nor exists).
                if (!expectsException)
                {
                    Console.Error.WriteLine(ex.Message);
                    throw;
                }
            }
        }
        #endregion
    }
}
