// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Azure.DataApiBuilder.Config.DabConfigEvents;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Shared initialization contract used by hosted HTTP startup and the manually started stdio host.
    /// </summary>
    public interface IMcpToolRegistryRefreshService
    {
        /// <summary>
        /// Initializes the registry for the current runtime configuration. Repeated calls for the
        /// same successfully applied configuration are no-ops.
        /// </summary>
        void EnsureInitialized();
    }

    /// <summary>
    /// Builds complete MCP tool-registry generations at startup and after ordered config reloads.
    /// </summary>
    public sealed class McpToolRegistryRefreshService :
        IMcpToolRegistryRefreshService,
        IHostedService
    {
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private readonly IReadOnlyList<IMcpTool> _builtInTools;
        private readonly McpToolRegistry _toolRegistry;
        private readonly IMetadataProviderFactory _metadataProviderFactory;
        private readonly IReadOnlyList<IMcpToolListChangedNotifier> _notifiers;
        private readonly ILogger<McpToolRegistryRefreshService> _logger;
        private readonly object _refreshLock = new();
        private RuntimeConfig? _lastAppliedConfig;

        public McpToolRegistryRefreshService(
            RuntimeConfigProvider runtimeConfigProvider,
            IEnumerable<IMcpTool> tools,
            McpToolRegistry toolRegistry,
            IMetadataProviderFactory metadataProviderFactory,
            IEnumerable<IMcpToolListChangedNotifier> notifiers,
            ILogger<McpToolRegistryRefreshService> logger,
            HotReloadEventHandler<HotReloadEventArgs>? hotReloadEventHandler = null)
        {
            _runtimeConfigProvider = runtimeConfigProvider;
            _builtInTools = tools
                .Where(tool => tool.ToolType == ToolType.BuiltIn)
                .ToArray();
            _toolRegistry = toolRegistry;
            _metadataProviderFactory = metadataProviderFactory;
            _notifiers = notifiers.ToArray();
            _logger = logger;

            hotReloadEventHandler?.Subscribe(
                MCP_TOOL_REGISTRY_ON_CONFIG_CHANGED,
                OnConfigChanged);
        }

        /// <inheritdoc />
        public void EnsureInitialized()
        {
            RefreshRegistry();
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureInitialized();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void OnConfigChanged(object? sender, HotReloadEventArgs args)
        {
            try
            {
                RefreshRegistry();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to refresh the MCP tool registry after a runtime configuration change. " +
                    "The previous registry snapshot remains active.");
            }
        }

        private void RefreshRegistry()
        {
            lock (_refreshLock)
            {
                RuntimeConfig config = _runtimeConfigProvider.GetConfig();
                if (ReferenceEquals(config, _lastAppliedConfig))
                {
                    return;
                }

                List<IMcpTool> customTools = CustomMcpToolFactory
                    .CreateCustomTools(config, _logger)
                    .ToList();

                foreach (DynamicCustomTool customTool in customTools.Cast<DynamicCustomTool>())
                {
                    bool initializedFromDatabase = customTool.InitializeMetadata(
                        config,
                        _metadataProviderFactory);
                    if (!initializedFromDatabase)
                    {
                        _logger.LogWarning(
                            "Database metadata was unavailable for custom MCP tool '{ToolName}' " +
                            "on entity '{EntityName}'. Using configuration-derived input schema.",
                            customTool.GetToolMetadata().Name,
                            customTool.EntityName);
                    }
                }

                McpToolRegistryCandidate candidate = McpToolRegistry.CreateCandidate(
                    _builtInTools.Concat(customTools),
                    config);

                if (!ReferenceEquals(config, _runtimeConfigProvider.GetConfig()))
                {
                    _logger.LogWarning(
                        "Discarded a stale MCP tool registry candidate because a newer runtime " +
                        "configuration became active during the rebuild.");
                    return;
                }

                bool isInitialGeneration = _lastAppliedConfig is null;
                McpToolRegistryUpdateResult result = _toolRegistry.PublishCandidate(candidate);
                _lastAppliedConfig = config;

                _logger.LogInformation(
                    "Published MCP tool registry version {Version} with {RegisteredToolCount} " +
                    "registered tools and {AdvertisedToolCount} advertised tools.",
                    result.Version,
                    result.RegisteredToolCount,
                    result.AdvertisedToolCount);

                if (!isInitialGeneration && result.DiscoveryChanged)
                {
                    NotifyToolsListChanged();
                }
            }
        }

        private void NotifyToolsListChanged()
        {
            foreach (IMcpToolListChangedNotifier notifier in _notifiers)
            {
                try
                {
                    notifier.NotifyToolsListChanged();
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to notify an MCP client that the advertised tool list changed.");
                }
            }
        }
    }

    /// <summary>
    /// Transport-specific notification sink for MCP tool discovery changes.
    /// </summary>
    public interface IMcpToolListChangedNotifier
    {
        /// <summary>
        /// Notifies a connected, initialized client that it should refresh <c>tools/list</c>.
        /// </summary>
        void NotifyToolsListChanged();
    }
}
