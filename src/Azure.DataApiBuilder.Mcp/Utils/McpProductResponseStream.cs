// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Mcp.Utils;

/// <summary>
/// Delegates every byte unchanged. Only successful write/flush boundaries and closed failure
/// categories are observed; no buffer, message text, request ID or exception is retained.
/// </summary>
internal sealed class McpProductResponseStream(Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }

    public override void Flush()
    {
        try
        {
            inner.Flush();
            McpProductResponseCompletion.Flushed();
        }
        catch (Exception exception)
        {
            McpProductResponseCompletion.WriteFailed(exception is OperationCanceledException);
            throw;
        }
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        try
        {
            await inner.FlushAsync(cancellationToken).ConfigureAwait(false);
            McpProductResponseCompletion.Flushed();
        }
        catch (Exception exception)
        {
            McpProductResponseCompletion.WriteFailed(exception is OperationCanceledException || cancellationToken.IsCancellationRequested);
            throw;
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        try
        {
            inner.Write(buffer, offset, count);
            if (count > 0)
            {
                McpProductResponseCompletion.Wrote();
            }
        }
        catch (Exception exception)
        {
            McpProductResponseCompletion.WriteFailed(exception is OperationCanceledException);
            throw;
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (!buffer.IsEmpty)
            {
                McpProductResponseCompletion.Wrote();
            }
        }
        catch (Exception exception)
        {
            McpProductResponseCompletion.WriteFailed(exception is OperationCanceledException || cancellationToken.IsCancellationRequested);
            throw;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    // The ASP.NET response owns the inner stream; this observer must never dispose it.
}
