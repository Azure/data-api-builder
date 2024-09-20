// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config
{
    public class HotReloadEventHandler<TEventArgs> where TEventArgs : CustomEventArgs
    {
        //public IServiceProvider? ServiceProvider;
        public event EventHandler<TEventArgs>? QueryManagerFactory_ConfigChangeEventOccurred;
        public event EventHandler<TEventArgs>? MetadataProviderFactory_ConfigChangeEventOccurred;
        public event EventHandler<TEventArgs>? QueryEngineFactory_ConfigChangeEventOccurred;
        public event EventHandler<TEventArgs>? MutationEngineFactory_ConfigChangeEventOccurred;
        public event EventHandler<TEventArgs>? QueryExecutor_ConfigChangeEventOccurred;
        public event EventHandler<TEventArgs>? MsSqlQueryExecutor_ConfigChangeEventOccurred;
        public event EventHandler<TEventArgs>? MySqlQueryExecutor_ConfigChangeEventOccurred;
        public event EventHandler<TEventArgs>? PostgreSqlQueryExecutor_ConfigChangeEventOccurred;

        public void QueryManagerFactory_OnConfigChangeEventOccurred(object sender, TEventArgs args)
        {
            QueryManagerFactory_ConfigChangeEventOccurred?.Invoke(sender, args);
        }

        public void QueryManagerFactory_Subscribe(EventHandler<TEventArgs> handler)
        {
            QueryManagerFactory_ConfigChangeEventOccurred += handler;
        }

        public void MetadataProviderFactory_OnConfigChangeEventOccurred(object sender, TEventArgs args)
        {
            MetadataProviderFactory_ConfigChangeEventOccurred?.Invoke(sender, args);
        }

        public void MetadataProviderFactory_Subscribe(EventHandler<TEventArgs> handler)
        {
            MetadataProviderFactory_ConfigChangeEventOccurred += handler;
        }

        public void QueryEngineFactory_OnConfigChangeEventOccurred(object sender, TEventArgs args)
        {
            QueryEngineFactory_ConfigChangeEventOccurred?.Invoke(sender, args);
        }

        public void QueryEngineFactory_Subscribe(EventHandler<TEventArgs> handler)
        {
            QueryEngineFactory_ConfigChangeEventOccurred += handler;
        }

        public void MutationEngineFactory_OnConfigChangeEventOccurred(object sender, TEventArgs args)
        {
            MutationEngineFactory_ConfigChangeEventOccurred?.Invoke(sender, args);
        }

        public void MutationEngineFactory_Subscribe(EventHandler<TEventArgs> handler)
        {
            MutationEngineFactory_ConfigChangeEventOccurred += handler;
        }

        public void QueryExecutor_OnConfigChangeEventOccurred(object sender, TEventArgs args)
        {
            QueryExecutor_ConfigChangeEventOccurred?.Invoke(sender, args);
        }

        public void QueryExecutor_Subscribe(EventHandler<TEventArgs> handler)
        {
            QueryExecutor_ConfigChangeEventOccurred += handler;
        }

        public void MsSqlQueryExecutor_OnConfigChangeEventOccurred(object sender, TEventArgs args)
        {
            MsSqlQueryExecutor_ConfigChangeEventOccurred?.Invoke(sender, args);
        }

        public void MsSqlQueryExecutor_Subscribe(EventHandler<TEventArgs> handler)
        {
            MsSqlQueryExecutor_ConfigChangeEventOccurred += handler;
        }

        public void MySqlQueryExecutor_OnConfigChangeEventOccurred(object sender, TEventArgs args)
        {
            MySqlQueryExecutor_ConfigChangeEventOccurred?.Invoke(sender, args);
        }

        public void MySqlQueryExecutor_Subscribe(EventHandler<TEventArgs> handler)
        {
            MySqlQueryExecutor_ConfigChangeEventOccurred += handler;
        }

        public void PostgreSqlQueryExecutor_OnConfigChangeEventOccurred(object sender, TEventArgs args)
        {
            PostgreSqlQueryExecutor_ConfigChangeEventOccurred?.Invoke(sender, args);
        }

        public void PostgreSqlQueryExecutor_Subscribe(EventHandler<TEventArgs> handler)
        {
            PostgreSqlQueryExecutor_ConfigChangeEventOccurred += handler;
        }
    }
}
