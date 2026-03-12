// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Config
{
    /// <summary>
    /// Buffers log entries that are emitted before the final log level is determined (e.g. before
    /// the <see cref="RuntimeConfig"/> has been loaded).  Once the proper
    /// <see cref="ILogger"/> is available, call <see cref="FlushToLogger"/> to replay the buffered
    /// entries at their original log levels.
    /// </summary>
    public class StartupLogBuffer
    {
        private readonly ConcurrentQueue<(LogLevel LogLevel, string Message)> _logBuffer = new();
        private readonly object _flushLock = new();

        /// <summary>
        /// Enqueues a log entry to be emitted later.
        /// </summary>
        /// <param name="logLevel">Severity of the log entry.</param>
        /// <param name="message">Log message.</param>
        public void BufferLog(LogLevel logLevel, string message)
        {
            _logBuffer.Enqueue((logLevel, message));
        }

        /// <summary>
        /// Drains the buffer and forwards every entry to <paramref name="targetLogger"/>.
        /// Entries are discarded (not re-buffered) when <paramref name="targetLogger"/> is null.
        /// This method is thread-safe and idempotent: a second call after the buffer is empty
        /// is a no-op.
        /// </summary>
        /// <param name="targetLogger">The logger to write buffered entries to, or null to discard them.</param>
        public void FlushToLogger(ILogger? targetLogger)
        {
            lock (_flushLock)
            {
                while (_logBuffer.TryDequeue(out (LogLevel LogLevel, string Message) entry))
                {
                    targetLogger?.Log(entry.LogLevel, message: entry.Message);
                }
            }
        }
    }
}
