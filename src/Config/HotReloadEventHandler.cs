// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using static Azure.DataApiBuilder.Config.DabConfigEvents;

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
    private readonly Dictionary<string, EventHandler<TEventArgs>?> _eventHandlers;

    public HotReloadEventHandler()
    {
        _eventHandlers = new Dictionary<string, EventHandler<TEventArgs>?>
        {
            { QUERY_MANAGER_FACTORY_ON_CONFIG_CHANGED, null },
            { METADATA_PROVIDER_FACTORY_ON_CONFIG_CHANGED, null },
            { QUERY_ENGINE_FACTORY_ON_CONFIG_CHANGED,null },
            { MUTATION_ENGINE_FACTORY_ON_CONFIG_CHANGED,null },
            { QUERY_EXECUTOR_ON_CONFIG_CHANGED, null },
            { MSSQL_QUERY_EXECUTOR_ON_CONFIG_CHANGED, null },
            { MYSQL_QUERY_EXECUTOR_ON_CONFIG_CHANGED, null },
            { POSTGRESQL_QUERY_EXECUTOR_ON_CONFIG_CHANGED, null },
            { DOCUMENTOR_ON_CONFIG_CHANGED, null }
        };
    }

    public void OnConfigChangedEvent(string eventName, object sender, TEventArgs args)
    {
        if (_eventHandlers.TryGetValue(eventName, out EventHandler<TEventArgs>? handler))
        {
            handler?.Invoke(sender, args);
        }
    }

    public void Subscribe(string eventName, EventHandler<TEventArgs> handler)
    {
        if (_eventHandlers.ContainsKey(eventName))
        {
            _eventHandlers[eventName] = handler;
        }
    }
}
