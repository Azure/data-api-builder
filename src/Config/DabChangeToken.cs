// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Config;

public class DabChangeToken : IChangeToken
{
    private CancellationTokenSource _cts = new();

    public bool HasChanged => _cts.IsCancellationRequested;

    public bool ActiveChangeCallbacks => true;

    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
    {
        return _cts.Token.Register(callback, state);
    }

    public void SignalChange()
    {
        _cts.Cancel();
    }
}

