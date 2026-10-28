// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.AuthenticationHelpers;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Authentication
{
    [TestClass]
    public class OboCredentialFactoryTests
    {
        private const string CLIENT_ID = "11111111-1111-1111-1111-111111111111";
        private const string TENANT_ID = "22222222-2222-2222-2222-222222222222";
        private const string MANAGED_IDENTITY_CLIENT_ID = "33333333-3333-3333-3333-333333333333";
        private const string CLIENT_SECRET = "  unit-test-only+secret=&  ";
        private const string USER_ASSERTION = "unit-test-only.user.assertion";
        private const string MANAGED_IDENTITY_ASSERTION = "unit-test-only-managed-identity-assertion";
        private const string SQL_ACCESS_TOKEN = "unit-test-only-sql-access-token";
        private const string SQL_SCOPE = "https://database.windows.net/.default";
        private const string TOKEN_EXCHANGE_SCOPE = "api://AzureADTokenExchange/.default";
        private const string AUTHORITY = "https://login.microsoftonline.com/" + TENANT_ID;
        private const string SDK_FAILURE_PAYLOAD = "unit-test-only-private-sdk-response";
        private const string MISSING_CREDENTIALS_ERROR_MESSAGE =
            "User-delegated authentication requires DAB_OBO_CLIENT_ID, DAB_OBO_TENANT_ID, and exactly one of DAB_OBO_CLIENT_SECRET or DAB_OBO_MANAGED_IDENTITY_CLIENT_ID.";

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" \t")]
        public async Task CreateClient_WorkloadIdentityUsesSelectedIdentityAndSeparateOboAssertions(string? clientSecret)
        {
            Dictionary<string, string?> environment = CreateWorkloadIdentityEnvironment();
            environment[UserDelegatedAuthOptions.DAB_OBO_CLIENT_SECRET_ENV_VAR] = clientSecret;
            environment["AZURE_CLIENT_ID"] = "unrelated-startup-identity";
            environment["AZURE_CLIENT_SECRET"] = "unrelated-default-credential-secret";
            environment["AZURE_FEDERATED_TOKEN_FILE"] = "not-an-obo-token-file";

            FakeTokenCredential credential = new((_, _) => ValueTask.FromResult(
                new AccessToken(MANAGED_IDENTITY_ASSERTION, DateTimeOffset.UtcNow.AddHours(1))));
            string? selectedIdentity = null;
            using StubMsalHttpClientFactory httpFactory = new();
            using CancellationTokenSource cancellation = new();

            IConfidentialClientApplication client = OboCredentialFactory.CreateClient(
                NullLogger.Instance,
                name => environment.GetValueOrDefault(name),
                managedIdentityCredentialFactory: clientId =>
                {
                    selectedIdentity = clientId;
                    return credential;
                },
                httpClientFactory: httpFactory);

            Assert.AreEqual(MANAGED_IDENTITY_CLIENT_ID, selectedIdentity);
            Assert.AreEqual(0, credential.Requests.Count, "Client construction must not acquire an assertion.");
            Assert.AreEqual(0, httpFactory.RequestCount);
            Assert.AreEqual(AUTHORITY, client.Authority.TrimEnd('/'));

            MsalClientWrapper wrapper = new(client);
            AuthenticationResult result = await wrapper.AcquireTokenOnBehalfOfAsync(
                [SQL_SCOPE], USER_ASSERTION, cancellation.Token);

            Assert.AreEqual(SQL_ACCESS_TOKEN, result.AccessToken);
            Assert.AreEqual(1, credential.Requests.Count);
            CollectionAssert.AreEqual(new[] { TOKEN_EXCHANGE_SCOPE }, credential.Requests[0].Scopes);
            Assert.AreEqual(cancellation.Token, credential.CancellationTokens[0]);
            Assert.AreEqual(1, httpFactory.TokenRequests.Count);

            IReadOnlyDictionary<string, string> request = httpFactory.TokenRequests[0];
            AssertOboRequest(request);
            Assert.AreEqual(MANAGED_IDENTITY_ASSERTION, request["client_assertion"]);
            Assert.AreEqual("urn:ietf:params:oauth:client-assertion-type:jwt-bearer", request["client_assertion_type"]);
            Assert.IsFalse(request.ContainsKey("client_secret"));
        }

        [TestMethod]
        public async Task CreateClient_WorkloadIdentityReusesCredentialAndRefreshesAssertion()
        {
            Dictionary<string, string?> environment = CreateWorkloadIdentityEnvironment();
            int credentialInstances = 0;
            int assertionsIssued = 0;
            FakeTokenCredential credential = new((_, _) => ValueTask.FromResult(
                new AccessToken($"{MANAGED_IDENTITY_ASSERTION}-{++assertionsIssued}", DateTimeOffset.UtcNow.AddHours(1))));
            using StubMsalHttpClientFactory httpFactory = new();

            IConfidentialClientApplication client = OboCredentialFactory.CreateClient(
                NullLogger.Instance,
                name => environment.GetValueOrDefault(name),
                managedIdentityCredentialFactory: _ =>
                {
                    credentialInstances++;
                    return credential;
                },
                httpClientFactory: httpFactory);

            await client.AcquireTokenOnBehalfOf([SQL_SCOPE], new UserAssertion(USER_ASSERTION)).ExecuteAsync();

            // Force a fresh OBO acquisition rather than reading MSAL's SQL access-token cache.
            await client.AcquireTokenOnBehalfOf([SQL_SCOPE], new UserAssertion(USER_ASSERTION))
                .WithForceRefresh(true)
                .ExecuteAsync();

            Assert.AreEqual(1, credentialInstances);
            Assert.AreEqual(2, credential.Requests.Count);
            Assert.AreEqual(2, httpFactory.TokenRequests.Count);
            Assert.AreEqual($"{MANAGED_IDENTITY_ASSERTION}-1", httpFactory.TokenRequests[0]["client_assertion"]);
            Assert.AreEqual($"{MANAGED_IDENTITY_ASSERTION}-2", httpFactory.TokenRequests[1]["client_assertion"]);

            foreach (TokenRequestContext request in credential.Requests)
            {
                CollectionAssert.AreEqual(new[] { TOKEN_EXCHANGE_SCOPE }, request.Scopes);
            }

            foreach (IReadOnlyDictionary<string, string> request in httpFactory.TokenRequests)
            {
                AssertOboRequest(request);
                Assert.IsFalse(request.ContainsKey("client_secret"));
            }
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" \t")]
        public async Task CreateClient_ClientSecretPreservesLegacyBehaviorAndSecretBytes(string? managedIdentityClientId)
        {
            Dictionary<string, string?> environment = new()
            {
                [UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR] = CLIENT_ID,
                [UserDelegatedAuthOptions.DAB_OBO_TENANT_ID_ENV_VAR] = TENANT_ID,
                [UserDelegatedAuthOptions.DAB_OBO_CLIENT_SECRET_ENV_VAR] = CLIENT_SECRET,
                [UserDelegatedAuthOptions.DAB_OBO_MANAGED_IDENTITY_CLIENT_ID_ENV_VAR] = managedIdentityClientId,
                ["AZURE_CLIENT_ID"] = "unrelated-startup-identity"
            };
            using StubMsalHttpClientFactory httpFactory = new();

            IConfidentialClientApplication client = OboCredentialFactory.CreateClient(
                NullLogger.Instance,
                name => environment.GetValueOrDefault(name),
                managedIdentityCredentialFactory: _ => throw new AssertFailedException("ClientSecret must not create a managed identity credential."),
                httpClientFactory: httpFactory);

            MsalClientWrapper wrapper = new(client);
            AuthenticationResult result = await wrapper.AcquireTokenOnBehalfOfAsync([SQL_SCOPE], USER_ASSERTION);

            Assert.AreEqual(SQL_ACCESS_TOKEN, result.AccessToken);
            Assert.AreEqual(1, httpFactory.TokenRequests.Count);
            IReadOnlyDictionary<string, string> request = httpFactory.TokenRequests[0];
            AssertOboRequest(request);
            Assert.AreEqual(CLIENT_SECRET, request["client_secret"], "Nonblank secret bytes must not be trimmed.");
            Assert.IsFalse(request.ContainsKey("client_assertion"));
            Assert.IsFalse(request.ContainsKey("client_assertion_type"));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task CreateClient_ManagedIdentityFailureIsSanitizedWithoutFallback(bool unavailable)
        {
            Dictionary<string, string?> environment = CreateWorkloadIdentityEnvironment();
            environment["AZURE_CLIENT_SECRET"] = CLIENT_SECRET;
            string sdkMessage = $"{SDK_FAILURE_PAYLOAD} {CLIENT_SECRET} {USER_ASSERTION} {MANAGED_IDENTITY_ASSERTION}";
            Exception sdkException = unavailable
                ? new CredentialUnavailableException(sdkMessage, new InvalidOperationException(sdkMessage))
                : new AuthenticationFailedException(sdkMessage, new InvalidOperationException(sdkMessage));
            FakeTokenCredential credential = new((_, _) => ValueTask.FromException<AccessToken>(sdkException));
            Mock<ILogger> logger = new();
            using StubMsalHttpClientFactory httpFactory = new();

            IConfidentialClientApplication client = OboCredentialFactory.CreateClient(
                logger.Object,
                name => environment.GetValueOrDefault(name),
                _ => credential,
                httpFactory);

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() =>
                client.AcquireTokenOnBehalfOf([SQL_SCOPE], new UserAssertion(USER_ASSERTION)).ExecuteAsync());

            AssertAuthenticationFailure(exception, logger);
            Assert.AreEqual(1, credential.Requests.Count);
            Assert.AreEqual(0, httpFactory.TokenRequests.Count, "A failed assertion must not trigger a fallback OBO request.");
        }

        [DataTestMethod]
        [DataRow(null, 3600)]
        [DataRow("", 3600)]
        [DataRow(" \t", 3600)]
        [DataRow(MANAGED_IDENTITY_ASSERTION, -60)]
        public async Task CreateClient_UnusableManagedIdentityTokenIsSanitized(string? token, int expiresInSeconds)
        {
            Dictionary<string, string?> environment = CreateWorkloadIdentityEnvironment();
            AccessToken assertion = token is null
                ? default
                : new AccessToken(token, DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds));
            FakeTokenCredential credential = new((_, _) => ValueTask.FromResult(assertion));
            Mock<ILogger> logger = new();
            using StubMsalHttpClientFactory httpFactory = new();

            IConfidentialClientApplication client = OboCredentialFactory.CreateClient(
                logger.Object,
                name => environment.GetValueOrDefault(name),
                _ => credential,
                httpFactory);

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() =>
                client.AcquireTokenOnBehalfOf([SQL_SCOPE], new UserAssertion(USER_ASSERTION)).ExecuteAsync());

            AssertAuthenticationFailure(exception, logger);
            Assert.AreEqual(1, credential.Requests.Count);
            Assert.AreEqual(0, httpFactory.TokenRequests.Count);
        }

        [DataTestMethod]
        [DataRow("OperationCanceled")]
        [DataRow("AuthenticationFailed")]
        [DataRow("CredentialUnavailable")]
        public async Task CreateClient_AssertionCallbackPreservesCancellation(string failure)
        {
            Dictionary<string, string?> environment = CreateWorkloadIdentityEnvironment();
            using CancellationTokenSource cancellation = new();
            FakeTokenCredential credential = new((_, token) =>
            {
                Assert.AreEqual(cancellation.Token, token);
                cancellation.Cancel();
                Exception exception = failure switch
                {
                    "AuthenticationFailed" => new AuthenticationFailedException(SDK_FAILURE_PAYLOAD),
                    "CredentialUnavailable" => new CredentialUnavailableException(SDK_FAILURE_PAYLOAD),
                    _ => new OperationCanceledException(token)
                };

                return ValueTask.FromException<AccessToken>(exception);
            });
            Mock<ILogger> logger = new();
            using StubMsalHttpClientFactory httpFactory = new();
            IConfidentialClientApplication client = OboCredentialFactory.CreateClient(
                logger.Object,
                name => environment.GetValueOrDefault(name),
                _ => credential,
                httpFactory);

            await AssertCancellationAsync(
                client.AcquireTokenOnBehalfOf([SQL_SCOPE], new UserAssertion(USER_ASSERTION)).ExecuteAsync(cancellation.Token),
                cancellation.Token);

            Assert.AreEqual(1, credential.Requests.Count);
            Assert.AreEqual(0, httpFactory.TokenRequests.Count);
            logger.VerifyNoOtherCalls();
        }

        [TestMethod]
        public async Task CreateClient_AlreadyCanceledRequestDoesNotAcquireAssertion()
        {
            Dictionary<string, string?> environment = CreateWorkloadIdentityEnvironment();
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            FakeTokenCredential credential = new((_, _) => throw new AssertFailedException("A canceled request must not acquire an assertion."));
            Mock<ILogger> logger = new();
            using StubMsalHttpClientFactory httpFactory = new();
            IConfidentialClientApplication client = OboCredentialFactory.CreateClient(
                logger.Object,
                name => environment.GetValueOrDefault(name),
                _ => credential,
                httpFactory);

            await AssertCancellationAsync(
                client.AcquireTokenOnBehalfOf([SQL_SCOPE], new UserAssertion(USER_ASSERTION)).ExecuteAsync(cancellation.Token),
                cancellation.Token);

            Assert.AreEqual(0, credential.Requests.Count);
            Assert.AreEqual(0, httpFactory.TokenRequests.Count);
            logger.VerifyNoOtherCalls();
        }

        [DataTestMethod]
        [DataRow(CLIENT_SECRET, null)]
        [DataRow(null, MANAGED_IDENTITY_CLIENT_ID)]
        public void ValidateAndCreateClient_ReadOnlyCredentialEnvironmentVariables(
            string? clientSecret,
            string? managedIdentityClientId)
        {
            Dictionary<string, string?> environment = new()
            {
                [UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR] = CLIENT_ID,
                [UserDelegatedAuthOptions.DAB_OBO_TENANT_ID_ENV_VAR] = TENANT_ID,
                [UserDelegatedAuthOptions.DAB_OBO_CLIENT_SECRET_ENV_VAR] = clientSecret,
                [UserDelegatedAuthOptions.DAB_OBO_MANAGED_IDENTITY_CLIENT_ID_ENV_VAR] = managedIdentityClientId
            };
            FakeTokenCredential credential = new((_, _) =>
                throw new AssertFailedException("Configuration and client creation must not acquire tokens."));

            string? ReadEnvironmentVariable(string name)
            {
                Assert.IsTrue(environment.ContainsKey(name), $"Unexpected environment variable: {name}");
                return environment[name];
            }

            OboCredentialFactory.ValidateEnvironmentVariables(ReadEnvironmentVariable);
            IConfidentialClientApplication client = OboCredentialFactory.CreateClient(
                NullLogger.Instance, ReadEnvironmentVariable, _ => credential);

            Assert.AreEqual(CLIENT_ID, client.AppConfig.ClientId);
            Assert.AreEqual(0, credential.Requests.Count);
        }

        [DataTestMethod]
        [DataRow(CLIENT_ID, TENANT_ID)]
        [DataRow(null, null)]
        [DataRow(" \t", " \t")]
        public void ValidateAndCreateClient_RejectConflictingCredentials(string? clientId, string? tenantId)
        {
            Dictionary<string, string?> environment = CreateWorkloadIdentityEnvironment();
            environment[UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR] = clientId;
            environment[UserDelegatedAuthOptions.DAB_OBO_TENANT_ID_ENV_VAR] = tenantId;
            environment[UserDelegatedAuthOptions.DAB_OBO_CLIENT_SECRET_ENV_VAR] = CLIENT_SECRET;

            AssertConfigurationFailure(environment,
                "Configure exactly one OBO credential method. DAB_OBO_CLIENT_SECRET and DAB_OBO_MANAGED_IDENTITY_CLIENT_ID cannot both be set.");
        }

        [DataTestMethod]
        [DataRow(null, null)]
        [DataRow(null, "")]
        [DataRow(null, " \t")]
        [DataRow("", null)]
        [DataRow("", "")]
        [DataRow("", " \t")]
        [DataRow(" \t", null)]
        [DataRow(" \t", "")]
        [DataRow(" \t", " \t")]
        public void ValidateAndCreateClient_RejectNeitherCredential(string? clientSecret, string? managedIdentityClientId)
        {
            Dictionary<string, string?> environment = new()
            {
                [UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR] = CLIENT_ID,
                [UserDelegatedAuthOptions.DAB_OBO_TENANT_ID_ENV_VAR] = TENANT_ID,
                [UserDelegatedAuthOptions.DAB_OBO_CLIENT_SECRET_ENV_VAR] = clientSecret,
                [UserDelegatedAuthOptions.DAB_OBO_MANAGED_IDENTITY_CLIENT_ID_ENV_VAR] = managedIdentityClientId,
                ["AZURE_CLIENT_ID"] = "unrelated-startup-identity",
                ["AZURE_CLIENT_SECRET"] = CLIENT_SECRET
            };

            AssertConfigurationFailure(environment, MISSING_CREDENTIALS_ERROR_MESSAGE);
        }

        [DataTestMethod]
        [DataRow(CLIENT_SECRET, null, UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR)]
        [DataRow(CLIENT_SECRET, null, UserDelegatedAuthOptions.DAB_OBO_TENANT_ID_ENV_VAR)]
        [DataRow(null, MANAGED_IDENTITY_CLIENT_ID, UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR)]
        [DataRow(null, MANAGED_IDENTITY_CLIENT_ID, UserDelegatedAuthOptions.DAB_OBO_TENANT_ID_ENV_VAR)]
        public void ValidateAndCreateClient_RequireCommonIdsForEitherCredential(
            string? clientSecret,
            string? managedIdentityClientId,
            string variableName)
        {
            Dictionary<string, string?> environment = new()
            {
                [UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR] = CLIENT_ID,
                [UserDelegatedAuthOptions.DAB_OBO_TENANT_ID_ENV_VAR] = TENANT_ID,
                [UserDelegatedAuthOptions.DAB_OBO_CLIENT_SECRET_ENV_VAR] = clientSecret,
                [UserDelegatedAuthOptions.DAB_OBO_MANAGED_IDENTITY_CLIENT_ID_ENV_VAR] = managedIdentityClientId
            };

            foreach (string? missingValue in new[] { null, "", " \t" })
            {
                environment[variableName] = missingValue;
                AssertConfigurationFailure(environment, MISSING_CREDENTIALS_ERROR_MESSAGE);
            }
        }

        private static Dictionary<string, string?> CreateWorkloadIdentityEnvironment()
        {
            return new Dictionary<string, string?>
            {
                [UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR] = CLIENT_ID,
                [UserDelegatedAuthOptions.DAB_OBO_TENANT_ID_ENV_VAR] = TENANT_ID,
                [UserDelegatedAuthOptions.DAB_OBO_MANAGED_IDENTITY_CLIENT_ID_ENV_VAR] = MANAGED_IDENTITY_CLIENT_ID
            };
        }

        private static void AssertOboRequest(IReadOnlyDictionary<string, string> request)
        {
            Assert.AreEqual(CLIENT_ID, request["client_id"]);
            Assert.AreEqual(USER_ASSERTION, request["assertion"]);
            Assert.AreEqual("on_behalf_of", request["requested_token_use"]);
            Assert.AreEqual("urn:ietf:params:oauth:grant-type:jwt-bearer", request["grant_type"]);

            // MSAL can add OpenID Connect protocol scopes; the sole resource scope must remain SQL.
            string[] resourceScopes = request["scope"].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Except(["openid", "profile", "offline_access"])
                .ToArray();
            CollectionAssert.AreEquivalent(new[] { SQL_SCOPE }, resourceScopes);
        }

        private static void AssertAuthenticationFailure(DataApiBuilderException exception, Mock<ILogger> logger)
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.OboAuthenticationFailure, exception.SubStatusCode);
            Assert.AreEqual(DataApiBuilderException.OBO_TOKEN_ACQUISITION_FAILED, exception.Message);
            Assert.IsNull(exception.InnerException);
            AssertNoCredentialDisclosure(exception.ToString());

            IInvocation[] logCalls = logger.Invocations.Where(call => call.Method.Name == nameof(ILogger.Log)).ToArray();
            Assert.AreEqual(1, logCalls.Length);
            Assert.AreEqual(Microsoft.Extensions.Logging.LogLevel.Error, logCalls[0].Arguments[0]);
            Assert.IsNull(logCalls[0].Arguments[3], "The SDK exception must not be passed to the logger.");
            AssertNoCredentialDisclosure(logCalls[0].Arguments[2].ToString()!);
        }

        private static void AssertConfigurationFailure(Dictionary<string, string?> environment, string expectedMessage)
        {
            DataApiBuilderException validationException = Assert.ThrowsException<DataApiBuilderException>(() =>
                OboCredentialFactory.ValidateEnvironmentVariables(name => environment.GetValueOrDefault(name)));
            DataApiBuilderException creationException = Assert.ThrowsException<DataApiBuilderException>(() =>
                OboCredentialFactory.CreateClient(
                    NullLogger.Instance,
                    name => environment.GetValueOrDefault(name),
                    managedIdentityCredentialFactory: _ => throw new AssertFailedException("Invalid configuration must not create a credential.")));

            foreach (DataApiBuilderException exception in new[] { validationException, creationException })
            {
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, exception.SubStatusCode);
                Assert.AreEqual(expectedMessage, exception.Message);
                Assert.IsNull(exception.InnerException);
                AssertNoCredentialDisclosure(exception.ToString());
            }
        }

        private static void AssertNoCredentialDisclosure(string text)
        {
            foreach (string value in new[] { CLIENT_ID, TENANT_ID, MANAGED_IDENTITY_CLIENT_ID, CLIENT_SECRET, USER_ASSERTION, MANAGED_IDENTITY_ASSERTION, SDK_FAILURE_PAYLOAD })
            {
                Assert.IsFalse(text.Contains(value, StringComparison.Ordinal), "Credential values and SDK payloads must not be disclosed.");
            }
        }

        private static async Task AssertCancellationAsync(Task acquisition, CancellationToken cancellationToken)
        {
            try
            {
                await acquisition;
                Assert.Fail("Token acquisition should propagate cancellation.");
            }
            catch (OperationCanceledException exception)
            {
                Assert.AreEqual(cancellationToken, exception.CancellationToken);
            }
        }

        private sealed class FakeTokenCredential : TokenCredential
        {
            private readonly Func<TokenRequestContext, CancellationToken, ValueTask<AccessToken>> _getToken;

            public List<TokenRequestContext> Requests { get; } = [];
            public List<CancellationToken> CancellationTokens { get; } = [];

            public FakeTokenCredential(Func<TokenRequestContext, CancellationToken, ValueTask<AccessToken>> getToken)
            {
                _getToken = getToken;
            }

            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                throw new AssertFailedException("Managed identity assertions must be acquired asynchronously.");
            }

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                Requests.Add(requestContext);
                CancellationTokens.Add(cancellationToken);
                return _getToken(requestContext, cancellationToken);
            }
        }

        private sealed class StubMsalHttpClientFactory : HttpMessageHandler, IMsalHttpClientFactory
        {
            private readonly HttpClient _httpClient;

            public List<IReadOnlyDictionary<string, string>> TokenRequests { get; } = [];
            public int RequestCount { get; private set; }

            public StubMsalHttpClientFactory()
            {
                _httpClient = new HttpClient(this, disposeHandler: false);
            }

            public HttpClient GetHttpClient()
            {
                return _httpClient;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequestCount++;

                Uri uri = request.RequestUri!;
                if (request.Method == HttpMethod.Get
                    && uri.GetLeftPart(UriPartial.Path) == "https://login.microsoftonline.com/common/discovery/instance")
                {
                    return JsonResponse(new
                    {
                        tenant_discovery_endpoint = $"{AUTHORITY}/v2.0/.well-known/openid-configuration",
                        metadata = new[]
                        {
                            new
                            {
                                preferred_network = "login.microsoftonline.com",
                                preferred_cache = "login.microsoftonline.com",
                                aliases = new[] { "login.microsoftonline.com", "login.windows.net", "sts.windows.net" }
                            }
                        }
                    });
                }

                if (request.Method == HttpMethod.Get
                    && uri.GetLeftPart(UriPartial.Path) == $"{AUTHORITY}/v2.0/.well-known/openid-configuration")
                {
                    return JsonResponse(new
                    {
                        authorization_endpoint = $"{AUTHORITY}/oauth2/v2.0/authorize",
                        token_endpoint = $"{AUTHORITY}/oauth2/v2.0/token",
                        issuer = $"{AUTHORITY}/v2.0"
                    });
                }

                if (request.Method == HttpMethod.Post
                    && uri.GetLeftPart(UriPartial.Path) == $"{AUTHORITY}/oauth2/v2.0/token")
                {
                    string body = await request.Content!.ReadAsStringAsync(cancellationToken);
                    Dictionary<string, string> parameters = body.Split('&')
                        .Select(parameter => parameter.Split('=', 2))
                        .ToDictionary(parameter => WebUtility.UrlDecode(parameter[0]), parameter => WebUtility.UrlDecode(parameter[1]));
                    TokenRequests.Add(parameters);

                    string clientInfo = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                    {
                        uid = "44444444-4444-4444-4444-444444444444",
                        utid = TENANT_ID
                    }))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

                    return JsonResponse(new
                    {
                        token_type = "Bearer",
                        access_token = SQL_ACCESS_TOKEN,
                        expires_in = 3600,
                        scope = SQL_SCOPE,
                        client_info = clientInfo
                    });
                }

                throw new AssertFailedException($"Unexpected MSAL HTTP request: {request.Method} {uri.GetLeftPart(UriPartial.Path)}");
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _httpClient.Dispose();
                }

                base.Dispose(disposing);
            }

            private static HttpResponseMessage JsonResponse(object response)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json")
                };
            }
        }
    }
}
