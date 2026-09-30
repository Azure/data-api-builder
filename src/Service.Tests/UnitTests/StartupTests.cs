// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using StackExchange.Redis;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class StartupTests
    {
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
