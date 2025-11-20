// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Azure.Cosmos;

namespace Azure.DataApiBuilder.Config;

public class CosmosDbRuntimeConfigLoader : RuntimeConfigLoader
{
    private readonly CosmosClient _cosmosClient;
    private readonly Container _container;
    private readonly string _documentId;
    private readonly string _partitionKey;
    private string? _lastETag;
    private Timer? _pollingTimer;

    public CosmosDbRuntimeConfigLoader(
        CosmosClient cosmosClient,
        string databaseName,
        string containerName,
        string documentId,
        string partitionKey,
        HotReloadEventHandler<HotReloadEventArgs>? handler = null,
        string? connectionString = null)
        : base(handler, connectionString)
    {
        _cosmosClient = cosmosClient ?? throw new ArgumentNullException(nameof(cosmosClient));
        _container = _cosmosClient.GetContainer(databaseName, containerName);
        _documentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
        _partitionKey = partitionKey ?? throw new ArgumentNullException(nameof(partitionKey));
    }

    public override bool TryLoadKnownConfig(
        [NotNullWhen(true)] out RuntimeConfig? config,
        bool replaceEnvVar = false)
    {
        try
        {
            ItemResponse<ConfigDocument> response = _container
                .ReadItemAsync<ConfigDocument>(
                    _documentId, new PartitionKey(_partitionKey))
                .GetAwaiter()
                .GetResult();

            ConfigDocument doc = response.Resource;
            _lastETag = response.ETag;

            string json = JsonSerializer.Serialize(doc.ConfigData);

            DeserializationVariableReplacementSettings? settings =
                replaceEnvVar ? new DeserializationVariableReplacementSettings() : null;

            if (TryParseConfig(json, out RuntimeConfig? parsedConfig, settings, connectionString: _connectionString))
            {
                RuntimeConfig = parsedConfig;
                config = parsedConfig;

                if (RuntimeConfig.IsDevelopmentMode())
                {
                    SetupPollingTimer();
                }

                return true;
            }

            config = null;
            return false;
        }
        catch (CosmosException ex)
        {
            Console.Error.WriteLine($"Cosmos DB config load error ({ex.StatusCode}): {ex.Message}");
            config = null;
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected Cosmos DB config load error: {ex}");
            config = null;
            return false;
        }
    }

    private void SetupPollingTimer()
    {
        _pollingTimer ??= new Timer(
            callback: CheckForConfigChanges,
            state: null,
            dueTime: TimeSpan.FromSeconds(5),
            period: TimeSpan.FromSeconds(5));
    }

    private void CheckForConfigChanges(object? state)
    {
        try
        {
            ItemResponse<ConfigDocument> response = _container
                .ReadItemAsync<ConfigDocument>(
                    _documentId, new PartitionKey(_partitionKey))
                .GetAwaiter()
                .GetResult();

            if (!string.Equals(response.ETag, _lastETag, StringComparison.Ordinal))
            {
                _lastETag = response.ETag;
                HotReloadConfig(RuntimeConfig?.IsDevelopmentMode() ?? false);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Cosmos DB config polling error: {ex.Message}");
        }
    }

    /// <summary>
    /// Hot Reloads the runtime config when the polling timer
    /// detects a change to the underlying config document in CosmosDB.
    /// </summary>
    private void HotReloadConfig(bool isDevMode)
    {
        if (!TryLoadKnownConfig(out _, replaceEnvVar: true))
        {
            throw new DataApiBuilderException(
                message: "Deserialization of the configuration from CosmosDB failed.",
                statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        IsNewConfigDetected = true;
        IsNewConfigValidated = false;
        SignalConfigChanged();
    }

    public override string GetPublishedDraftSchemaLink()
        => "https://github.com/Azure/data-api-builder/releases/download/v{version}/dab.draft.schema.json";

    private sealed class ConfigDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("environment")]
        public string Environment { get; set; } = string.Empty;

        [JsonPropertyName("configData")]
        public JsonElement ConfigData { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("lastModified")]
        public DateTime? LastModified { get; set; }
    }
}
