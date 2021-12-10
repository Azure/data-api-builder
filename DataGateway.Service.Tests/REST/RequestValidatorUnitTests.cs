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
            catch (PrimaryKeyValidationException ex)
            {
                //If we are not expecting an exception, fail the test. Completing test method without
                //failure will pass the test, so no Assert.Pass() is necessary (nor exists).
                if (!expectsException)
                {
                    Console.Error.WriteLine(ex.Message);
                    Assert.Fail();
                }
            }
            catch (Exception ex)
            {
                //Any exception that is not validation related means another
                //unexpected issue was encountered.
                Console.Error.WriteLine(ex.Message);
                Assert.Fail();
            }
        }
        #endregion
    }
}
