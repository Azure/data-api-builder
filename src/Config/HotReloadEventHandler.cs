// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config;

public class HotReloadEventHandler<TEventArgs> where TEventArgs : HotReloadEventArgs
{
    private readonly Dictionary<string, EventHandler<TEventArgs>?> _eventHandlers;

    public HotReloadEventHandler()
    {
        _eventHandlers = new Dictionary<string, EventHandler<TEventArgs>?>
        {
            { "QueryManagerFactoryOnConfigChanged", null },
            { "MetadataProviderFactoryOnConfigChanged", null },
            { "QueryEngineFactoryOnConfigChanged", null },
            { "MutationEngineFactoryOnConfigChanged", null },
            { "QueryExecutorOnConfigChanged", null },
            { "MsSqlQueryExecutorOnConfigChanged", null },
            { "MySqlQueryExecutorOnConfigChanged", null },
            { "PostgreSqlQueryExecutorOnConfigChanged", null }
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
