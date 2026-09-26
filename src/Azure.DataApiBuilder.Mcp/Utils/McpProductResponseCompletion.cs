// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp.Utils;

/// <summary>
/// Correlates one tool response through the SDK's nonserialized Items, without retaining an
/// ID or payload. A byte-opaque stream observer confirms that this response actually flushed.
/// </summary>
internal sealed class McpProductResponseCompletion
{
    private const string ITEM_KEY = "DAB.ProductTelemetry.ResponseCompletion";
    private static readonly AsyncLocal<McpProductResponseCompletion?> _sending = new();
    private readonly EngineTelemetryRequestScope _request;
    private CancellationTokenRegistration _cancellation;
    private int _completed;
    private int _wrote;
    private int _flushed;

    internal McpProductResponseCompletion(EngineTelemetryRequestScope request, CancellationToken cancellationToken)
    {
        _request = request;
        _cancellation = cancellationToken.UnsafeRegister(static value =>
            ((McpProductResponseCompletion)value!).Complete(EngineTelemetryOutcome.Canceled), this);
        if (Volatile.Read(ref _completed) != 0)
        {
            _cancellation.Unregister();
        }
    }

    internal static McpProductResponseCompletion Attach(IDictionary<string, object?> items, EngineTelemetryRequestScope request,
        CancellationToken cancellationToken = default)
    {
        McpProductResponseCompletion completion = new(request, cancellationToken);
        items[ITEM_KEY] = completion;
        return completion;
    }

    internal void Abandon(IDictionary<string, object?> items)
    {
        _cancellation.Unregister();
        items.Remove(ITEM_KEY);
    }

    internal static void Wrote() => _sending.Value?.RecordWrite();

    internal static void Flushed() => _sending.Value?.RecordFlush();

    internal static void WriteFailed(bool canceled) => _sending.Value?.Complete(canceled
        ? EngineTelemetryOutcome.Canceled : EngineTelemetryOutcome.Failure);

    internal static McpMessageHandler Filter(McpMessageHandler next) => async (context, cancellationToken) =>
    {
        // The SDK copies the request context onto ordinary responses. SDK-generated error
        // messages may not preserve Items; the tool wrapper already records thrown failures.
        if (context.JsonRpcMessage is not JsonRpcResponse ||
            context.JsonRpcMessage.Context?.Items is not { } items ||
            !items.TryGetValue(ITEM_KEY, out object? value) || value is not McpProductResponseCompletion completion)
        {
            await next(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        McpProductResponseCompletion? previous = _sending.Value;
        _sending.Value = completion;
        try
        {
            await next(context, cancellationToken).ConfigureAwait(false);
            if (!completion._request.IsCompleted)
            {
                // Disposed transports can silently drop a send; an SDK return alone is not
                // delivery. Preserve an unknown outcome if no write+flush was observed.
                completion.Complete(Volatile.Read(ref completion._flushed) != 0
                    ? completion._request.Outcome : EngineTelemetryOutcome.Unknown);
            }
        }
        catch (OperationCanceledException)
        {
            completion.Complete(EngineTelemetryOutcome.Canceled);
            throw;
        }
        catch (Exception)
        {
            completion.Complete(EngineTelemetryOutcome.Failure);
            throw;
        }
        finally
        {
            _sending.Value = previous;
            items.Remove(ITEM_KEY);
        }
    };

    private void RecordWrite() => Volatile.Write(ref _wrote, 1);

    private void RecordFlush()
    {
        if (Volatile.Read(ref _wrote) != 0)
        {
            Volatile.Write(ref _flushed, 1);
        }
    }

    private void Complete(EngineTelemetryOutcome outcome)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        _cancellation.Unregister();
        try
        {
            _request.Complete(outcome, Volatile.Read(ref _flushed) != 0 ? StatusCodes.Status200OK : null);
        }
        catch (Exception)
        {
            // Optional product recording must never change SDK response delivery.
        }
    }
}
