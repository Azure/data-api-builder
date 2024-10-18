// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Config;

/// <summary>
/// Propagates notifications that a change has occurred.
/// </summary>
/// <seealso cref=""/>
public class DabChangeToken : IChangeToken
{
    private CancellationTokenSource _cts = new();

    /// <summary>
    /// Gets a value that indicates if a change has occurred.
    /// </summary>
    public bool HasChanged => _cts.IsCancellationRequested;

    /// <summary>
    /// Indicates if this token will pro-actively raise callbacks. If <c>false</c>, the token consumer must
    /// poll <see cref="HasChanged" /> to detect changes.
    /// </summary>
    public bool ActiveChangeCallbacks => true;

    /// <summary>
    /// Registers for a callback that will be invoked when the entry has changed.
    /// <see cref="HasChanged"/> MUST be set before the callback is invoked.
    /// Used by ChangeToken.OnChange callback registration.
    /// </summary>
    /// <param name="callback">The <see cref="Action{Object}"/> to invoke.</param>
    /// <param name="state">State to be passed into the callback.</param>
    /// <returns>An <see cref="IDisposable"/> that is used to unregister the callback.</returns>
    /// <seealso cref="https://github.com/dotnet/runtime/blob/2e0276cbbaeef01afc4cfabfb224ced729963c79/src/libraries/Microsoft.Extensions.Primitives/src/ChangeToken.cs"/>
    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
    {
        return _cts.Token.Register(callback, state);
    }

    public void SignalChange()
    {
        _cts.Cancel();
    }
}

