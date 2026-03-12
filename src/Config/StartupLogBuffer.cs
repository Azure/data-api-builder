// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Azure.DataApiBuilder.Config
{
    /// <summary>
    /// A general-purpose log buffer that stores log entries before the final log level is determined.
    /// Can be used across different components during startup to capture important early logs.
    /// </summary>
    public class StartupLogBuffer
    {
        private readonly ConcurrentQueue<(LogLevel LogLevel, string Message)> _logBuffer;
        private readonly object _flushLock = new();

        public StartupLogBuffer()
        {
            _logBuffer = new();
        }

        /// <summary>
        /// Buffers a log entry with a specific category name.
        /// </summary>
        public void BufferLog(LogLevel logLevel, string message)
        {
            _logBuffer.Enqueue((logLevel, message));
        }

        /// <summary>
        /// Flushes all buffered logs to a single target logger.
        /// </summary>
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
