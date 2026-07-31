// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Model;
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
        private readonly McpStdoutWriter _stdoutWriter;
        private int _isInitialized;

        public McpStdioToolListChangedNotifier(McpStdoutWriter stdoutWriter)
        {
            _stdoutWriter = stdoutWriter;
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

            var notification = new
            {
                jsonrpc = McpStdioJsonRpcErrorCodes.JSON_RPC_VERSION,
                method = NotificationMethods.ToolListChangedNotification,
                @params = new { }
            };

            _stdoutWriter.WriteLine(JsonSerializer.Serialize(notification));
        }
    }
}
