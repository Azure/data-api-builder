using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Services;
using Microsoft.OData.UriParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.REST
{
    /// <summary>
    /// Unit tests for RequestValidator.cs. Makes sure the proper primary key validation
    /// occurs for REST requests for FindOne().
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class ODataASTVisitorUnitTests
    {
        private static FilterParser _filterParser;

        [ClassInitialize]
        public static void InitializeTestFixture(TestContext context)
        {
            string jsonString = File.ReadAllText("sql-config.json");
            JsonSerializerOptions options = new()
            {
                PropertyNameCaseInsensitive = true,
            };
            options.Converters.Add(new JsonStringEnumConverter());

            ResolverConfig? deserializedConfig;
            deserializedConfig = JsonSerializer.Deserialize<ResolverConfig>(jsonString, options);
            _filterParser = new(deserializedConfig.DatabaseSchema);
        }

        #region Positive Tests
        /// <summary>
        /// Simulated client request defines primary key column name and value
        /// aligning to DB schema primary key.
        /// </summary>
        [TestMethod]
        public void VisitorLeftNullRightValueFilterTest()
        {
            // (RestRequestContext context, IMetadataStoreProvider metadataStoreProvider)
            Mock<IMetadataStoreProvider> metaDataStore = new();
            Mock<RestRequestContext> context = new();
            string entityName = "books";
            context.SetupGet(x => x.EntityName).Returns(entityName);
            string filterString = "?$filter=NULL eq id";
            FilterClause ast = _filterParser.GetFilterClause(filterString, "books");
            ODataASTVisitor visitor = new(new SqlQueryStructure(context.Object, metaDataStore.Object));
            string filter = ast.Expression.Accept<string>(visitor);
        }

        #endregion
        #region Negative Tests
        /// <summary>
        /// Simulated client request contains matching number of Primary Key columns,
        /// but defines column that is NOT a primary key. We verify that the correct
        /// status code and sub status code is a part of the DataGatewayException thrown.
        /// </summary>
        [TestMethod]
        public void InvalidEdmTypeBoolTest()
        {
            // (RestRequestContext context, IMetadataStoreProvider metadataStoreProvider)
            Mock<IMetadataStoreProvider> metaDataStore = new();
            Mock<RestRequestContext> context = new();
            string entityName = "books";
            context.SetupGet(x => x.EntityName).Returns(entityName);
            string filterString = "?$filter=id eq (publisher_id > 1)";
            FilterClause ast = _filterParser.GetFilterClause(filterString, "books");
            ODataASTVisitor visitor = new(new SqlQueryStructure(context.Object, metaDataStore.Object));
            string filter = ast.Expression.Accept<string>(visitor);
        }

        #endregion
        #region Helper Methods
        /// <summary>
        /// Runs the Validation method to show success/failure. Extracted to separate helper method
        /// to avoid code duplication. Only attempt to catch DataGatewayException since
        /// that exception determines whether we encounter an expected validation failure in case
        /// of negative tests, vs downstream service failure.
        /// </summary>
        /// <param name="findRequestContext">Client simulated request</param>
        /// <param name="metadataStore">Mocked Config provider</param>
        /// <param name="expectsException">True/False whether we expect validation to fail.</param>
        /// <param name="statusCode">Integer which represents the http status code expected to return.</param>
        /// <param name="subStatusCode">Represents the sub status code that we expect to return.</param>
        public static void PerformTest(
            FindRequestContext findRequestContext,
            IMetadataStoreProvider metadataStore,
            bool expectsException,
            HttpStatusCode statusCode = HttpStatusCode.BadRequest,
            DataGatewayException.SubStatusCodes subStatusCode = DataGatewayException.SubStatusCodes.BadRequest)
        {
            //try
            //{
            //    RequestValidator.ValidatePrimaryKey(findRequestContext, _metadataStore.Object);

            //    //If expecting an exception, the code should not reach this point.
            //    if (expectsException)
            //    {
            //        Assert.Fail();
            //    }
            //}
            //catch (DataGatewayException ex)
            //{
            //    //If we are not expecting an exception, fail the test. Completing test method without
            //    //failure will pass the test, so no Assert.Pass() is necessary (nor exists).
            //    if (!expectsException)
            //    {
            //        Console.Error.WriteLine(ex.Message);
            //        throw;
            //    }

            //    // validates the status code and sub status code match the expected values.
            //    Assert.AreEqual(statusCode, ex.StatusCode);
            //    Assert.AreEqual(subStatusCode, ex.SubStatusCode);
            //}
        }

        /// <summary>
        /// Tries to parse a primary key route that contains duplicates.
        /// Expectation is that this should always throw bad request exception.
        /// </summary>
        //private static void PerformDuplicatePrimaryKeysTest(string[] primaryKeys)
        //{
        //FindRequestContext findRequestContext = new(entityName: "entity", isList: false);
        //StringBuilder primaryKeyRoute = new();

        //foreach (string key in primaryKeys)
        //{
        //    primaryKeyRoute.Append($"{key}/1/{key}/1/");
        //}

        //// Remove the trailing slash
        //primaryKeyRoute.Remove(primaryKeyRoute.Length - 1, 1);

        //try
        //{
        //    RequestParser.ParsePrimaryKey(primaryKeyRoute.ToString(), findRequestContext);

        //    Assert.Fail();
        //}
        //catch (DataGatewayException ex)
        //{
        //    // validates the status code and sub status code match the expected values.
        //    Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        //    Assert.AreEqual(DataGatewayException.SubStatusCodes.BadRequest, ex.SubStatusCode);
        //}
    }

    /// <summary>
    /// Tests the ParsePrimaryKey method in the RequestParser class.
    /// </summary>
    //private static void PerformRequestParserPrimaryKeyTest(
    //    FindRequestContext findRequestContext,
    //    string primaryKeyRoute,
    //    bool expectsException,
    //    HttpStatusCode statusCode = HttpStatusCode.BadRequest,
    //    DataGatewayException.SubStatusCodes subStatusCode = DataGatewayException.SubStatusCodes.BadRequest)
    //{
    //    try
    //    {
    //        RequestParser.ParsePrimaryKey(primaryKeyRoute.ToString(), findRequestContext);

    //        //If expecting an exception, the code should not reach this point.
    //        Assert.IsFalse(expectsException, "No exception thrown when exception expected.");
    //    }
    //    catch (DataGatewayException ex)
    //    {
    //        //If we are not expecting an exception, fail the test. Completing test method without
    //        //failure will pass the test, so no Assert.Pass() is necessary (nor exists).
    //        if (!expectsException)
    //        {
    //            Console.Error.WriteLine(ex.Message);
    //            throw;
    //        }

    //        // validates the status code and sub status code match the expected values.
    //        Assert.AreEqual(statusCode, ex.StatusCode);
    //        Assert.AreEqual(subStatusCode, ex.SubStatusCode);
    //    }
    //}

    #endregion
}
