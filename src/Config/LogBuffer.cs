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
