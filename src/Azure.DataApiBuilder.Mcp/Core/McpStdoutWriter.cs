// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Process-wide owner of the MCP stdio server's stdout stream.
    ///
    /// In MCP stdio mode, stdout is the JSON-RPC channel and is shared by
    /// multiple writers — JSON-RPC responses from <see cref="McpStdioServer"/>
    /// and asynchronous <c>notifications/message</c> frames from the logging
    /// pipeline. Without coordination, two writers calling <c>WriteLine</c>
    /// concurrently can interleave at the byte level and corrupt the channel.
    ///
    /// This class wraps the underlying <see cref="StreamWriter"/> and serializes
    /// every write through a single lock so JSON-RPC frames stay intact.
    /// Registered as a singleton in DI for MCP stdio mode; instantiated lazily
    /// (the underlying stream is opened on the first write) so non-MCP code
    /// paths and unit tests can construct the type without side effects.
    /// </summary>
    public sealed class McpStdoutWriter : IDisposable
    {
        private readonly object _lock = new();
        private TextWriter? _writer;
        private bool _disposed;

        /// <summary>
        /// Production constructor. The underlying stdout stream is opened
        /// lazily on the first <see cref="WriteLine"/> call.
        /// </summary>
        public McpStdoutWriter()
        {
        }

        /// <summary>
        /// Test-only constructor that injects a pre-built writer so unit tests
        /// can verify lock behavior, disposal semantics, and notification
        /// framing without touching the real stdout stream.
        /// </summary>
        internal McpStdoutWriter(TextWriter writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        /// <summary>
        /// Writes a single line to stdout under a process-wide lock so
        /// concurrent JSON-RPC responses and notifications cannot interleave.
        /// No-op after <see cref="Dispose"/>.
        /// </summary>
        public void WriteLine(string line)
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                EnsureInitialized();
                _writer!.WriteLine(line);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _writer?.Dispose();
                _writer = null;
            }
        }

        private void EnsureInitialized()
        {
            if (_writer is not null)
            {
                return;
            }

            // Opening the raw stdout stream bypasses any Console.SetOut(...)
            // redirection. This is intentional: in MCP stdio mode, Program.cs
            // redirects Console.Out to a sink (TextWriter.Null or stderr) so
            // stray Console.WriteLine calls from third-party code cannot
            // corrupt the JSON-RPC channel. Only this class - and only via
            // WriteLine() - is allowed to write to the real stdout.
            Stream stdout = Console.OpenStandardOutput();
            _writer = new StreamWriter(stdout, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
        }
    }
}
