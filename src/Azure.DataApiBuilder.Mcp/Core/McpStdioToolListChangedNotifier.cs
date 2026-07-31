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
        private int _isInitialized;
        private int _notificationPending;
        private int _notificationWorkerScheduled;

        public McpStdioToolListChangedNotifier(
            McpStdoutWriter stdoutWriter,
            ILogger<McpStdioToolListChangedNotifier>? logger = null)
        {
            _stdoutWriter = stdoutWriter;
            _logger = logger ?? NullLogger<McpStdioToolListChangedNotifier>.Instance;
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

            if (!ThreadPool.QueueUserWorkItem(
                    static notifier => notifier.ProcessPendingNotifications(),
                    this,
                    preferLocal: false))
            {
                Interlocked.Exchange(ref _notificationPending, 0);
                Volatile.Write(ref _notificationWorkerScheduled, 0);
                _logger.LogError("Failed to queue an MCP tool-list change notification.");
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
