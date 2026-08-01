// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config;

public class HotReloadEventArgs : EventArgs
{
    public string EventName { get; set; }

    public string Message { get; set; }

    /// <summary>
    /// Cancels the current ordered hot-reload generation during loader shutdown.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    public HotReloadEventArgs(string eventName, string message)
        : this(eventName, message, CancellationToken.None)
    {
    }

    public HotReloadEventArgs(
        string eventName,
        string message,
        CancellationToken cancellationToken)
    {
        EventName = eventName;
        Message = message;
        CancellationToken = cancellationToken;
    }
}
