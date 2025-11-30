// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Config;

public class CosmosDbRuntimeConfigLoader : RuntimeConfigLoader, IDisposable
{
    private readonly CosmosClient _cosmosClient;
    private readonly Container _container;
    private readonly string _documentId;
    private readonly string _partitionKey;
    private readonly ILogger<CosmosDbRuntimeConfigLoader>? _logger;
    private string? _lastETag;
    private Timer? _pollingTimer;
    private bool _disposed;

    /// <summary>
    /// Default polling interval for checking configuration changes in CosmosDB.
    /// Used only when in development mode.
    /// </summary>
    private const int DEFAULT_POLLING_INTERVAL_SECONDS = 5;

    public CosmosDbRuntimeConfigLoader(
        CosmosClient cosmosClient,
        string databaseName,
        string containerName,
        string documentId,
        string partitionKey,
        HotReloadEventHandler<HotReloadEventArgs>? handler = null,
        string? connectionString = null,
        ILogger<CosmosDbRuntimeConfigLoader>? logger = null)
        : base(handler, connectionString)
    {
        _cosmosClient = cosmosClient ?? throw new ArgumentNullException(nameof(cosmosClient));
        _container = _cosmosClient.GetContainer(databaseName, containerName);
        _documentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
        _partitionKey = partitionKey ?? throw new ArgumentNullException(nameof(partitionKey));
        _logger = logger;
    }

    public override async Task<RuntimeConfig?> LoadKnownConfigAsync(
        bool replaceEnvVar = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            ItemResponse<ConfigDocument> response = await ReadConfigDocumentAsync(cancellationToken).ConfigureAwait(false);

            ConfigDocument doc = response.Resource;
            doc.Validate();
            _lastETag = response.ETag;

            // Use GetRawText() to preserve the original JSON structure for proper converter processing
            string json = doc.ConfigData.GetRawText();
            Console.WriteLine($"[CosmosLoader] DEBUG: Raw JSON connection-string before parse: {(json.Contains("@env") ? "contains @env" : "no @env found")}");
            Console.WriteLine($"[CosmosLoader] DEBUG: replaceEnvVar = {replaceEnvVar}");

            DeserializationVariableReplacementSettings? settings =
                replaceEnvVar ? new DeserializationVariableReplacementSettings(doReplaceEnvVar: true) : null;
            Console.WriteLine($"[CosmosLoader] DEBUG: settings created: {settings != null}, DoReplaceEnvVar: {settings?.DoReplaceEnvVar}");

            if (TryParseConfig(json, out RuntimeConfig? parsedConfig, settings, connectionString: _connectionString))
            {
                Console.WriteLine($"[CosmosLoader] DEBUG: After parse, connection-string = {parsedConfig.DataSource.ConnectionString?[..Math.Min(50, parsedConfig.DataSource.ConnectionString?.Length ?? 0)]}");
                RuntimeConfig = parsedConfig;

                if (RuntimeConfig.IsDevelopmentMode())
                {
                    SetupPollingTimer();
                }

                return parsedConfig;
            }

            return null;
        }
        catch (CosmosException ex)
        {
            _logger?.LogError(ex, "Cosmos DB config load error ({StatusCode}): {Message}", ex.StatusCode, ex.Message);
            Console.Error.WriteLine($"Cosmos DB config load error ({ex.StatusCode}): {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected Cosmos DB config load error");
            Console.Error.WriteLine($"Unexpected Cosmos DB config load error: {ex}");
            return null;
        }
    }

    private void SetupPollingTimer()
    {
        if (_disposed)
        {
            return;
        }

        _pollingTimer ??= new Timer(
            callback: CheckForConfigChanges,
            state: null,
            dueTime: TimeSpan.FromSeconds(DEFAULT_POLLING_INTERVAL_SECONDS),
            period: TimeSpan.FromSeconds(DEFAULT_POLLING_INTERVAL_SECONDS));
    }

    private async void CheckForConfigChanges(object? state)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            ItemResponse<ConfigDocument> response = await _container
                .ReadItemAsync<ConfigDocument>(_documentId, new PartitionKey(_partitionKey))
                .ConfigureAwait(false);

            if (!string.Equals(response.ETag, _lastETag, StringComparison.Ordinal))
            {
                _lastETag = response.ETag;
                await HotReloadConfigAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Cosmos DB config polling error: {Message}", ex.Message);
            Console.Error.WriteLine($"Cosmos DB config polling error: {ex.Message}");
        }
    }

    /// <summary>
    /// Hot Reloads the runtime config when the polling timer
    /// detects a change to the underlying config document in CosmosDB.
    /// </summary>
    private async Task HotReloadConfigAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (await LoadKnownConfigAsync(replaceEnvVar: true).ConfigureAwait(false) is null)
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

    private async Task<ItemResponse<ConfigDocument>> ReadConfigDocumentAsync(CancellationToken cancellationToken)
    {
        // Add a 30-second timeout to prevent indefinite hangs
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        
        try
        {
            Console.WriteLine($"[CosmosLoader] Calling ReadItemAsync...");
            var response = await _container.ReadItemAsync<ConfigDocument>(
                _documentId,
                new PartitionKey(_partitionKey),
                cancellationToken: linkedCts.Token).ConfigureAwait(false);
            Console.WriteLine($"[CosmosLoader] ReadItemAsync completed successfully");
            return response;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            Console.Error.WriteLine($"[CosmosLoader] Read operation timed out after 30 seconds!");
            Console.Error.WriteLine($"[CosmosLoader] This likely indicates a connectivity issue with the Cosmos DB endpoint.");
            Console.Error.WriteLine($"[CosmosLoader] Container endpoint: {_container.Database.Client.Endpoint}");
            throw new TimeoutException($"Cosmos DB read operation timed out after 30 seconds. Check connectivity to {_container.Database.Client.Endpoint}");
        }
    }

    public override string GetPublishedDraftSchemaLink()
    {
        string? assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        if (assemblyDirectory is null)
        {
            throw new DataApiBuilderException(
                message: "Could not get the link for DAB draft schema.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        string schemaPath = Path.Combine(assemblyDirectory, "dab.draft.schema.json");
        string schemaFileContent = File.ReadAllText(schemaPath);
        Dictionary<string, object>? jsonDictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(schemaFileContent, GetSerializationOptions(replacementSettings: null));

        if (jsonDictionary is null)
        {
            throw new DataApiBuilderException(
                message: "The schema file is misconfigured. Please check the file formatting.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        if (!jsonDictionary.TryGetValue("$id", out object? id))
        {
            throw new DataApiBuilderException(
                message: "The schema file doesn't have the required field : $id",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        return id.ToString()!;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _pollingTimer?.Dispose();
            _pollingTimer = null;
            _cosmosClient.Dispose();
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CosmosDbRuntimeConfigLoader));
        }
    }

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

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id))
            {
                throw new InvalidOperationException("ConfigDocument.Id is required");
            }

            if (string.IsNullOrWhiteSpace(Environment))
            {
                throw new InvalidOperationException("ConfigDocument.Environment is required");
            }

            if (ConfigData.ValueKind == JsonValueKind.Undefined || ConfigData.ValueKind == JsonValueKind.Null)
            {
                throw new InvalidOperationException("ConfigDocument.ConfigData is required");
            }
        }
    }
}
