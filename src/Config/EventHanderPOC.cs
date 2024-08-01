// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config
{
    public class EventHanderPOC<TEventArgs> where TEventArgs: EventArgs
    {
        public event EventHandler<TEventArgs>? EventOccurred;

        public void OnEventOccurred(object sender, TEventArgs args)
        {
            EventOccurred?.Invoke(sender, args);
        }

        public void Subscribe(EventHandler<TEventArgs> handler)
        {
            EventOccurred += handler;
        }
    }
}
