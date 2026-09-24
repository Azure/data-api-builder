// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Config;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class LogBufferTests
    {
        /// <summary>
        /// The buffer is bounded so it cannot grow without limit if it is never drained (e.g. a loader
        /// with no logger in a hot-reload loop). Beyond the cap, the oldest entries are dropped while the
        /// most recent are retained.
        /// </summary>
        [TestMethod]
        public void BufferLog_IsBounded_DropsOldestBeyondCap()
        {
            LogBuffer buffer = new();
            int overflow = LogBuffer.MAX_BUFFERED_ENTRIES + 50;

            for (int i = 0; i < overflow; i++)
            {
                buffer.BufferLog(LogLevel.Debug, $"entry-{i}");
            }

            CapturingLogger logger = new();
            buffer.FlushToLogger(logger);

            Assert.AreEqual(
                LogBuffer.MAX_BUFFERED_ENTRIES,
                logger.Messages.Count,
                "Buffer should be capped at MAX_BUFFERED_ENTRIES regardless of how many entries were buffered.");
            Assert.IsTrue(
                logger.Messages.Contains($"entry-{overflow - 1}"),
                "The most recent entry should be retained.");
            Assert.IsFalse(
                logger.Messages.Contains("entry-0"),
                "The oldest entries should be dropped once the cap is exceeded.");
        }

        /// <summary>
        /// Within the cap, all buffered entries are flushed in order and the buffer is drained (a second
        /// flush emits nothing).
        /// </summary>
        [TestMethod]
        public void FlushToLogger_EmitsAllBufferedEntries_AndDrains()
        {
            LogBuffer buffer = new();
            buffer.BufferLog(LogLevel.Information, "first");
            buffer.BufferLog(LogLevel.Warning, "second");

            CapturingLogger logger = new();
            buffer.FlushToLogger(logger);

            CollectionAssert.AreEqual(new[] { "first", "second" }, logger.Messages.ToArray());

            // The queue is drained on flush, so a second flush emits nothing more.
            CapturingLogger secondLogger = new();
            buffer.FlushToLogger(secondLogger);
            Assert.AreEqual(0, secondLogger.Messages.Count, "A drained buffer should emit nothing on a subsequent flush.");
        }

        /// <summary>Minimal in-memory <see cref="ILogger"/> that records formatted messages for assertions.</summary>
        private sealed class CapturingLogger : ILogger
        {
            public List<string> Messages { get; } = new();

            public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
                => Messages.Add(formatter(state, exception));

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
