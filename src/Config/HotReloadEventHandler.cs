// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Azure.DataApiBuilder.Config
{
    public class HotReloadEventHandler<TEventArgs> where TEventArgs : CustomEventArgs
    {
        public event EventHandler<TEventArgs>? MetadataProvider_ConfigChangeEventOccurred;
        public event EventHandler<TEventArgs>? Documentor_ConfigChangeEventOccurred;

        public void MetadataProvider_ConfigChangeEventOccurred(object sender, TEventArgs args)
        {
            MetadataProvider_ChangeEventOccurred?.Invoke(sender, args);
        }

        public void MetadataProvider_Subscribe(EventHandler<TEventArgs> handler)
        {
            MetadataProvider_ConfigChangeEventOccurred += handler;
        }

        public void Documentor_ConfigChangeEventOccurred(object sender, TEventArgs args)
        {
            Documentor_ConfigChangeEventOccurred?.Invoke(sender, args);
        }

        public void Documentor_Subscribe(EventHandler<TEventArgs> handler)
        {
            Documentor_ConfigChangeEventOccurred += handler;
        }
    }
}
