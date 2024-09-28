// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config;

/// <summary>
/// HotReloadEventHandler defines event invocation and subscription functions that are
/// used to facilitate updating DAB components' state due to a hot reload.
/// The events defined in this class are invoked in this class (versus being invoked in RuntimeConfigLoader)
/// because events are a special type of delegate that can only be invoked from within the class that declared them.
/// For more information about where events should be invoked, please see:
/// https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/events/how-to-raise-base-class-events-in-derived-classes
/// </summary>
/// <typeparam name="TEventArgs">Args used for hot reload events.</typeparam>
public class HotReloadEventHandler<TEventArgs> where TEventArgs : HotReloadEventArgs
{
    public event EventHandler<TEventArgs>? DocumentorOnConfigChanged;

    public void DocumentorOnConfigChangedEvent(object sender, TEventArgs args)
    {
        DocumentorOnConfigChanged?.Invoke(sender, args);
    }
    public void DocumentorSubscribe(EventHandler<TEventArgs> handler)
    {
        DocumentorOnConfigChanged += handler;
    }
}
