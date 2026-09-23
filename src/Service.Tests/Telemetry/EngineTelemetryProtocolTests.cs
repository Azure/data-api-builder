// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.AuthenticationHelpers;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Telemetry;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;
using GraphQLRequestContext = HotChocolate.Execution.RequestContext;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    [TestClass]
    [TestCategory("EngineTelemetry")]
    public class EngineTelemetryProtocolTests
    {
        [DataTestMethod]
        [DataRow("GET", "/data/books", true)]
        [DataRow("POST", "/data/books", true)]
        [DataRow("PUT", "/data/books/id/1", true)]
        [DataRow("PATCH", "/DATA/books/id/1", true)]
        [DataRow("DELETE", "/data/books/id/1", true)]
        [DataRow("OPTIONS", "/data/books", false)]
        [DataRow("HEAD", "/data/books", false)]
        [DataRow("GET", "/data", false)]
        [DataRow("GET", "/data/", false)]
        [DataRow("GET", "/database/books", false)]
        [DataRow("GET", "/api/books", false)]
        [DataRow("GET", "/", false)]
        [DataRow("GET", "/health", false)]
        [DataRow("POST", "/configuration", false)]
        [DataRow("GET", "/graphql", false)]
        [DataRow("POST", "/data/query", false)]
        [DataRow("POST", "/data/tools", false)]
        [DataRow("GET", "/swagger/index.html", false)]
        [DataRow("GET", "/data/openapi", false)]
        [DataRow("GET", "/data/OPENAPI/custom-role", false)]
        [DataRow("GET", "/data/swagger/index.html", true)]
        [DataRow("GET", "/data/favicon.ico", false)]
        public void RestEligibilityUsesConfiguredSegmentsAndExcludesProtocolAndDiscoveryTraffic(string method, string path, bool eligible)
        {
            DefaultHttpContext context = new();
            context.Request.Method = method;
            context.Request.Path = path;
            // The real REST controller is a catch-all; docs also receive its metadata.
            SetController(context, typeof(RestController));
            if (path is "/data/query" or "/data/tools")
            {
                // Actual GraphQL/MCP endpoints are not owned by the REST controller.
                context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(), "synthetic protocol endpoint"));
            }

            Assert.AreEqual(eligible, EngineTelemetryHttpMiddleware.IsDataRequest(context, CreateConfig()));
        }

        [TestMethod]
        public void RestEligibilityRejectsOtherControllersAndDisabledRest()
        {
            DefaultHttpContext context = new();
            context.Request.Method = "GET";
            context.Request.Path = "/data/books";
            SetController(context, typeof(HealthController));
            Assert.IsFalse(EngineTelemetryHttpMiddleware.IsDataRequest(context, CreateConfig()));

            SetController(context, typeof(RestController));
            RuntimeConfig config = CreateConfig();
            config = config with { Runtime = config.Runtime! with { Rest = new(Enabled: false, Path: "/data") } };
            Assert.IsFalse(EngineTelemetryHttpMiddleware.IsDataRequest(context, config));
        }

        [DataTestMethod]
        [DataRow(true, "/embed")]
        [DataRow(false, "/embed")]
        [DataRow(true, "/custom-vector-endpoint")]
        [DataRow(false, "/custom-vector-endpoint")]
        public void MappedEmbeddingEndpointIsIndependentOfRestPathAndEnablement(bool restEnabled, string path)
        {
            RuntimeConfig config = CreateConfig();
            config = config with { Runtime = config.Runtime! with { Rest = new(Enabled: restEnabled, Path: "/data") } };
            DefaultHttpContext context = new();
            context.Request.Method = "POST";
            context.Request.Path = path;
            Assert.IsFalse(EngineTelemetryHttpMiddleware.IsDataRequest(context, config));
            context.SetEndpoint(new Endpoint(null,
                new EndpointMetadataCollection(EngineTelemetryEmbeddingEndpointMetadata.Instance), "synthetic embedding endpoint"));
            Assert.IsTrue(EngineTelemetryHttpMiddleware.IsDataRequest(context, config));
            context.Request.Method = "OPTIONS";
            Assert.IsFalse(EngineTelemetryHttpMiddleware.IsDataRequest(context, config));
        }

        [TestMethod]
        public void RestEligibilityWorksBeforeEndpointExecutionAndWithRootRestPath()
        {
            DefaultHttpContext context = new();
            context.Request.Method = "GET";
            context.Request.Path = "/books";
            SetController(context, typeof(RestController));
            RuntimeConfig config = CreateConfig();
            config = config with { Runtime = config.Runtime! with { Rest = new(Path: "/") } };
            Assert.IsTrue(EngineTelemetryHttpMiddleware.IsDataRequest(context, config));
            context.Request.Path = "/configuration";
            SetController(context, typeof(ConfigurationController));
            Assert.IsFalse(EngineTelemetryHttpMiddleware.IsDataRequest(context, config));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void McpPathDoesNotExcludeRoutedRestDescendants(bool mcpEnabled)
        {
            RuntimeConfig config = CreateConfig();
            config = config with
            {
                Runtime = config.Runtime! with
                {
                    GraphQL = new(Enabled: false),
                    Mcp = new(Enabled: mcpEnabled, Path: "/data/tools")
                }
            };
            DefaultHttpContext context = new();
            context.Request.Method = "GET";
            context.Request.Path = "/data/tools/books";
            SetController(context, typeof(RestController));
            Assert.IsTrue(EngineTelemetryHttpMiddleware.IsDataRequest(context, config));
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(), "synthetic protocol endpoint"));
            Assert.IsFalse(EngineTelemetryHttpMiddleware.IsDataRequest(context, config));
        }

        [DataTestMethod]
        [DataRow("/data/swagger")]
        [DataRow("/data/swagger/books")]
        public void RestControllerRoutesMustNotBeGuessedToBeDocumentation(string path)
        {
            DefaultHttpContext context = new();
            context.Request.Method = "GET";
            context.Request.Path = path;
            SetController(context, typeof(RestController));
            Assert.IsTrue(EngineTelemetryHttpMiddleware.IsDataRequest(context, CreateConfig()));
        }

        [TestMethod]
        public async Task DisabledGraphQlRouteIsRejectedBeforeTelemetryMiddleware()
        {
            RuntimeConfig config = CreateConfig();
            config = config with
            {
                Runtime = config.Runtime! with
                {
                    GraphQL = new(Enabled: false, Path: "/data"),
                    Mcp = new(Enabled: false)
                }
            };
            using RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            // Startup loads the configuration before serving requests; the rewrite middleware
            // intentionally reads only an already-loaded configuration.
            RuntimeConfig loadedConfig = provider.GetConfig();
            Assert.IsFalse(loadedConfig.IsGraphQLEnabled);
            Assert.AreEqual(loadedConfig.RestPath, loadedConfig.GraphQLPath);
            DefaultHttpContext context = new();
            context.Request.Method = "GET";
            context.Request.Path = "/data/books";
            int nextCalls = 0;
            PathRewriteMiddleware rewrite = new(_ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            }, provider);

            await rewrite.InvokeAsync(context);

            Assert.AreEqual(404, context.Response.StatusCode);
            Assert.AreEqual(0, nextCalls, "Unlike disabled MCP, this path never reaches a REST data handler or telemetry middleware.");
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task DefaultAuthenticationRolesAreNotCustomRoles(bool authenticated)
        {
            using RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(CreateConfig());
            DefaultHttpContext context = new();
            Mock<IAuthenticationService> authentication = new(MockBehavior.Strict);
            AuthenticateResult result = authenticated
                ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("synthetic")), "synthetic"))
                : AuthenticateResult.NoResult();
            authentication.Setup(service => service.AuthenticateAsync(context, It.IsAny<string>())).ReturnsAsync(result);
            using ServiceProvider services = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider();
            context.RequestServices = services;
            ClientRoleHeaderAuthenticationMiddleware middleware = new(_ => Task.CompletedTask,
                NullLogger<ClientRoleHeaderAuthenticationMiddleware>.Instance, provider);

            await middleware.InvokeAsync(context);

            Assert.AreEqual(authenticated ? "Authenticated" : "Anonymous", context.Request.Headers["X-MS-API-ROLE"].ToString());
            Assert.AreEqual(authenticated ? EngineTelemetryRole.Authenticated : EngineTelemetryRole.Anonymous,
                EngineTelemetryHttpMiddleware.ClassifyRequestRole(context));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void HealthProbeMarkerRequiresThisEnabledSession(bool enabled)
        {
            using EngineTelemetrySession session = EngineTelemetrySession.Create(
                () => Mock.Of<IEngineTelemetryExporter>(), enableSyntheticCollection: enabled,
                readEnvironmentVariable: _ => null, showNotice: () => { }, startTimer: false);
            DefaultHttpContext context = new();
            Assert.IsFalse(EngineTelemetryHealthProbe.IsProbe(context, session));
            context.Request.Headers[EngineTelemetryHealthProbe.HEADER_NAME] = "synthetic-unrelated-marker";
            Assert.IsFalse(EngineTelemetryHealthProbe.IsProbe(context, session));
            if (enabled)
            {
                Assert.IsNotNull(session.HealthProbeToken);
                context.Request.Headers[EngineTelemetryHealthProbe.HEADER_NAME] = session.HealthProbeToken;
                Assert.IsTrue(EngineTelemetryHealthProbe.IsProbe(context, session));
                context.Request.Headers[EngineTelemetryHealthProbe.HEADER_NAME] = new[] { session.HealthProbeToken, session.HealthProbeToken };
                Assert.IsFalse(EngineTelemetryHealthProbe.IsProbe(context, session));
                context.Request.Headers[EngineTelemetryHealthProbe.HEADER_NAME] = session.HealthProbeToken;
                session.Disable();
                Assert.IsFalse(EngineTelemetryHealthProbe.IsProbe(context, session));
            }
            else
            {
                Assert.IsNull(session.HealthProbeToken);
            }
        }

        [TestMethod]
        public void UnvalidatedCredentialsAreNotReportedAsAuthenticatedBeforeAuthentication()
        {
            DefaultHttpContext context = new();
            context.Request.Headers.Authorization = "Bearer synthetic-invalid-input";
            Assert.AreEqual(EngineTelemetryRole.Unknown, EngineTelemetryHttpMiddleware.ClassifyRequestRole(context));
            context.Request.Headers.Remove("Authorization");
            context.Request.Headers["X-MS-API-ROLE"] = new[] { "first", "second" };
            Assert.AreEqual(EngineTelemetryRole.Unknown, EngineTelemetryHttpMiddleware.ClassifyRequestRole(context));
        }

        [TestMethod]
        public async Task HttpCompletionWaitsForResultExecutionAndUsesTheFinalStatus()
        {
            (DefaultHttpContext context, ResponseCallbacks response) = CreateHttpContext();
            List<(EngineTelemetryOutcome Outcome, int? Status)> completed = new();
            _ = new EngineTelemetryHttpCompletion(context, (outcome, status) => completed.Add((outcome, status)),
                () => EngineTelemetryOutcome.Unknown, inferSuccessFromHttp: true);

            Assert.AreEqual(0, completed.Count, "Returning an IActionResult is not response completion.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await response.CompleteAsync();
            await response.CompleteAsync();

            Assert.AreEqual(1, completed.Count);
            Assert.AreEqual((EngineTelemetryOutcome.Failure, (int?)500), completed.Single());
        }

        [TestMethod]
        public async Task Http200CannotTurnProtocolFailureOrUnknownIntoSuccess()
        {
            foreach (EngineTelemetryOutcome expected in new[]
            {
                EngineTelemetryOutcome.Failure, EngineTelemetryOutcome.PartialFailure, EngineTelemetryOutcome.Unknown
            })
            {
                (DefaultHttpContext context, ResponseCallbacks response) = CreateHttpContext();
                List<(EngineTelemetryOutcome Outcome, int? Status)> completed = new();
                _ = new EngineTelemetryHttpCompletion(context, (outcome, status) => completed.Add((outcome, status)),
                    () => expected, inferSuccessFromHttp: false);
                await response.CompleteAsync();
                Assert.AreEqual((expected, (int?)200), completed.Single());
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task AbortedResponsesCompleteExactlyOnceWithoutACompletedHttpStatus(bool alreadyCanceled)
        {
            using CancellationTokenSource cancellation = new();
            if (alreadyCanceled)
            {
                cancellation.Cancel();
            }

            (DefaultHttpContext context, ResponseCallbacks response) = CreateHttpContext(cancellation.Token);
            ConcurrentQueue<(EngineTelemetryOutcome Outcome, int? Status)> completed = new();
            EngineTelemetryHttpCompletion completion = new(context, (outcome, status) => completed.Enqueue((outcome, status)),
                () => EngineTelemetryOutcome.Success, inferSuccessFromHttp: false);
            cancellation.Cancel();
            completion.Fail(EngineTelemetryOutcome.Failure);
            await response.CompleteAsync();

            Assert.AreEqual(1, completed.Count);
            Assert.AreEqual((EngineTelemetryOutcome.Canceled, (int?)null), completed.Single());
        }

        [TestMethod]
        public async Task SuccessfulResponseCompletionWinsOverLaterCancellation()
        {
            using CancellationTokenSource cancellation = new();
            (DefaultHttpContext context, ResponseCallbacks response) = CreateHttpContext(cancellation.Token);
            List<EngineTelemetryOutcome> completed = new();
            _ = new EngineTelemetryHttpCompletion(context, (outcome, _) => completed.Add(outcome),
                () => EngineTelemetryOutcome.Success, inferSuccessFromHttp: false);
            await response.CompleteAsync();
            cancellation.Cancel();
            Assert.AreEqual(EngineTelemetryOutcome.Success, completed.Single());
        }

        [TestMethod]
        public async Task ConcurrentAbortAndResponseCompletionCannotCountTheSameRequestTwice()
        {
            for (int iteration = 0; iteration < 32; iteration++)
            {
                using CancellationTokenSource cancellation = new();
                (DefaultHttpContext context, ResponseCallbacks response) = CreateHttpContext(cancellation.Token);
                ConcurrentQueue<EngineTelemetryOutcome> completed = new();
                _ = new EngineTelemetryHttpCompletion(context, (outcome, _) => completed.Enqueue(outcome),
                    () => EngineTelemetryOutcome.Success, inferSuccessFromHttp: false);
                await Task.WhenAll(Task.Run(cancellation.Cancel), Task.Run(response.CompleteAsync));
                Assert.AreEqual(1, completed.Count);
                Assert.IsTrue(completed.Single() is EngineTelemetryOutcome.Success or EngineTelemetryOutcome.Canceled);
            }
        }

        [TestMethod]
        public async Task PipelineFailureIsNotReplacedByTheDefaultHttp200()
        {
            (DefaultHttpContext context, ResponseCallbacks response) = CreateHttpContext();
            List<(EngineTelemetryOutcome Outcome, int? Status)> completed = new();
            EngineTelemetryHttpCompletion completion = new(context, (outcome, status) => completed.Add((outcome, status)),
                () => EngineTelemetryOutcome.Success, inferSuccessFromHttp: true);
            completion.Fail(EngineTelemetryOutcome.Failure);
            await response.CompleteAsync();
            Assert.AreEqual((EngineTelemetryOutcome.Failure, (int?)null), completed.Single());
        }

        [DataTestMethod]
        [DataRow("{ __typename }", null, false)]
        [DataRow("{ alias: __schema { queryType { name } } }", null, false)]
        [DataRow("query Discovery { ...Info } fragment Info on Query { __type(name: \"Query\") { name } }", "Discovery", false)]
        [DataRow("{ ... on Query { __typename } }", null, false)]
        [DataRow("{ books { __typename } }", null, true)]
        [DataRow("{ books { id } }", "", true)]
        [DataRow("query IntrospectionQuery { books { id } }", "IntrospectionQuery", true)]
        [DataRow("{ __schema { queryType { name } } books { id } }", null, true)]
        [DataRow("query Data { ...Root } fragment Root on Query { books { id } }", "Data", true)]
        [DataRow("query Data { books { id } } query Discovery { __typename }", "Discovery", false)]
        [DataRow("query Data { books { id } } query Discovery { __typename }", "Data", true)]
        [DataRow("query Data { books { id } } query Discovery { __typename }", null, false)]
        [DataRow("query Data { books { id } }", "NotAnOperation", false)]
        [DataRow("{ ...Cycle } fragment Cycle on Query { ...Cycle __typename }", null, false)]
        public void GraphQLDiscoveryClassificationUsesTheSelectedAstNotNamesOrAliases(string document, string? operationName, bool eligible)
        {
            Assert.AreEqual(eligible, EngineTelemetryGraphQLListener.IsDataOperation(Utf8GraphQLParser.Parse(document), operationName));
        }

        [TestMethod]
        public async Task GraphQLErrorAndPartialFailureUseTypedResultsWithoutReadingOrFormattingData()
        {
            await using OperationResult failure = OperationResult.FromError(ErrorBuilder.New().SetMessage("synthetic error").Build());
            Assert.AreEqual(EngineTelemetryOutcome.Failure, EngineTelemetryGraphQLListener.ClassifyResult(failure, EngineTelemetryOutcome.Unknown, canceled: false));

            // A strict formatter would throw if the adapter tried to serialize the result.
            Mock<IRawJsonFormatter> formatter = new(MockBehavior.Strict);
            ImmutableList<IError> errors = ImmutableList.Create<IError>(ErrorBuilder.New().SetMessage("synthetic error").Build());
            await using OperationResult partial = new(new OperationResultData(new object(), false, formatter.Object, null), errors);
            Assert.AreEqual(EngineTelemetryOutcome.PartialFailure, EngineTelemetryGraphQLListener.ClassifyResult(partial, EngineTelemetryOutcome.Failure, canceled: false));

            await using OperationResult nullData = new(new OperationResultData(new object(), true, formatter.Object, null), errors);
            Assert.AreEqual(EngineTelemetryOutcome.Failure, EngineTelemetryGraphQLListener.ClassifyResult(nullData, EngineTelemetryOutcome.Unknown, canceled: false));
            Assert.AreEqual(EngineTelemetryOutcome.Canceled, EngineTelemetryGraphQLListener.ClassifyResult(partial, EngineTelemetryOutcome.Unknown, canceled: true));
            formatter.VerifyNoOtherCalls();
        }

        [TestMethod]
        public async Task GraphQLIncompleteOrMissingResultsAreNotSuccesses()
        {
            Assert.AreEqual(EngineTelemetryOutcome.Unknown, EngineTelemetryGraphQLListener.ClassifyResult(null, EngineTelemetryOutcome.Unknown, canceled: false));
            await using OperationResult incremental = new(new OperationResultData(new object(), false, Mock.Of<IRawJsonFormatter>(), null))
            {
                HasNext = true
            };
            Assert.AreEqual(EngineTelemetryOutcome.Unknown, EngineTelemetryGraphQLListener.ClassifyResult(incremental, EngineTelemetryOutcome.Unknown, canceled: false));
        }

        /// <summary>
        /// Verifies the installed SDK contract on which EngineTelemetryGraphQLListener depends:
        /// synchronous ExecuteRequest establishes ambient state before async resolver dispatch.
        /// Core session tests separately exercise the product scope's own ambient storage.
        /// </summary>
        [TestMethod]
        public async Task HotChocolateSynchronousDiagnosticScopeFlowsToParallelResolversAndChildTasks()
        {
            DiagnosticScopeProbe probe = new();
            ServiceCollection services = new();
            AddProbeSchema(services, probe);
            await using ServiceProvider provider = services.BuildServiceProvider();
            IRequestExecutor executor = await provider.GetRequestExecutorAsync();

            IExecutionResult[] results = await Task.WhenAll(
                executor.ExecuteAsync(OperationRequestBuilder.New().SetDocument("{ left: value right: value }").Build()),
                executor.ExecuteAsync(OperationRequestBuilder.New().SetDocument("{ value }").Build()));
            foreach (IExecutionResult result in results)
            {
                await using (result)
                {
                    Assert.AreEqual(0, result.ExpectOperationResult().Errors.Count);
                }
            }

            Assert.AreEqual(2, probe.Requests.Count);
            Assert.AreEqual(2, probe.Requests.Distinct().Count(), "Concurrent executions must not share a request scope.");
            Assert.AreEqual(3, probe.ResolvedFields);
            Assert.IsNull(probe.Current, "A completed execution must not leak its scope into the caller.");
        }

        [TestMethod]
        public async Task HotChocolateBatchHasOneDiagnosticScopePerExecutionAndWaitsForHttpCompletion()
        {
            (DefaultHttpContext context, ResponseCallbacks response) = CreateHttpContext();
            DiagnosticScopeProbe probe = new();
            ServiceCollection services = new();
            AddProbeSchema(services, probe);
            await using ServiceProvider provider = services.BuildServiceProvider();
            IRequestExecutor executor = await provider.GetRequestExecutorAsync();
            using OperationRequestBatch batch = new(new[]
            {
                OperationRequestBuilder.New().SetDocument("{ value }").SetGlobalState(nameof(HttpContext), context).Build(),
                OperationRequestBuilder.New().SetDocument("{ left: value right: value }").SetGlobalState(nameof(HttpContext), context).Build()
            });
            await using IResponseStream result = await executor.ExecuteBatchAsync(batch);
            int results = 0;
            await foreach (OperationResult operation in result.ReadResultsAsync())
            {
                await using (operation)
                {
                    Assert.AreEqual(0, operation.Errors.Count);
                    results++;
                }
            }

            Assert.AreEqual(2, results);
            Assert.AreEqual(2, probe.Requests.Count, "Two batch executions are not one HTTP request measurement.");
            Assert.AreEqual(0, probe.HttpCompletions.Count);
            await response.CompleteAsync();
            Assert.AreEqual(2, probe.HttpCompletions.Count);
            Assert.IsTrue(probe.HttpCompletions.All(outcome => outcome == EngineTelemetryOutcome.Success));
        }

        [DataTestMethod]
        [DataRow(ToolType.BuiltIn, "read_records", true)]
        [DataRow(ToolType.BuiltIn, "create_record", true)]
        [DataRow(ToolType.BuiltIn, "update_record", true)]
        [DataRow(ToolType.BuiltIn, "delete_record", true)]
        [DataRow(ToolType.BuiltIn, "execute_entity", true)]
        [DataRow(ToolType.BuiltIn, "aggregate_records", true)]
        [DataRow(ToolType.BuiltIn, "READ_RECORDS", true)]
        [DataRow(ToolType.BuiltIn, "describe_entities", false)]
        [DataRow(ToolType.BuiltIn, "unknown_tool", false)]
        [DataRow(ToolType.BuiltIn, "tools/list", false)]
        [DataRow(ToolType.BuiltIn, "initialize", false)]
        [DataRow((ToolType)99, "read_records", false)]
        [DataRow(ToolType.Custom, "a-customer-defined-name", true)]
        public void McpProductEligibilityDoesNotReuseTheCustomerUnknownToolFallback(ToolType type, string name, bool eligible)
        {
            Mock<IMcpTool> tool = new();
            tool.SetupGet(t => t.ToolType).Returns(type);
            Assert.AreEqual(eligible, McpTelemetryHelper.IsProductDataTool(tool.Object, name));
        }

        [DataTestMethod]
        [DataRow(ToolType.BuiltIn, "read_records", (int)EngineTelemetryOperation.Read)]
        [DataRow(ToolType.BuiltIn, "aggregate_records", (int)EngineTelemetryOperation.Read)]
        [DataRow(ToolType.BuiltIn, "create_record", (int)EngineTelemetryOperation.Write)]
        [DataRow(ToolType.BuiltIn, "update_record", (int)EngineTelemetryOperation.Write)]
        [DataRow(ToolType.BuiltIn, "delete_record", (int)EngineTelemetryOperation.Write)]
        [DataRow(ToolType.BuiltIn, "execute_entity", (int)EngineTelemetryOperation.Execute)]
        [DataRow(ToolType.Custom, "arbitrary-private-name", (int)EngineTelemetryOperation.Execute)]
        [DataRow(ToolType.BuiltIn, "describe_entities", (int)EngineTelemetryOperation.Unknown)]
        public void McpLogicalOperationsUseOnlyClosedProductCategories(ToolType type, string name, int expected)
        {
            Mock<IMcpTool> tool = new();
            tool.SetupGet(t => t.ToolType).Returns(type);
            Assert.AreEqual((EngineTelemetryOperation)expected, McpTelemetryHelper.ClassifyProductOperation(tool.Object, name));
        }

        [TestMethod]
        public async Task McpStdioWrapperWaitsForWriterAndLeavesCustomerSpanAtToolExecutionBoundary()
        {
            // Initialize the source before registering a callback that observes it. Reading
            // the static field inside ShouldListenTo can re-enter its type initializer.
            ActivitySource dabSource = TelemetryTracesHelper.DABActivitySource;
            using ActivityListener listener = new()
            {
                ShouldListenTo = source => ReferenceEquals(source, dabSource),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
            };
            ActivitySource.AddActivityListener(listener);
            TaskCompletionSource writerEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource writerReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Activity? toolActivity = null;
            CallToolResult expected = new() { Content = new List<ContentBlock>() };
            Mock<IMcpTool> tool = CreateTool(expected);
            tool.Setup(t => t.ExecuteAsync(It.IsAny<JsonDocument?>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    toolActivity = Activity.Current;
                    return Task.FromResult(expected);
                });
            using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
            Task<CallToolResult> executing = McpTelemetryHelper.ExecuteWithTelemetryAsync(
                tool.Object, "read_records", null, provider, CancellationToken.None,
                async result =>
                {
                    Assert.AreSame(expected, result);
                    Assert.IsNotNull(toolActivity);
                    Assert.IsTrue(toolActivity.IsStopped, "A product completion hook must not extend the customer's tool activity.");
                    writerEntered.SetResult();
                    await writerReleased.Task;
                });

            await writerEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(executing.IsCompleted);
            writerReleased.SetResult();
            Assert.AreSame(expected, await executing);
        }

        [TestMethod]
        public async Task McpStdioWriterFailurePropagatesAndDoesNotReturnAServedResult()
        {
            Mock<IMcpTool> tool = CreateTool(new CallToolResult { Content = new List<ContentBlock>() });
            using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
            await Assert.ThrowsExceptionAsync<IOException>(() => McpTelemetryHelper.ExecuteWithTelemetryAsync(
                tool.Object, "read_records", null, provider, CancellationToken.None,
                _ => throw new IOException("synthetic broken response pipe")));
        }

        [TestMethod]
        public async Task StdioServerWritesToolErrorAndDoesNotInvokeToolsForUnknownOrMalformedCalls()
        {
            CallToolResult result = new()
            {
                IsError = true,
                Content = new List<ContentBlock> { new TextContentBlock { Text = "synthetic failure" } }
            };
            Mock<IMcpTool> tool = CreateTool(result);
            McpToolRegistry registry = new();
            registry.RegisterTool(tool.Object);
            using StringWriter output = new();
            using McpStdoutWriter writer = new(output);
            using ServiceProvider provider = new ServiceCollection()
                .AddSingleton(writer)
                .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MCP:StdioMode"] = "true"
                }).Build())
                .BuildServiceProvider();
            using StringReader input = new("""
                {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"read_records","arguments":{}}}
                {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"unknown_tool"}}
                {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{}}
                {"jsonrpc":"2.0","id":4,"method":"ping"}
                """);
            McpStdioServer server = new(registry, provider, input);
            await server.RunAsync(CancellationToken.None);
            tool.Verify(t => t.ExecuteAsync(It.IsAny<JsonDocument?>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()), Times.Once);
            string[] lines = output.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(4, lines.Length);
            using JsonDocument response = JsonDocument.Parse(lines[0]);
            Assert.IsTrue(response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        }

        private static RuntimeConfig CreateConfig() => new(
            Schema: null,
            DataSource: new DataSource(DatabaseType.MSSQL, string.Empty),
            Entities: new RuntimeEntities(new Dictionary<string, Entity>()),
            Runtime: new RuntimeOptions(new RestRuntimeOptions(Path: "/data"), new GraphQLRuntimeOptions(Path: "/data/query"),
                new McpRuntimeOptions(Path: "/data/tools"), Host: null));

        private static void SetController(HttpContext context, Type controller)
        {
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new ControllerActionDescriptor
            {
                ControllerTypeInfo = controller.GetTypeInfo()
            }), "synthetic endpoint"));
        }

        private static (DefaultHttpContext Context, ResponseCallbacks Response) CreateHttpContext(CancellationToken aborted = default)
        {
            ResponseCallbacks response = new();
            DefaultHttpContext context = new() { RequestAborted = aborted };
            context.Features.Set<IHttpResponseFeature>(response);
            return (context, response);
        }

        private static Mock<IMcpTool> CreateTool(CallToolResult result)
        {
            Mock<IMcpTool> tool = new();
            tool.SetupGet(t => t.ToolType).Returns(ToolType.BuiltIn);
            tool.Setup(t => t.GetToolMetadata()).Returns(new Tool { Name = "read_records" });
            tool.Setup(t => t.ExecuteAsync(It.IsAny<JsonDocument?>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);
            return tool;
        }

        private static void AddProbeSchema(IServiceCollection services, DiagnosticScopeProbe probe)
        {
            services.AddGraphQL().AddQueryType(descriptor => descriptor
                .Name("Query")
                .Field("value")
                .Type<StringType>()
                .Resolve(async _ =>
                {
                    object? scope = probe.Current;
                    Assert.IsNotNull(scope);
                    await Task.Yield();
                    Assert.AreSame(scope, probe.Current);
                    await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
                    {
                        Assert.AreSame(scope, probe.Current);
                        await Task.Yield();
                        Assert.AreSame(scope, probe.Current);
                    })));
                    probe.RecordResolvedField();
                    return "synthetic result";
                }))
                .AddDiagnosticEventListener(_ => probe);
        }

        private sealed class ResponseCallbacks : IHttpResponseFeature
        {
            private readonly ConcurrentStack<(Func<object, Task> Callback, object State)> _completed = new();

            public int StatusCode { get; set; } = 200;
            public string? ReasonPhrase { get; set; }
            public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
            public Stream Body { get; set; } = Stream.Null;
            public bool HasStarted => false;

            public void OnStarting(Func<object, Task> callback, object state) { }

            public void OnCompleted(Func<object, Task> callback, object state) => _completed.Push((callback, state));

            internal async Task CompleteAsync()
            {
                // Retain callbacks so tests can deliberately invoke the same completion twice.
                foreach ((Func<object, Task> callback, object state) in _completed)
                {
                    await callback(state);
                }
            }
        }

        private sealed class DiagnosticScopeProbe : ExecutionDiagnosticEventListener
        {
            private readonly AsyncLocal<object?> _current = new();
            private int _resolvedFields;
            internal object? Current => _current.Value;
            internal ConcurrentQueue<object> Requests { get; } = new();
            internal ConcurrentQueue<EngineTelemetryOutcome> HttpCompletions { get; } = new();
            internal int ResolvedFields => Volatile.Read(ref _resolvedFields);

            internal void RecordResolvedField() => Interlocked.Increment(ref _resolvedFields);

            public override IDisposable ExecuteRequest(GraphQLRequestContext context)
            {
                if (context.IsWarmupRequest())
                {
                    return EmptyScope;
                }

                object? previous = _current.Value;
                object scope = new();
                _current.Value = scope;
                Requests.Enqueue(scope);
                return new ProbeScope(() =>
                {
                    if (context.ContextData.TryGetValue(nameof(HttpContext), out object? value) && value is HttpContext httpContext)
                    {
                        EngineTelemetryOutcome outcome = EngineTelemetryGraphQLListener.ClassifyResult(context.Result, EngineTelemetryOutcome.Unknown, canceled: false);
                        _ = new EngineTelemetryHttpCompletion(httpContext, (result, _) => HttpCompletions.Enqueue(result), () => outcome, inferSuccessFromHttp: false);
                    }

                    _current.Value = previous;
                });
            }
        }

        private sealed class ProbeScope(Action onDispose) : IDisposable
        {
            public void Dispose() => onDispose();
        }
    }
}
