// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class CosmosEngineHelperTests
    {
        [DataTestMethod]
        [DataRow(EntityActionOperation.UpdateGraphQL, EntityActionOperation.Update)]
        [DataRow(EntityActionOperation.Patch, EntityActionOperation.Update)]
        [DataRow(EntityActionOperation.Create, EntityActionOperation.Create)]
        public void AuthorizeMutation_DelegatesColumnAuthorization(
            EntityActionOperation operation,
            EntityActionOperation delegatedOperation)
        {
            Mock<IAuthorizationResolver> authorization = new();
            authorization.Setup(x => x.AreColumnsAllowedForOperation(
                "Book", It.IsAny<string>(), delegatedOperation, It.IsAny<IEnumerable<string>>())).Returns(true);
            CosmosMutationEngine engine = new(null!, null!, authorization.Object);
            IDictionary<string, object?> parameters = new Dictionary<string, object?>
            {
                ["item"] = new List<ObjectFieldNode> { new("title", "DAB") }
            };

            engine.AuthorizeMutation(CreateContext(), parameters, "Book", operation);

            authorization.Verify(x => x.AreColumnsAllowedForOperation(
                "Book", It.IsAny<string>(), delegatedOperation, It.Is<IEnumerable<string>>(c => c.Contains("title"))), Times.Once);
        }

        [TestMethod]
        public void AuthorizeMutation_DeleteDoesNotPerformColumnAuthorization()
        {
            Mock<IAuthorizationResolver> authorization = new();
            CosmosMutationEngine engine = new(null!, null!, authorization.Object);

            engine.AuthorizeMutation(CreateContext(), new Dictionary<string, object?> { ["id"] = "1" }, "Book", EntityActionOperation.Delete);

            authorization.VerifyNoOtherCalls();
        }

        [TestMethod]
        public void AuthorizeMutation_DeniedColumnsThrow()
        {
            Mock<IAuthorizationResolver> authorization = new();
            authorization.Setup(x => x.AreColumnsAllowedForOperation(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>(), It.IsAny<IEnumerable<string>>())).Returns(false);
            CosmosMutationEngine engine = new(null!, null!, authorization.Object);

            Assert.ThrowsException<DataApiBuilderException>(() => engine.AuthorizeMutation(
                CreateContext(),
                new Dictionary<string, object?> { ["item"] = new List<ObjectFieldNode> { new("title", "DAB") } },
                "Book",
                EntityActionOperation.Create));
        }

        [TestMethod]
        public void AuthorizeMutation_UnsupportedOperationThrows()
        {
            CosmosMutationEngine engine = new(null!, null!, new Mock<IAuthorizationResolver>().Object);

            Assert.ThrowsException<DataApiBuilderException>(() => engine.AuthorizeMutation(
                CreateContext(), new Dictionary<string, object?>(), "Book", EntityActionOperation.Read));
        }

        [TestMethod]
        public void ParseVariableInputItem_MapsNonNullProperties()
        {
            object? result = InvokeMutation("ParseVariableInputItem", new Dictionary<string, object?>
            {
                ["title"] = "DAB",
                ["ignored"] = null,
                ["nested"] = new Dictionary<string, object?> { ["id"] = 7 }
            });

            JObject parsed = (JObject)result!;
            Assert.AreEqual("DAB", parsed["title"]!.Value<string>());
            Assert.IsNull(parsed["ignored"]);
            Assert.AreEqual(7, parsed["nested"]!["id"]!.Value<int>());
            Assert.AreEqual("value", InvokeMutation("ParseVariableInputItem", "value"));
        }

        [TestMethod]
        public void ParseInlineInputItem_HandlesObjectListArrayAndPrimitiveValues()
        {
            JObject node = (JObject)InvokeMutation("ParseInlineInputItem", new ObjectFieldNode("title", "DAB"))!;
            JObject list = (JObject)InvokeMutation("ParseInlineInputItem", new List<ObjectFieldNode>
            {
                new("title", "DAB"),
                new("count", 7)
            })!;
            Mock<IValueNode> nestedElement = new();
            nestedElement.SetupGet(x => x.Kind).Returns(SyntaxKind.ObjectValue);
            nestedElement.SetupGet(x => x.Value).Returns(new List<ObjectFieldNode> { new("id", 2) });
            JArray array = (JArray)InvokeMutation("ParseInlineInputItem", new List<IValueNode>
            {
                new StringValueNode("one"),
                nestedElement.Object
            })!;

            Assert.AreEqual("DAB", node["title"]!.Value<string>());
            Assert.AreEqual("DAB", list["title"]!.Value<string>());
            Assert.AreEqual(7, list["count"]!.Value<int>());
            Assert.AreEqual("one", array[0]!.Value<string>());
            Assert.AreEqual(2, array[1]!["id"]!.Value<int>());
            Assert.AreEqual(9, InvokeMutation("ParseInlineInputItem", 9));
        }

        [TestMethod]
        public void GeneratePatchOperations_CreatesLeafAndArrayOperations()
        {
            JObject input = JObject.Parse(@"{ ""name"": ""DAB"", ""nested"": { ""id"": 7 }, ""tags"": [""a""] }");
            List<Microsoft.Azure.Cosmos.PatchOperation> operations = new();

            InvokeMutation("GeneratePatchOperations", input, string.Empty, operations);

            Assert.AreEqual(3, operations.Count);
            CollectionAssert.AreEquivalent(new[] { "/name", "/nested/id", "/tags" }, operations.Select(o => o.Path).ToArray());
        }

        [DataTestMethod]
        [DataRow(null, null)]
        [DataRow("continuation", "Y29udGludWF0aW9u")]
        public void Base64Helpers_RoundTrip(string? plain, string? encoded)
        {
            Assert.AreEqual(encoded, InvokeQuery("Base64Encode", plain));
            Assert.AreEqual(plain, InvokeQuery("Base64Decode", encoded));
        }

        [TestMethod]
        public async Task UnsupportedCosmosEngineEntryPointsThrow()
        {
            CosmosMutationEngine mutation = new(null!, null!, new Mock<IAuthorizationResolver>().Object);
            CosmosQueryEngine query = (CosmosQueryEngine)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(CosmosQueryEngine));

            await Assert.ThrowsExceptionAsync<NotImplementedException>(() => mutation.ExecuteAsync((RestRequestContext)null!));
            await Assert.ThrowsExceptionAsync<NotImplementedException>(() => query.ExecuteAsync((FindRequestContext)null!));
            await Assert.ThrowsExceptionAsync<NotImplementedException>(() => query.ExecuteAsync((StoredProcedureRequestContext)null!, string.Empty));
        }

        private static IMiddlewareContext CreateContext()
        {
            Mock<IMiddlewareContext> context = new();
            context.SetupGet(x => x.ContextData).Returns(new Dictionary<string, object?>
            {
                [AuthorizationResolver.CLIENT_ROLE_HEADER] = new StringValues(AuthorizationResolver.ROLE_ANONYMOUS)
            });
            return context.Object;
        }

        private static object? InvokeMutation(string methodName, params object?[] args)
        {
            MethodInfo method = typeof(CosmosMutationEngine).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
            return method.Invoke(null, args);
        }

        private static object? InvokeQuery(string methodName, params object?[] args)
        {
            MethodInfo method = typeof(CosmosQueryEngine).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
            return method.Invoke(null, args);
        }
    }
}
