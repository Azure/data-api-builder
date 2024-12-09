// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config;

public class HotReloadEventArgs : EventArgs
{
    public string EventName { get; set; }

    public string Message { get; set; }

    public HotReloadEventArgs(string eventName, string message)
    {
        EventName = eventName;
        Message = message;
    }
}
