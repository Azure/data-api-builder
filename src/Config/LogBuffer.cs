// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Config
{
    /// <summary>
    /// A general-purpose log buffer that stores log entries before the final log level is determined.
    /// A single shared instance (<see cref="Instance"/>) holds the early logs of all components so they
    /// are emitted in the order they were produced. Each entry records the category of the logger it
    /// belongs to so it can be routed to the right logger on flush.
    /// </summary>
    public class LogBuffer
    {
        /// <summary>
        /// Upper bound on buffered entries. Prevents unbounded growth when the buffer is never drained
        /// (e.g. a loader with no logger in a hot-reload loop). The oldest entries are dropped first.
        /// </summary>
        internal const int MAX_BUFFERED_ENTRIES = 1000;

        /// <summary>
        /// The single buffer shared by all loggers.
        /// </summary>
        public static LogBuffer Instance { get; } = new();

        private readonly Queue<(string? Category, LogLevel LogLevel, string Message, Exception? Exception)> _logBuffer = new();
        private readonly object _lock = new();

        internal LogBuffer()
        {
        }

        /// <summary>
        /// Buffers a log entry. A <c>null</c> category is routed to the flushing component's default logger.
        /// </summary>
        public void BufferLog(LogLevel logLevel, string message, Exception? exception = null, string? category = null)
        {
            lock (_lock)
            {
                _logBuffer.Enqueue((category, logLevel, message, exception));

                // Keep the buffer bounded so it cannot grow without limit if it is never drained. Dropping
                // the oldest entries first preserves the most recent (most useful) diagnostics.
                while (_logBuffer.Count > MAX_BUFFERED_ENTRIES)
                {
                    _logBuffer.Dequeue();
                }
            }
        }

        /// <summary>
        /// Buffers a log entry for the logger category of <typeparamref name="TCategory"/>.
        /// </summary>
        public void BufferLog<TCategory>(LogLevel logLevel, string message, Exception? exception = null)
        {
            BufferLog(logLevel, message, exception, typeof(TCategory).FullName);
        }

        /// <summary>
        /// Flushes all buffered logs to a single target logger.
        /// </summary>
        public void FlushToLogger(ILogger targetLogger)
        {
            FlushToLogger(_ => targetLogger);
        }

        /// <summary>
        /// Flushes buffered logs, in order, to the logger resolved for each entry's category.
        /// Entries whose category resolves to <c>null</c> remain buffered (in order) for a later flush.
        /// </summary>
        public void FlushToLogger(Func<string?, ILogger?> loggerResolver)
        {
            lock (_lock)
            {
                int count = _logBuffer.Count;
                for (int i = 0; i < count; i++)
                {
                    (string? Category, LogLevel LogLevel, string Message, Exception? Exception) entry = _logBuffer.Dequeue();
                    ILogger? targetLogger = loggerResolver(entry.Category);
                    if (targetLogger is null)
                    {
                        _logBuffer.Enqueue(entry);
                    }
                    else
                    {
                        targetLogger.Log(entry.LogLevel, message: entry.Message, exception: entry.Exception);
                    }
                }
            }
        }
    }
}
