// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config;

/// <summary>
/// HotReloadEventHandler manages the events that are needed to signal refreshing
/// classes that must be updated during a hot reload. Events are defied in this class
/// rather than in the RuntimeConfigLoader where they are raised because of the interaction
/// between events and base and derived classes. For more information please see:
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
