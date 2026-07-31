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
        private readonly IReadOnlyList<IMcpTool> _registeredTools;
        private readonly McpToolRegistry _toolRegistry;
        private readonly IMetadataProviderFactory _metadataProviderFactory;
        private readonly IReadOnlyList<IMcpToolListChangedNotifier> _notifiers;
        private readonly ILogger<McpToolRegistryRefreshService> _logger;
        private readonly object _refreshLock = new();
        // RuntimeConfigLoader publishes a new object for every parsed generation. Reference
        // identity is therefore the generation token used by both idempotency and stale guards.
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
            // Configuration-generated DynamicCustomTool instances are created separately for each
            // generation. Every tool explicitly registered in DI remains an independent extension
            // and must be retained regardless of its declared ToolType.
            _registeredTools = tools.ToArray();
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
            if (RefreshRegistry(forceRebuildForCurrentConfig: false))
            {
                NotifyToolsListChanged();
            }
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Startup.Configure initializes database metadata after hosted services start.
            // The HTTP startup orchestrator calls EnsureInitialized once that dependency is
            // ready. Keeping this hosted-service registration ensures this singleton is created
            // early enough to subscribe to ordered hot-reload events without publishing a
            // config-only schema first.
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
                // The runtime config becomes current before its ordered dependency events run.
                // An out-of-band EnsureInitialized call can therefore observe this config while
                // the metadata provider still represents the previous generation. Always rebuild
                // at the ordered MCP event, after metadata and authorization have been refreshed.
                if (RefreshRegistry(forceRebuildForCurrentConfig: true))
                {
                    // Transport notification is deliberately outside _refreshLock. Implementations
                    // must enqueue any potentially blocking I/O so the ordered reload pipeline can
                    // continue to GraphQL and logging handlers.
                    NotifyToolsListChanged();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to refresh the MCP tool registry after a runtime configuration change. " +
                    "The previous registry snapshot remains active.");
            }
        }

        /// <returns>
        /// <see langword="true"/> when an initialized client should be notified after the writer
        /// lock is released; otherwise <see langword="false"/>.
        /// </returns>
        private bool RefreshRegistry(bool forceRebuildForCurrentConfig)
        {
            lock (_refreshLock)
            {
                RuntimeConfig config = _runtimeConfigProvider.GetConfig();
                if (!forceRebuildForCurrentConfig && ReferenceEquals(config, _lastAppliedConfig))
                {
                    return false;
                }

                List<DynamicCustomTool> customTools = CustomMcpToolFactory
                    .CreateCustomTools(config, _logger)
                    .ToList();

                foreach (DynamicCustomTool customTool in customTools)
                {
                    bool initializedFromDatabase = customTool.InitializeMetadata(
                        config,
                        _metadataProviderFactory,
                        out string fallbackReason);
                    if (!initializedFromDatabase)
                    {
                        _logger.LogWarning(
                            "Using configuration-derived input schema for custom MCP tool " +
                            "'{ToolName}' on entity '{EntityName}'. Reason: {FallbackReason}",
                            customTool.ToolName,
                            customTool.EntityName,
                            fallbackReason);
                    }
                }

                McpToolRegistryCandidate candidate = McpToolRegistry.CreateCandidate(
                    _registeredTools.Concat(customTools),
                    config);

                if (!ReferenceEquals(config, _runtimeConfigProvider.GetConfig()))
                {
                    _logger.LogWarning(
                        "Discarded a stale MCP tool registry candidate because a newer runtime " +
                        "configuration became active during the rebuild.");
                    return false;
                }

                bool isInitialGeneration = _lastAppliedConfig is null;
                McpToolRegistryUpdateResult result = _toolRegistry.PublishCandidate(candidate);
                _lastAppliedConfig = config;

                _logger.LogInformation(
                    "Published MCP tool registry version {Version} with {BuiltInToolCount} " +
                    "built-in tools, {RegisteredCustomToolCount} DI-registered custom tools, " +
                    "{GeneratedCustomToolCount} configuration-generated custom tools, {RegisteredToolCount} " +
                    "registered tools, and {AdvertisedToolCount} advertised tools. " +
                    "Discovery changed: {DiscoveryChanged}.",
                    result.Version,
                    _registeredTools.Count(tool => tool.ToolType == ToolType.BuiltIn),
                    _registeredTools.Count(tool => tool.ToolType != ToolType.BuiltIn),
                    customTools.Count,
                    result.RegisteredToolCount,
                    result.AdvertisedToolCount,
                    result.DiscoveryChanged);

                return !isInitialGeneration && result.DiscoveryChanged;
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
        /// Enqueues notification of a connected, initialized client that it should refresh
        /// <c>tools/list</c>. Implementations must not block on transport I/O.
        /// </summary>
        void NotifyToolsListChanged();
    }
}
