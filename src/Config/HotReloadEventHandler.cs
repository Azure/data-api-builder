// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config
{
    public class HotReloadEventHandler<TEventArgs> where TEventArgs : CustomEventArgs
    {
        //public IServiceProvider? ServiceProvider;
        public event EventHandler<TEventArgs>? QueryManagerFactory_ConfigChangeEventOccurred;
        public event EventHandler<TEventArgs>? MetadataProviderFactory_ConfigChangeEventOccurred;

        public void MetadataProvider_OnConfigChangeEventOccurred(object sender, TEventArgs args)
        {
            MetadataProviderFactory_ConfigChangeEventOccurred?.Invoke(sender, args);
        }

        public void MetadataProvider_Subscribe(EventHandler<TEventArgs> handler)
        {
            MetadataProviderFactory_ConfigChangeEventOccurred += handler;
        }

        public void QueryManagerFactory_OnConfigChangeEventOccurred(object sender, TEventArgs args)
        {
            QueryManagerFactory_ConfigChangeEventOccurred?.Invoke(sender, args);
        }

        public void QueryManagerFactory_Subscribe(EventHandler<TEventArgs> handler)
        {
            QueryManagerFactory_ConfigChangeEventOccurred += handler;
        }
    }
}
