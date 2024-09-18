// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config
{
    /// <summary>
    /// HotReloadEventHandler manages the events that are needed to signal refreshing
    /// classes that must be updated during a hot reload.
    /// </summary>
    /// <typeparam name="TEventArgs">Args used for hot reload events.</typeparam>
    public class HotReloadEventHandler<TEventArgs> where TEventArgs : CustomEventArgs
    {
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
