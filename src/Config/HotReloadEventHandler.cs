// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config
{
    public class HotReloadEventHandler<TEventArgs> where TEventArgs : CustomEventArgs
    {
        public event EventHandler<TEventArgs>? MetadataProvider_ConfigChangeEventOccurred;
        public event EventHandler<TEventArgs>? Documentor_ConfigChangeEventOccurred;

        public void Documentor_OnConfigChangeEventOccurred(object sender, TEventArgs args)
        {
            Documentor_ConfigChangeEventOccurred?.Invoke(sender, args);
        }

        public void Documentor_Subscribe(EventHandler<TEventArgs> handler)
        {
            Documentor_ConfigChangeEventOccurred += handler;
        }
    }
}
