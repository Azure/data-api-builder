using Cosmos.GraphQL.Service.Controllers;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Service.Resolvers;
using Cosmos.GraphQL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.Tests
{
    [TestClass, TestCategory(TestCategory.Cosmos)]
    public class MutationTests : TestBase
    {


        [TestMethod]
        public async Task TestMutationRun()
        {

            // Add mutation resolver
            this.controller.addMutationResolver(TestHelper.SampleMutationResolver());

            // Run mutation;
            controller.ControllerContext.HttpContext = GetHttpContextWithBody(TestHelper.SampleMutation);
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
