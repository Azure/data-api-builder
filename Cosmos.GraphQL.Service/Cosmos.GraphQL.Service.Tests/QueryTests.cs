using Cosmos.GraphQL.Service.Controllers;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Service.Resolvers;
using Cosmos.GraphQL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.Tests
{
    [TestClass, TestCategory(TestCategory.Cosmos)]
    public class QueryTests : TestBase
    {


        [TestMethod]
        public async Task TestSimpleQuery()
        {

            // Add query resolver
            this.controller.addResolver(TestHelper.SampleQueryResolver());

            // Run query
            controller.ControllerContext.HttpContext = GetHttpContextWithBody(TestHelper.SampleQuery);
            JsonDocument response = await controller.Post();

            // Validate results
            Assert.IsFalse(response.ToString().Contains("Error"));
        }


        /* [ClassInitialize]
         public void Init()
         {

         }
        */

    }
}
