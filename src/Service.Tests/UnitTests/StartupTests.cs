// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using StackExchange.Redis;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    [DoNotParallelize]
    public class StartupTests
    {
        private const string OBO_TEST_CLIENT_ID = "11111111-1111-1111-1111-111111111111";
        private const string OBO_TEST_TENANT_ID = "22222222-2222-2222-2222-222222222222";
        private const string OBO_TEST_MANAGED_IDENTITY_CLIENT_ID = "33333333-3333-3333-3333-333333333333";

        [DataTestMethod]
        [DataRow("test-client-secret", null, DisplayName = "Existing secret configuration")]
        [DataRow("test-client-secret", "", DisplayName = "Secret with empty identity")]
        [DataRow("test-client-secret", " \t", DisplayName = "Secret with whitespace identity")]
        [DataRow(null, OBO_TEST_MANAGED_IDENTITY_CLIENT_ID, DisplayName = "Federation without a client secret")]
        [DataRow("", OBO_TEST_MANAGED_IDENTITY_CLIENT_ID, DisplayName = "Federation with empty secret")]
        [DataRow(" \t", OBO_TEST_MANAGED_IDENTITY_CLIENT_ID, DisplayName = "Federation with whitespace secret")]
        public void RegisterOboServices_ValidCredentials_RegistersSingletons(
            string? clientSecret,
            string? managedIdentityClientId)
        {
            WithOboEnvironment(clientSecret, managedIdentityClientId, () =>
            {
                ServiceCollection services = new();
                services.AddLogging();
                services.AddFusionCache();

                Startup.RegisterOboServices(services, CreateOboRuntimeConfig(enabled: true));
                using ServiceProvider provider = services.BuildServiceProvider();

                IMsalClientWrapper msalClient = provider.GetRequiredService<IMsalClientWrapper>();
                IOboTokenProvider tokenProvider = provider.GetRequiredService<IOboTokenProvider>();
                Assert.IsInstanceOfType<MsalClientWrapper>(msalClient);
                Assert.IsInstanceOfType<OboSqlTokenProvider>(tokenProvider);
                Assert.AreSame(msalClient, provider.GetRequiredService<IMsalClientWrapper>());
                Assert.AreSame(tokenProvider, provider.GetRequiredService<IOboTokenProvider>());
            });
        }

        [DataTestMethod]
        [DataRow(null, null, DisplayName = "Neither credential")]
        [DataRow(null, "", DisplayName = "Missing secret and empty identity")]
        [DataRow(null, " \t", DisplayName = "Missing secret and whitespace identity")]
        [DataRow("", null, DisplayName = "Empty secret and missing identity")]
        [DataRow("", "", DisplayName = "Both credentials empty")]
        [DataRow("", " \t", DisplayName = "Empty secret and whitespace identity")]
        [DataRow(" \t", null, DisplayName = "Whitespace secret and missing identity")]
        [DataRow(" \t", "", DisplayName = "Whitespace secret and empty identity")]
        [DataRow(" \t", " \t", DisplayName = "Both credentials whitespace")]
        [DataRow("test-client-secret", OBO_TEST_MANAGED_IDENTITY_CLIENT_ID, DisplayName = "Conflicting credentials")]
        public void RegisterOboServices_InvalidCredentials_FailsInsteadOfDisablingObo(
            string? clientSecret,
            string? managedIdentityClientId)
        {
            WithOboEnvironment(clientSecret, managedIdentityClientId, () =>
            {
                ServiceCollection services = new();
                DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                    () => Startup.RegisterOboServices(services, CreateOboRuntimeConfig(enabled: true)));

                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, exception.SubStatusCode);
                Assert.IsFalse(exception.Message.Contains("test-client-secret", StringComparison.Ordinal));
                Assert.AreEqual(0, services.Count);
            });
        }

        [DataTestMethod]
        [DataRow("test-client-secret", null, UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR)]
        [DataRow("test-client-secret", null, UserDelegatedAuthOptions.DAB_OBO_TENANT_ID_ENV_VAR)]
        [DataRow(null, OBO_TEST_MANAGED_IDENTITY_CLIENT_ID, UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR)]
        [DataRow(null, OBO_TEST_MANAGED_IDENTITY_CLIENT_ID, UserDelegatedAuthOptions.DAB_OBO_TENANT_ID_ENV_VAR)]
        public void RegisterOboServices_MissingCommonCredentials_FailsStartup(
            string? clientSecret,
            string? managedIdentityClientId,
            string variableName)
        {
            WithOboEnvironment(clientSecret, managedIdentityClientId, () =>
            {
                foreach (string? missingValue in new[] { null, "", " \t" })
                {
                    Environment.SetEnvironmentVariable(variableName, missingValue);
                    ServiceCollection services = new();
                    DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                        () => Startup.RegisterOboServices(services, CreateOboRuntimeConfig(enabled: true)));

                    Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, exception.SubStatusCode);
                    StringAssert.Contains(exception.Message, variableName);
                    Assert.AreEqual(0, services.Count);
                }
            });
        }

        [DataTestMethod]
        [DataRow(null, null)]
        [DataRow(" \t", " \t")]
        [DataRow("test-client-secret", OBO_TEST_MANAGED_IDENTITY_CLIENT_ID)]
        public void RegisterOboServices_OboDisabled_IgnoresCredentialSettings(
            string? clientSecret,
            string? managedIdentityClientId)
        {
            WithOboEnvironment(clientSecret, managedIdentityClientId, () =>
            {
                ServiceCollection services = new();
                Startup.RegisterOboServices(services, runtimeConfig: null);
                Startup.RegisterOboServices(services, CreateOboRuntimeConfig(enabled: null));
                Startup.RegisterOboServices(services, CreateOboRuntimeConfig(enabled: false));

                Assert.AreEqual(0, services.Count);
            });
        }

        [TestMethod]
        public void RegisterOboServices_EnabledSecondaryDataSource_RegistersProvider()
        {
            WithOboEnvironment(null, OBO_TEST_MANAGED_IDENTITY_CLIENT_ID, () =>
            {
                RuntimeConfig defaultConfig = CreateOboRuntimeConfig(enabled: false);
                RuntimeConfig delegatedConfig = CreateOboRuntimeConfig(enabled: true);
                RuntimeConfig runtimeConfig = new(
                    Schema: defaultConfig.Schema,
                    DataSource: defaultConfig.DataSource,
                    Runtime: defaultConfig.Runtime,
                    Entities: defaultConfig.Entities,
                    DefaultDataSourceName: "default",
                    DataSourceNameToDataSource: new()
                    {
                        ["default"] = defaultConfig.DataSource,
                        ["delegated"] = delegatedConfig.DataSource
                    },
                    EntityNameToDataSourceName: new());
                ServiceCollection services = new();
                services.AddLogging();
                services.AddFusionCache();
                Startup.RegisterOboServices(services, runtimeConfig);
                using ServiceProvider provider = services.BuildServiceProvider();

                Assert.IsNotNull(provider.GetRequiredService<IOboTokenProvider>());
            });
        }

        private static RuntimeConfig CreateOboRuntimeConfig(bool? enabled)
        {
            DataSource dataSource = new(DatabaseType.MSSQL, "Server=localhost;Database=test;", Options: null)
            {
                UserDelegatedAuth = enabled.HasValue
                    ? new UserDelegatedAuthOptions(
                        Enabled: enabled.Value,
                        Provider: "EntraId",
                        DatabaseAudience: "https://database.windows.net")
                    : null
            };
            return new RuntimeConfig(
                Schema: "UnitTestSchema",
                DataSource: dataSource,
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(Cors: null, Authentication: null),
                    Cache: new(Enabled: false)),
                Entities: new(new Dictionary<string, Entity>()));
        }

        private static void WithOboEnvironment(
            string? clientSecret,
            string? managedIdentityClientId,
            Action action)
        {
            Dictionary<string, string?> settings = new()
            {
                [UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR] = OBO_TEST_CLIENT_ID,
                [UserDelegatedAuthOptions.DAB_OBO_TENANT_ID_ENV_VAR] = OBO_TEST_TENANT_ID,
                [UserDelegatedAuthOptions.DAB_OBO_CLIENT_SECRET_ENV_VAR] = clientSecret,
                [UserDelegatedAuthOptions.DAB_OBO_MANAGED_IDENTITY_CLIENT_ID_ENV_VAR] = managedIdentityClientId
            };
            Dictionary<string, string?> originalValues = new();
            foreach (string name in settings.Keys)
            {
                originalValues.Add(name, Environment.GetEnvironmentVariable(name));
            }

            try
            {
                foreach ((string name, string? value) in settings)
                {
                    Environment.SetEnvironmentVariable(name, value);
                }

                action();
            }
            finally
            {
                foreach ((string name, string? value) in originalValues)
                {
                    Environment.SetEnvironmentVariable(name, value);
                }
            }
        }

        [DataTestMethod]
        [DataRow("localhost:6379", false, DisplayName = "Localhost endpoint without password should NOT use Entra auth.")]
        [DataRow("127.0.0.1:6379", false, DisplayName = "IPv4 loopback without password should NOT use Entra auth.")]
        [DataRow("[::1]:6379", false, DisplayName = "IPv6 loopback without password should NOT use Entra auth.")]
        [DataRow("redis.example.com:6380", true, DisplayName = "Remote endpoint without password SHOULD use Entra auth.")]
        [DataRow("redis.example.com:6380,password=secret", false, DisplayName = "Presence of password should NOT use Entra auth, even for remote endpoints.")]
        [DataRow("localhost:6379,redis.example.com:6380", true, DisplayName = "Mixed endpoints (including remote) without password SHOULD use Entra auth.")]
        [DataRow("localhost:6379,password=secret", false, DisplayName = "Localhost with password should NOT use Entra auth.")]
        public void ShouldUseEntraAuthForRedis(string connectionString, bool expectedUseEntraAuth)
        {
            // Arrange
            var options = ConfigurationOptions.Parse(connectionString);

            // Act
            bool result = Startup.ShouldUseEntraAuthForRedis(options);

            // Assert
            Assert.AreEqual(expectedUseEntraAuth, result);
        }

        // ---------------------------------------------------------------------
        // Tests for Startup.ConnectWithRetryAsync (embeddings L2 Redis connect
        // retry/backoff helper). Exercised via an injected connection factory
        // delegate so these tests do not require a real Redis instance.
        // ---------------------------------------------------------------------

        /// <summary>
        /// Captures the no-wait delay function used to avoid real backoff sleeps in unit tests
        /// while still recording the requested delay values for assertion.
        /// </summary>
        private sealed class RecordingDelay
        {
            public List<TimeSpan> Delays { get; } = new();

            public Task DelayAsync(TimeSpan delay)
            {
                Delays.Add(delay);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// First attempt succeeds: factory invoked once, no delays, returned multiplexer is the one
        /// produced by the factory.
        /// </summary>
        [TestMethod]
        public async Task ConnectWithRetryAsync_SucceedsOnFirstAttempt_DoesNotRetryOrDelay()
        {
            Mock<IConnectionMultiplexer> expected = new();
            int callCount = 0;
            RecordingDelay delays = new();

            Task<IConnectionMultiplexer> Factory()
            {
                callCount++;
                return Task.FromResult(expected.Object);
            }

            IConnectionMultiplexer result = await Startup.ConnectWithRetryAsync(
                Factory,
                maxAttempts: 3,
                initialDelay: TimeSpan.FromMilliseconds(500),
                logger: null,
                delayAsync: delays.DelayAsync);

            Assert.AreSame(expected.Object, result);
            Assert.AreEqual(1, callCount, "Factory should be invoked exactly once on first-attempt success.");
            Assert.AreEqual(0, delays.Delays.Count, "No delays should be incurred on first-attempt success.");
        }

        /// <summary>
        /// Transient failure on the first attempt and success on the second: factory is invoked
        /// twice, exactly one backoff delay is applied, and that delay matches initialDelay.
        /// </summary>
        [TestMethod]
        public async Task ConnectWithRetryAsync_TransientFailureThenSuccess_RetriesAndSucceeds()
        {
            Mock<IConnectionMultiplexer> expected = new();
            int callCount = 0;
            RecordingDelay delays = new();
            TimeSpan initialDelay = TimeSpan.FromMilliseconds(500);

            Task<IConnectionMultiplexer> Factory()
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "transient");
                }

                return Task.FromResult(expected.Object);
            }

            IConnectionMultiplexer result = await Startup.ConnectWithRetryAsync(
                Factory,
                maxAttempts: 3,
                initialDelay: initialDelay,
                logger: null,
                delayAsync: delays.DelayAsync);

            Assert.AreSame(expected.Object, result);
            Assert.AreEqual(2, callCount, "Factory should be invoked twice (one failure + one success).");
            Assert.AreEqual(1, delays.Delays.Count, "Exactly one backoff delay should be applied before the second attempt.");
            Assert.AreEqual(initialDelay, delays.Delays[0], "First backoff should equal the initial delay.");
        }

        /// <summary>
        /// All attempts fail: factory is invoked maxAttempts times, the final exception is re-thrown,
        /// and the delay sequence follows exponential backoff (no delay is applied after the last attempt).
        /// </summary>
        [TestMethod]
        public async Task ConnectWithRetryAsync_AllAttemptsFail_RethrowsLastExceptionWithExponentialBackoff()
        {
            int callCount = 0;
            RecordingDelay delays = new();
            TimeSpan initialDelay = TimeSpan.FromMilliseconds(100);

            Task<IConnectionMultiplexer> Factory()
            {
                callCount++;
                throw new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect,
                    $"failure-{callCount}");
            }

            RedisConnectionException ex = await Assert.ThrowsExceptionAsync<RedisConnectionException>(
                () => Startup.ConnectWithRetryAsync(
                    Factory,
                    maxAttempts: 3,
                    initialDelay: initialDelay,
                    logger: null,
                    delayAsync: delays.DelayAsync));

            Assert.AreEqual("failure-3", ex.Message, "Last exception (from final attempt) should be re-thrown.");
            Assert.AreEqual(3, callCount, "Factory should be invoked maxAttempts times.");

            // With 3 attempts, exactly 2 delays should be applied (between attempts 1->2 and 2->3),
            // doubling each time: initialDelay, initialDelay * 2.
            Assert.AreEqual(2, delays.Delays.Count, "Should apply (maxAttempts - 1) delays.");
            Assert.AreEqual(initialDelay, delays.Delays[0], "First delay should equal initialDelay.");
            Assert.AreEqual(
                TimeSpan.FromMilliseconds(initialDelay.TotalMilliseconds * 2),
                delays.Delays[1],
                "Second delay should be initialDelay * 2 (exponential backoff).");
        }

        /// <summary>
        /// maxAttempts = 1 disables retry: factory is invoked once and any exception is propagated
        /// immediately without any backoff delay.
        /// </summary>
        [TestMethod]
        public async Task ConnectWithRetryAsync_MaxAttemptsOne_NoRetryOnFailure()
        {
            int callCount = 0;
            RecordingDelay delays = new();

            Task<IConnectionMultiplexer> Factory()
            {
                callCount++;
                throw new InvalidOperationException("boom");
            }

            InvalidOperationException ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => Startup.ConnectWithRetryAsync(
                    Factory,
                    maxAttempts: 1,
                    initialDelay: TimeSpan.FromSeconds(1),
                    logger: null,
                    delayAsync: delays.DelayAsync));

            Assert.AreEqual("boom", ex.Message);
            Assert.AreEqual(1, callCount, "Factory should be invoked exactly once when maxAttempts is 1.");
            Assert.AreEqual(0, delays.Delays.Count, "No delay should be applied when maxAttempts is 1.");
        }

        /// <summary>
        /// Non-Redis exceptions (e.g. timeouts, argument errors) are also retried and ultimately
        /// re-thrown unchanged when all attempts fail. The retry helper does not filter by exception type.
        /// </summary>
        [TestMethod]
        public async Task ConnectWithRetryAsync_RetriesOnAnyException()
        {
            int callCount = 0;
            RecordingDelay delays = new();

            Task<IConnectionMultiplexer> Factory()
            {
                callCount++;
                throw new TimeoutException("timed out");
            }

            TimeoutException ex = await Assert.ThrowsExceptionAsync<TimeoutException>(
                () => Startup.ConnectWithRetryAsync(
                    Factory,
                    maxAttempts: 2,
                    initialDelay: TimeSpan.FromMilliseconds(10),
                    logger: null,
                    delayAsync: delays.DelayAsync));

            Assert.AreEqual("timed out", ex.Message);
            Assert.AreEqual(2, callCount);
            Assert.AreEqual(1, delays.Delays.Count);
        }

        /// <summary>
        /// Invalid arguments are validated up-front before any factory call.
        /// </summary>
        [TestMethod]
        public async Task ConnectWithRetryAsync_InvalidArguments_Throw()
        {
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => Startup.ConnectWithRetryAsync(
                    connectFactory: null!,
                    maxAttempts: 3,
                    initialDelay: TimeSpan.FromMilliseconds(1),
                    logger: null));

            await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(
                () => Startup.ConnectWithRetryAsync(
                    connectFactory: () => Task.FromResult(Mock.Of<IConnectionMultiplexer>()),
                    maxAttempts: 0,
                    initialDelay: TimeSpan.FromMilliseconds(1),
                    logger: null));
        }
    }
}
