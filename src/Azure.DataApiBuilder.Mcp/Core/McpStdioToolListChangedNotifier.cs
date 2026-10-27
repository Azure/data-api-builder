// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Stdio-specific lifecycle contract used by the JSON-RPC server to mark the client ready for
    /// unsolicited tool-list change notifications.
    /// </summary>
    public interface IMcpStdioToolListChangedNotifier : IMcpToolListChangedNotifier
    {
        /// <summary>
        /// Marks the MCP initialization handshake complete.
        /// </summary>
        void MarkInitialized();
    }

    /// <summary>
    /// Writes MCP <c>notifications/tools/list_changed</c> frames for an initialized stdio client.
    /// </summary>
    public sealed class McpStdioToolListChangedNotifier : IMcpStdioToolListChangedNotifier
    {
        private static readonly string _notificationJson = JsonSerializer.Serialize(new
        {
            jsonrpc = McpStdioJsonRpcErrorCodes.JSON_RPC_VERSION,
            method = NotificationMethods.ToolListChangedNotification,
            @params = new { }
        });

        private readonly McpStdoutWriter _stdoutWriter;
        private readonly ILogger<McpStdioToolListChangedNotifier> _logger;
        private readonly Func<Action, bool> _tryScheduleWorker;
        private int _isInitialized;
        private int _notificationPending;
        private int _notificationWorkerScheduled;

        public McpStdioToolListChangedNotifier(
            McpStdoutWriter stdoutWriter,
            ILogger<McpStdioToolListChangedNotifier>? logger = null)
            : this(stdoutWriter, logger, TryScheduleOnThreadPool)
        {
        }

        internal McpStdioToolListChangedNotifier(
            McpStdoutWriter stdoutWriter,
            ILogger<McpStdioToolListChangedNotifier>? logger,
            Func<Action, bool> tryScheduleWorker)
        {
            _stdoutWriter = stdoutWriter ?? throw new ArgumentNullException(nameof(stdoutWriter));
            _logger = logger ?? NullLogger<McpStdioToolListChangedNotifier>.Instance;
            _tryScheduleWorker = tryScheduleWorker ??
                throw new ArgumentNullException(nameof(tryScheduleWorker));
        }

        /// <inheritdoc />
        public void MarkInitialized()
        {
            Interlocked.Exchange(ref _isInitialized, 1);
        }

        /// <inheritdoc />
        public void NotifyToolsListChanged()
        {
            if (Volatile.Read(ref _isInitialized) == 0)
            {
                return;
            }

            // One pending invalidation is sufficient: after receiving it, the client requests the
            // latest complete snapshot. This keeps queued state bounded while stdout is blocked.
            Interlocked.Exchange(ref _notificationPending, 1);
            ScheduleNotificationWorker();
        }

        private void ScheduleNotificationWorker()
        {
            if (Interlocked.CompareExchange(ref _notificationWorkerScheduled, 1, 0) != 0)
            {
                return;
            }

            Action worker = ProcessPendingNotifications;
            if (!_tryScheduleWorker(worker))
            {
                // Do not clear _notificationPending: this invalidation is still required even if
                // no later configuration change occurs. A dedicated background thread is a rare
                // fallback for ThreadPool queue rejection and preserves the nonblocking contract.
                _logger.LogWarning(
                    "Failed to queue an MCP tool-list change notification on the thread pool. " +
                    "Starting a dedicated fallback worker.");
                StartDedicatedFallbackWorker(worker);
            }
        }

        private static bool TryScheduleOnThreadPool(Action worker)
        {
            return ThreadPool.QueueUserWorkItem(
                static callback => callback(),
                worker,
                preferLocal: false);
        }

        private void StartDedicatedFallbackWorker(Action worker)
        {
            try
            {
                Thread fallbackWorker = new(
                    static callback => ((Action)callback!).Invoke())
                {
                    IsBackground = true,
                    Name = "DAB MCP tool-list notification fallback"
                };
                fallbackWorker.Start(worker);
            }
            catch (Exception ex)
            {
                // Retain the pending flag and reopen the scheduling gate. A later notification can
                // retry delivery if the process could not create the fallback thread.
                Volatile.Write(ref _notificationWorkerScheduled, 0);
                _logger.LogError(
                    ex,
                    "Failed to start the MCP tool-list notification fallback worker. " +
                    "The notification remains pending.");
            }
        }

        private void ProcessPendingNotifications()
        {
            try
            {
                while (Interlocked.Exchange(ref _notificationPending, 0) != 0)
                {
                    try
                    {
                        _stdoutWriter.WriteLine(_notificationJson);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to write an MCP tool-list change notification.");
                    }
                }
            }
            finally
            {
                Volatile.Write(ref _notificationWorkerScheduled, 0);

                // A publication can race with worker shutdown after the final pending-flag
                // exchange. Reschedule so that invalidation is never lost in that window.
                if (Volatile.Read(ref _notificationPending) != 0)
                {
                    ScheduleNotificationWorker();
                }
            }
        }
    }
}
