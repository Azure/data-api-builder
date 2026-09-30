// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Config
{
    /// <summary>
    /// A general-purpose log buffer that stores log entries before the final log level is determined.
    /// Can be used across different components during startup to capture important early logs.
    /// </summary>
    public class LogBuffer
    {
        /// <summary>
        /// Upper bound on buffered entries. Prevents unbounded growth when the buffer is never drained
        /// (e.g. a loader with no logger in a hot-reload loop). The oldest entries are dropped first.
        /// </summary>
        internal const int MAX_BUFFERED_ENTRIES = 1000;

        private readonly ConcurrentQueue<(LogLevel LogLevel, string Message, Exception? Exception)> _logBuffer;
        private readonly object _flushLock = new();

        public LogBuffer()
        {
            _logBuffer = new();
        }

        /// <summary>
        /// Buffers a log entry with a specific category name.
        /// </summary>
        public void BufferLog(LogLevel logLevel, string message, Exception? exception = null)
        {
            _logBuffer.Enqueue((logLevel, message, exception));

            // Keep the buffer bounded so it cannot grow without limit if it is never drained. Dropping
            // the oldest entries first preserves the most recent (most useful) diagnostics.
            while (_logBuffer.Count > MAX_BUFFERED_ENTRIES && _logBuffer.TryDequeue(out _))
            {
            }
        }

        /// <summary>
        /// Flushes all buffered logs to a single target logger.
        /// </summary>
        public void FlushToLogger(ILogger targetLogger)
        {
            lock (_flushLock)
            {
                while (_logBuffer.TryDequeue(out (LogLevel LogLevel, string Message, Exception? Exception) entry))
                {
                    targetLogger.Log(entry.LogLevel, message: entry.Message, exception: entry.Exception);
                }
            }
        }
    }
}
