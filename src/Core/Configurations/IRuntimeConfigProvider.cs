// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using static Azure.DataApiBuilder.Core.Configurations.HostedRuntimeConfigProvider;

namespace Azure.DataApiBuilder.Core.Configurations
{
    public interface IRuntimeConfigProvider
    {
        public bool IsLateConfigured { get; set; }

        public RuntimeConfigLoader ConfigLoader { get; set; }

        public List<RuntimeConfigLoadedHandler> RuntimeConfigLoadedHandlers { get; }

        public Dictionary<string, string?> ManagedIdentityAccessToken { get; set; }

        public RuntimeConfig GetConfig();

        public bool TryGetConfig([NotNullWhen(true)] out RuntimeConfig? runtimeConfig);

        public bool TryGetLoadedConfig([NotNullWhen(true)] out RuntimeConfig? runtimeConfig);
    }
}
