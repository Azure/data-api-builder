// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
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
using Microsoft.Azure.Cosmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
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
                CreateContext(),
                new Dictionary<string, object?>
                {
                    [MutationBuilder.ITEM_INPUT_ARGUMENT_NAME] = new List<ObjectFieldNode>()
                },
                "Book",
                EntityActionOperation.Read));
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
        public void ParseInlineInputItem_HandlesNestedObjectValue()
        {
            Mock<IValueNode> nested = new();
            nested.SetupGet(x => x.Kind).Returns(SyntaxKind.ObjectValue);
            nested.SetupGet(x => x.Value).Returns(new List<ObjectFieldNode> { new("id", 7) });
            JObject result = (JObject)InvokeMutation(
                "ParseInlineInputItem",
                new List<ObjectFieldNode> { new("nested", nested.Object) })!;

            Assert.AreEqual(7, result["nested"]!["id"]!.Value<int>());

            JObject directResult = (JObject)InvokeMutation(
                "ParseInlineInputItem",
                new ObjectFieldNode("nested", nested.Object))!;
            Assert.AreEqual(7, directResult["nested"]!["id"]!.Value<int>());
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
            CosmosQueryEngine query = (CosmosQueryEngine)RuntimeHelpers.GetUninitializedObject(typeof(CosmosQueryEngine));

            await Assert.ThrowsExceptionAsync<NotImplementedException>(() => mutation.ExecuteAsync((RestRequestContext)null!));
            await Assert.ThrowsExceptionAsync<NotImplementedException>(() => mutation.ExecuteAsync((StoredProcedureRequestContext)null!, string.Empty));
            await Assert.ThrowsExceptionAsync<NotImplementedException>(() => query.ExecuteAsync((FindRequestContext)null!));
            await Assert.ThrowsExceptionAsync<NotImplementedException>(() => query.ExecuteAsync((StoredProcedureRequestContext)null!, string.Empty));
        }

        [TestMethod]
        public async Task ExecuteMutation_NullArgumentsAndMissingClientThrow()
        {
            CosmosClientProvider clientProvider = CreateClientProvider(new Dictionary<string, CosmosClient?>
            {
                ["cosmos"] = null
            });
            CosmosMutationEngine engine = new(clientProvider, null!, Mock.Of<IAuthorizationResolver>());
            CosmosOperationMetadata operation = new("db", "container", EntityActionOperation.Create);

            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                InvokeMutationAsync(engine, null!, operation, "cosmos"));
            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() =>
                InvokeMutationAsync(engine, new Dictionary<string, object?>(), operation, "cosmos"));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed, exception.SubStatusCode);
        }

        [TestMethod]
        public async Task MutationHandlers_ValidateRequiredArguments()
        {
            Container container = Mock.Of<Container>();

            await AssertPrivateThrowsAsync<InvalidDataException>(
                "HandleDeleteAsync",
                new Dictionary<string, object?>(),
                container);
            await AssertPrivateThrowsAsync<InvalidDataException>(
                "HandleDeleteAsync",
                new Dictionary<string, object?> { [QueryBuilder.ID_FIELD_NAME] = "1" },
                container);
            await AssertPrivateThrowsAsync<InvalidDataException>(
                "HandleUpdateAsync",
                new Dictionary<string, object?>(),
                container);
            await AssertPrivateThrowsAsync<InvalidDataException>(
                "HandleUpdateAsync",
                new Dictionary<string, object?> { [QueryBuilder.ID_FIELD_NAME] = "1" },
                container);
            await AssertPrivateThrowsAsync<InvalidDataException>(
                "HandlePatchAsync",
                new Dictionary<string, object?>(),
                container);
            await AssertPrivateThrowsAsync<InvalidDataException>(
                "HandlePatchAsync",
                new Dictionary<string, object?> { [QueryBuilder.ID_FIELD_NAME] = "1" },
                container);
        }

        [DataTestMethod]
        [DataRow("HandleCreateAsync", false)]
        [DataRow("HandleUpdateAsync", true)]
        [DataRow("HandlePatchAsync", true)]
        public async Task MutationHandlers_InvalidInputThrows(string methodName, bool requiresKeys)
        {
            Dictionary<string, object?> arguments = new()
            {
                [MutationBuilder.ITEM_INPUT_ARGUMENT_NAME] = null
            };
            if (requiresKeys)
            {
                arguments[QueryBuilder.ID_FIELD_NAME] = "1";
                arguments[QueryBuilder.PARTITION_KEY_FIELD_NAME] = "tenant";
            }

            await AssertPrivateThrowsAsync<InvalidDataException>(methodName, arguments, Mock.Of<Container>());
        }

        [DataTestMethod]
        [DataRow("HandleCreateAsync", false)]
        [DataRow("HandleUpdateAsync", true)]
        public async Task MutationHandlers_AcceptVariableInput(string methodName, bool requiresKeys)
        {
            Dictionary<string, object?> arguments = new()
            {
                [MutationBuilder.ITEM_INPUT_ARGUMENT_NAME] = new Dictionary<string, object?> { ["title"] = "DAB" }
            };
            if (requiresKeys)
            {
                arguments[QueryBuilder.ID_FIELD_NAME] = "1";
                arguments[QueryBuilder.PARTITION_KEY_FIELD_NAME] = "tenant";
            }

            Mock<ItemResponse<JObject>> response = new();
            response.SetupGet(x => x.Resource).Returns(JObject.Parse(@"{ ""title"": ""DAB"" }"));
            Mock<Container> container = new();
            container.Setup(x => x.CreateItemAsync(
                    It.IsAny<JObject>(),
                    It.IsAny<PartitionKey?>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response.Object);
            container.Setup(x => x.ReplaceItemAsync(
                    It.IsAny<JObject>(),
                    It.IsAny<string>(),
                    It.IsAny<PartitionKey>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response.Object);

            await InvokePrivateMutationAsync(methodName, arguments, container.Object);
        }

        [TestMethod]
        public async Task HandlePatchAsync_VariableInputWithinLimitReturnsResource()
        {
            Dictionary<string, object?> arguments = CreatePatchArguments(1);
            JObject resource = JObject.Parse(@"{ ""id"": ""1"" } ");
            Mock<ItemResponse<JObject>> response = new();
            response.SetupGet(x => x.Resource).Returns(resource);
            Mock<Container> container = new();
            container.Setup(x => x.PatchItemAsync<JObject>(
                    "1",
                    It.IsAny<PartitionKey>(),
                    It.IsAny<IReadOnlyList<PatchOperation>>(),
                    It.IsAny<PatchItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response.Object);

            JObject result = (JObject)(await InvokePrivateMutationAsync("HandlePatchAsync", arguments, container.Object))!;

            Assert.AreSame(resource, result);
        }

        [TestMethod]
        public async Task HandlePatchAsync_FailedTransactionalBatchThrows()
        {
            Dictionary<string, object?> arguments = CreatePatchArguments(11);
            Mock<TransactionalBatchResponse> response = new();
            response.SetupGet(x => x.IsSuccessStatusCode).Returns(false);
            Mock<TransactionalBatch> batch = new();
            batch.Setup(x => x.PatchItem(It.IsAny<string>(), It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<TransactionalBatchPatchItemRequestOptions>()))
                .Returns(batch.Object);
            batch.Setup(x => x.ExecuteAsync(It.IsAny<CancellationToken>())).ReturnsAsync(response.Object);
            Mock<Container> container = new();
            container.Setup(x => x.CreateTransactionalBatch(It.IsAny<PartitionKey>())).Returns(batch.Object);

            await Assert.ThrowsExceptionAsync<DataApiBuilderException>(async () =>
                await InvokePrivateMutationAsync("HandlePatchAsync", arguments, container.Object));
        }

        [TestMethod]
        public void QueryValueHelpers_HandleMissingInputs()
        {
            Assert.IsNull(InvokeQuery("GetPartitionKeyValue", CreateContext(), null, null));
            List<ObjectFieldNode> filter = new()
            {
                new("id", new ObjectValueNode(new ObjectFieldNode("ne", 1)))
            };
            Assert.IsNull(InvokeQuery("GetIdValue", CreateContext(), filter));
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

        private static CosmosClientProvider CreateClientProvider(Dictionary<string, CosmosClient?> clients)
        {
            CosmosClientProvider provider =
                (CosmosClientProvider)RuntimeHelpers.GetUninitializedObject(typeof(CosmosClientProvider));
            typeof(CosmosClientProvider).GetField("<Clients>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(provider, clients);
            return provider;
        }

        private static async Task InvokeMutationAsync(
            CosmosMutationEngine engine,
            IDictionary<string, object?> arguments,
            CosmosOperationMetadata operation,
            string dataSourceName)
        {
            MethodInfo method = typeof(CosmosMutationEngine).GetMethod(
                "ExecuteAsync",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[]
                {
                    typeof(IMiddlewareContext),
                    typeof(IDictionary<string, object?>),
                    typeof(CosmosOperationMetadata),
                    typeof(string)
                },
                modifiers: null)!;
            Task task = (Task)method.Invoke(engine, new object?[]
            {
                CreateContext(), arguments, operation, dataSourceName
            })!;
            await task;
        }

        private static async Task AssertPrivateThrowsAsync<TException>(
            string methodName,
            IDictionary<string, object?> arguments,
            Container container)
            where TException : Exception
        {
            MethodInfo method = typeof(CosmosMutationEngine).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic)!;
            Task task = (Task)method.Invoke(null, new object[] { arguments, container })!;
            await Assert.ThrowsExceptionAsync<TException>(() => task);
        }

        private static Dictionary<string, object?> CreatePatchArguments(int propertyCount)
        {
            Dictionary<string, object?> item = Enumerable.Range(1, propertyCount)
                .ToDictionary(index => $"property{index}", index => (object?)index);
            return new Dictionary<string, object?>
            {
                [QueryBuilder.ID_FIELD_NAME] = "1",
                [QueryBuilder.PARTITION_KEY_FIELD_NAME] = "tenant",
                [MutationBuilder.ITEM_INPUT_ARGUMENT_NAME] = item
            };
        }

        private static async Task<object?> InvokePrivateMutationAsync(
            string methodName,
            IDictionary<string, object?> arguments,
            Container container)
        {
            MethodInfo method = typeof(CosmosMutationEngine).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic)!;
            dynamic task = method.Invoke(null, new object[] { arguments, container })!;
            return await task;
        }
    }
}
