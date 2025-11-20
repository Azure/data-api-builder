# Data API Builder: CosmosDB Configuration Storage - Feasibility Analysis

## Executive Summary

**Feasibility Rating: HIGH ✅**

This document analyzes the feasibility of modifying Azure Data API Builder (DAB) to read its runtime configuration from Azure CosmosDB instead of the file system. Based on the codebase analysis, this modification is **highly feasible** and can be implemented with moderate effort by extending the existing configuration loader architecture.

---

## Current Architecture Overview

### Configuration Loading System

Data API Builder uses an **abstract configuration loader pattern** that provides excellent extensibility:

```
RuntimeConfigLoader (abstract base class)
    ↓
FileSystemRuntimeConfigLoader (current implementation)
    ↓
RuntimeConfigProvider (consumer/facade)
```

**Key Components:**

1. **`RuntimeConfigLoader`** (`src/Config/RuntimeConfigLoader.cs`)
   - Abstract base class defining the configuration loading contract
   - Handles JSON deserialization, validation, and change notifications
   - Implements common functionality like environment variable replacement
   - **Key abstract method:** `TryLoadKnownConfig(out RuntimeConfig config, bool replaceEnvVar)`

2. **`FileSystemRuntimeConfigLoader`** (`src/Config/FileSystemRuntimeConfigLoader.cs`)
   - Current concrete implementation reading from file system
   - Supports hot-reload via `ConfigFileWatcher`
   - Handles environment-specific config files (e.g., `dab-config.Development.json`)
   - Manages config file path resolution

3. **`RuntimeConfigProvider`** (`src/Core/Configurations/RuntimeConfigProvider.cs`)
   - Wraps the loader and provides configuration to the rest of the application
   - Implements change token pattern for hot-reload support
   - Validates configuration during hot-reload scenarios

4. **Dependency Injection** (`src/Service/Startup.cs`, line 109)
   ```csharp
   FileSystemRuntimeConfigLoader configLoader = new(fileSystem, _hotReloadEventHandler, configFileName, connectionString);
   RuntimeConfigProvider configProvider = new(configLoader);
   services.AddSingleton(configLoader);
   services.AddSingleton(configProvider);
   ```

### Configuration Format

DAB uses JSON configuration files with the following structure:
- **Data Sources:** Database connection strings and types (SQL Server, PostgreSQL, MySQL, CosmosDB NoSQL, etc.)
- **Runtime Settings:** Host mode (development/production), authentication, telemetry, caching
- **Entities:** API entity definitions with permissions and mappings
- **Schema Validation:** JSON Schema validation via `dab.draft.schema.json`

---

## Proposed Solution: CosmosDbRuntimeConfigLoader

### Architecture

Create a new implementation: **`CosmosDbRuntimeConfigLoader : RuntimeConfigLoader`**

```
RuntimeConfigLoader (abstract)
    ├── FileSystemRuntimeConfigLoader (existing)
    └── CosmosDbRuntimeConfigLoader (NEW)
```

### Implementation Strategy

#### 1. **New Class: CosmosDbRuntimeConfigLoader**

**Location:** `src/Config/CosmosDbRuntimeConfigLoader.cs`

**Key Responsibilities:**
- Read configuration JSON from CosmosDB document
- Implement `TryLoadKnownConfig()` abstract method
- Support hot-reload via CosmosDB Change Feed
- Handle connection to CosmosDB using existing `CosmosClientProvider`

**Constructor Parameters:**
```csharp
public CosmosDbRuntimeConfigLoader(
    CosmosClient cosmosClient,
    HotReloadEventHandler<HotReloadEventArgs>? handler = null,
    string databaseName = "dab-config",
    string containerName = "configurations",
    string configDocumentId = "runtime-config",
    string? connectionString = null)
    : base(handler, connectionString)
```

**Core Implementation:**
```csharp
public override bool TryLoadKnownConfig(
    [NotNullWhen(true)] out RuntimeConfig? config, 
    bool replaceEnvVar = false)
{
    // 1. Fetch document from CosmosDB
    // 2. Extract JSON string from document
    // 3. Use existing TryParseConfig() from base class
    // 4. Set up change feed for hot-reload (if in development mode)
    // 5. Return parsed config
}
```

#### 2. **Configuration Storage Schema in CosmosDB**

**Database:** `dab-config` (configurable)  
**Container:** `configurations` (configurable)  
**Partition Key:** `/environment` (supports multiple environments)

**Document Structure:**
```json
{
  "id": "runtime-config",
  "environment": "production",
  "version": "1.0.0",
  "lastModified": "2025-11-21T10:30:00Z",
  "configData": {
    "$schema": "https://github.com/Azure/data-api-builder/releases/...",
    "data-source": { ... },
    "runtime": { ... },
    "entities": { ... }
  },
  "_ts": 1700567400
}
```

**Benefits:**
- `configData` contains the actual DAB configuration
- `environment` allows partition-based isolation
- `version` and `lastModified` support auditing
- `_ts` (timestamp) enables change tracking

#### 3. **Hot-Reload Support via Change Feed**

**Approach:** Use CosmosDB Change Feed Processor

```csharp
private void SetupChangeFeedProcessor()
{
    _changeFeedProcessor = _container
        .GetChangeFeedProcessorBuilder<ConfigDocument>(
            "dab-config-hotreload", 
            OnConfigChanged)
        .WithInstanceName("dab-instance")
        .WithLeaseContainer(_leaseContainer)
        .Build();
    
    await _changeFeedProcessor.StartAsync();
}

private async Task OnConfigChanged(
    ChangeFeedProcessorContext context,
    IReadOnlyCollection<ConfigDocument> changes,
    CancellationToken cancellationToken)
{
    // Trigger hot-reload similar to FileSystemWatcher
    HotReloadConfig(RuntimeConfig.IsDevelopmentMode());
}
```

**Alternative (Simpler):** Polling with ETag-based change detection
- Poll CosmosDB every N seconds (configurable interval)
- Use ETag to detect changes
- Lower resource usage than Change Feed for single-instance scenarios

#### 4. **Startup Integration**

**Modify:** `src/Service/Startup.cs`

**Option A: Environment Variable Toggle**
```csharp
string configSource = Configuration.GetValue<string>("DAB_CONFIG_SOURCE") ?? "FileSystem";

RuntimeConfigLoader configLoader = configSource switch
{
    "CosmosDB" => new CosmosDbRuntimeConfigLoader(
        cosmosClient: GetOrCreateCosmosClient(),
        handler: _hotReloadEventHandler,
        databaseName: Configuration.GetValue<string>("DAB_COSMOS_DATABASE"),
        containerName: Configuration.GetValue<string>("DAB_COSMOS_CONTAINER"),
        configDocumentId: Configuration.GetValue<string>("DAB_COSMOS_DOCUMENT_ID")
    ),
    "FileSystem" => new FileSystemRuntimeConfigLoader(
        fileSystem, 
        _hotReloadEventHandler, 
        configFileName, 
        connectionString
    ),
    _ => throw new NotSupportedException($"Config source '{configSource}' not supported")
};
```

**Option B: Configuration File Setting**
Add to `appsettings.json`:
```json
{
  "ConfigurationSource": {
    "Type": "CosmosDB",
    "CosmosDB": {
      "ConnectionString": "@env('DAB_COSMOS_CONNECTION_STRING')",
      "DatabaseName": "dab-config",
      "ContainerName": "configurations",
      "DocumentId": "runtime-config"
    }
  }
}
```

#### 5. **Environment Configuration**

**Environment Variables:**
```bash
DAB_CONFIG_SOURCE=CosmosDB
DAB_COSMOS_CONNECTION_STRING=AccountEndpoint=https://...;AccountKey=...
DAB_COSMOS_DATABASE=dab-config
DAB_COSMOS_CONTAINER=configurations
DAB_COSMOS_DOCUMENT_ID=runtime-config
DAB_COSMOS_PARTITION_KEY=production
```

---

## Implementation Steps

### Phase 1: Core Implementation (2-3 days)

1. **Create `CosmosDbRuntimeConfigLoader` class**
   - Implement `TryLoadKnownConfig()` method
   - Add CosmosDB document fetching logic
   - Reuse existing `TryParseConfig()` from base class
   - Add error handling and retry logic (use Polly policies like other DB operations)

2. **Add configuration schema**
   - Define document structure in CosmosDB
   - Create initialization scripts for database/container setup
   - Add partition key strategy

3. **Update dependency injection**
   - Modify `Startup.cs` to support loader selection
   - Add configuration options for CosmosDB source
   - Maintain backward compatibility with file system loader

### Phase 2: Hot-Reload Support (1-2 days)

4. **Implement change detection**
   - Choose between Change Feed Processor or ETag polling
   - Integrate with existing hot-reload event system
   - Test configuration updates trigger proper reloads

5. **Testing hot-reload scenarios**
   - Configuration updates
   - Connection failures and recovery
   - Multiple environment handling

### Phase 3: Tooling & Documentation (1-2 days)

6. **CLI tool updates** (Optional but recommended)
   - Extend `dab` CLI to support CosmosDB operations
   - Add commands: `dab config push`, `dab config pull`, `dab config validate`
   - Example: `dab config push --source cosmosdb --connection-string "..."`

7. **Migration utilities**
   - Script to migrate file-based config to CosmosDB
   - Validation tool to ensure config compatibility

8. **Documentation**
   - Update deployment guides
   - Add CosmosDB configuration examples
   - Document environment variables and settings

### Phase 4: Testing & Validation (2-3 days)

9. **Unit tests**
   - Mock CosmosDB client for loader tests
   - Test configuration parsing and validation
   - Test error scenarios (connection failures, malformed JSON)

10. **Integration tests**
    - Test with real CosmosDB instance
    - Validate hot-reload functionality
    - Test multi-environment scenarios

11. **End-to-end testing**
    - Deploy to Azure Container Apps with CosmosDB config
    - Validate all DAB features work correctly
    - Performance testing (config load times, hot-reload latency)

---

## Technical Considerations

### Advantages

✅ **Centralized Configuration Management**
- Single source of truth for multiple DAB instances
- Easier configuration updates across environments
- Better auditing and version control

✅ **Built-in Redundancy**
- CosmosDB's global distribution and high availability
- Automatic backups and point-in-time restore
- No file system dependencies

✅ **Dynamic Updates**
- Hot-reload without file system access
- Instant propagation to multiple instances
- Change Feed for real-time notifications

✅ **Environment Isolation**
- Partition-based separation (dev/staging/prod)
- Role-based access control via CosmosDB RBAC
- Fine-grained permissions

✅ **Cloud-Native Architecture**
- Aligns with microservices patterns
- Better for containerized/serverless deployments
- Reduced container image complexity (no config files needed)

### Challenges & Mitigations

⚠️ **Dependency on CosmosDB Availability**
- **Risk:** DAB won't start if CosmosDB is unavailable
- **Mitigation:** 
  - Implement local cache/fallback config
  - Add retry logic with exponential backoff
  - Support hybrid mode (CosmosDB + local file backup)

⚠️ **Latency on Startup**
- **Risk:** Network calls add startup time
- **Mitigation:**
  - Cache config in memory after first load
  - Use Direct connection mode for CosmosDB
  - Async initialization pattern

⚠️ **Cost Considerations**
- **Risk:** Additional CosmosDB costs (RU/s, storage)
- **Mitigation:**
  - Configuration is small (typically < 100KB)
  - Minimal RU consumption (< 10 RU/s for typical scenarios)
  - Use serverless tier for low-volume scenarios
  - Estimated cost: < $5/month for typical usage

⚠️ **Debugging Complexity**
- **Risk:** Harder to debug config issues vs. local files
- **Mitigation:**
  - Add verbose logging for config operations
  - Provide CLI tools to inspect CosmosDB config
  - Support exporting config to file for inspection

⚠️ **Hot-Reload Propagation Delay**
- **Risk:** Change Feed or polling introduces delay
- **Mitigation:**
  - Change Feed typically < 1 second latency
  - Polling interval configurable (default 5 seconds)
  - Document expected propagation times

### Security Considerations

🔒 **Connection String Management**
- Use Azure Key Vault for connection strings
- Support Managed Identity authentication
- Environment variable-based configuration (already supported)

🔒 **Access Control**
- CosmosDB RBAC for read/write permissions
- Separate read-only keys for production instances
- Audit logs via CosmosDB diagnostic settings

🔒 **Configuration Validation**
- Maintain JSON schema validation
- Validate before writing to CosmosDB
- Prevent invalid configs from being stored

---

## Alternative Approaches Considered

### 1. **Azure App Configuration Service**
- **Pros:** Purpose-built for configuration, supports key-value pairs, feature flags
- **Cons:** JSON config would need to be split into key-value pairs, less flexible
- **Verdict:** CosmosDB is better for storing complete JSON documents

### 2. **Azure Blob Storage**
- **Pros:** Simple, cheap, familiar for file-like storage
- **Cons:** No native change notifications, would need Event Grid setup
- **Verdict:** More complex change detection than CosmosDB Change Feed

### 3. **Azure SQL Database**
- **Pros:** Relational structure, strong consistency
- **Cons:** Overkill for JSON storage, more expensive
- **Verdict:** CosmosDB more appropriate for document storage

### 4. **Redis Cache**
- **Pros:** Very fast, pub/sub for change notifications
- **Cons:** Not designed for durable storage, requires backup strategy
- **Verdict:** Good as cache layer, not primary storage

**Recommendation:** CosmosDB is the optimal choice given DAB's existing CosmosDB integration and document-based config structure.

---

## Deployment Scenarios

### Scenario 1: Azure Container Apps
```yaml
# Container Apps Environment Variables
- name: DAB_CONFIG_SOURCE
  value: "CosmosDB"
- name: DAB_COSMOS_CONNECTION_STRING
  secretRef: cosmos-connection-string
- name: DAB_COSMOS_DATABASE
  value: "dab-config"
- name: DAB_COSMOS_CONTAINER
  value: "configurations"
```

### Scenario 2: Kubernetes
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: dab-config
data:
  DAB_CONFIG_SOURCE: "CosmosDB"
  DAB_COSMOS_DATABASE: "dab-config"
  DAB_COSMOS_CONTAINER: "configurations"
---
apiVersion: v1
kind: Secret
metadata:
  name: dab-secrets
type: Opaque
data:
  cosmos-connection-string: <base64-encoded>
```

### Scenario 3: Docker Compose
```yaml
services:
  dab:
    image: mcr.microsoft.com/azure-databases/data-api-builder
    environment:
      - DAB_CONFIG_SOURCE=CosmosDB
      - DAB_COSMOS_CONNECTION_STRING=${COSMOS_CONNECTION_STRING}
      - DAB_COSMOS_DATABASE=dab-config
      - DAB_COSMOS_CONTAINER=configurations
```

---

## Cost Analysis

### CosmosDB Costs (Estimated)

**Configuration Storage:**
- Average config size: 50KB
- Number of configs: 3 (dev, staging, prod)
- Total storage: 0.15KB = **negligible cost**

**Request Units (RU/s):**
- Startup read: 5 RU (once per instance)
- Hot-reload polling: 1 RU every 5 seconds = 17,280 RU/day
- Change Feed: ~10 RU/hour = 240 RU/day
- **Estimated:** 400 RU/s provisioned = ~$24/month
- **With Serverless:** < $1/month for low-traffic scenarios

**Comparison:**
- Azure App Configuration: ~$1.20/day = $36/month
- CosmosDB Serverless: < $1/month
- **Verdict:** CosmosDB is cost-effective

---

## Risk Assessment

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| CosmosDB unavailability | Low | High | Local cache, fallback config, health checks |
| Configuration corruption | Low | High | Schema validation, versioning, rollback capability |
| Hot-reload failures | Medium | Medium | Error handling, retry logic, alerts |
| Performance degradation | Low | Low | Caching, connection pooling, monitoring |
| Increased operational complexity | Medium | Low | Documentation, tooling, training |
| Cost overruns | Low | Low | Serverless tier, monitoring, alerts |

**Overall Risk Level:** **LOW-MEDIUM** with proper mitigations

---

## Recommendations

### Immediate Actions

1. ✅ **Proceed with Implementation** - The solution is architecturally sound and low-risk
2. 🔧 **Start with Phase 1** - Core implementation with simple polling-based change detection
3. 📝 **Create Proof of Concept** - Implement basic CosmosDB loader in 1-2 days
4. 🧪 **Test Thoroughly** - Validate against existing test suite

### Implementation Approach

**Recommended:** **Incremental Rollout**
1. Implement basic loader without hot-reload
2. Add hot-reload with polling mechanism
3. Optional: Upgrade to Change Feed for production
4. Add CLI tooling and migration utilities

**Timeline:** **1-2 weeks for complete implementation**

### Best Practices

- ✅ Maintain backward compatibility with file system loader
- ✅ Use feature flags to enable/disable CosmosDB config source
- ✅ Implement comprehensive logging for config operations
- ✅ Add health checks for CosmosDB connectivity
- ✅ Document all configuration options and environment variables
- ✅ Provide migration scripts and documentation

### Production Readiness Checklist

- [ ] Unit tests for `CosmosDbRuntimeConfigLoader`
- [ ] Integration tests with real CosmosDB instance
- [ ] Performance benchmarks (startup time, hot-reload latency)
- [ ] Security review (connection strings, RBAC, encryption)
- [ ] Documentation (deployment guides, troubleshooting)
- [ ] Monitoring and alerting setup
- [ ] Rollback procedure documented
- [ ] Cost monitoring and alerts configured

---

## Conclusion

**Implementing CosmosDB-based configuration storage for Data API Builder is highly feasible and recommended.** The existing architecture's extensibility through the `RuntimeConfigLoader` abstraction makes this modification straightforward. The benefits of centralized configuration management, high availability, and cloud-native architecture outweigh the moderate implementation effort and low operational risks.

**Expected Effort:** 1-2 weeks (including testing and documentation)  
**Risk Level:** Low-Medium (with mitigations)  
**Recommended Approach:** Incremental implementation with polling-based hot-reload  
**Go/No-Go Decision:** **✅ GO** - Proceed with implementation

---

## Next Steps

1. **Approval:** Review and approve this feasibility analysis
2. **Proof of Concept:** Implement basic `CosmosDbRuntimeConfigLoader` (1-2 days)
3. **Testing:** Validate with sample configurations and real CosmosDB
4. **Implementation:** Follow phased approach outlined above
5. **Documentation:** Update deployment guides and README
6. **Deployment:** Roll out to development environment first
7. **Production:** Deploy to staging, then production with monitoring

---

## Appendix: Code Snippets

### A. Basic CosmosDbRuntimeConfigLoader Implementation

```csharp
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
        _cosmosClient = cosmosClient;
        _container = _cosmosClient.GetContainer(databaseName, containerName);
        _documentId = documentId;
        _partitionKey = partitionKey;
    }

    public override bool TryLoadKnownConfig(
        [NotNullWhen(true)] out RuntimeConfig? config, 
        bool replaceEnvVar = false)
    {
        try
        {
            // Fetch document from CosmosDB
            ItemResponse<ConfigDocument> response = await _container.ReadItemAsync<ConfigDocument>(
                _documentId,
                new PartitionKey(_partitionKey)
            );

            ConfigDocument doc = response.Resource;
            _lastETag = response.ETag;

            // Convert to JSON string
            string json = JsonSerializer.Serialize(doc.ConfigData);

            // Use base class parsing logic
            DeserializationVariableReplacementSettings? settings = replaceEnvVar 
                ? new DeserializationVariableReplacementSettings() 
                : null;

            if (TryParseConfig(json, out RuntimeConfig parsedConfig, settings, connectionString: _connectionString))
            {
                RuntimeConfig = parsedConfig;
                config = parsedConfig;
                
                // Setup hot-reload if in development mode
                if (RuntimeConfig.IsDevelopmentMode())
                {
                    SetupPollingTimer();
                }
                
                return true;
            }

            config = null;
            return false;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            Console.Error.WriteLine($"Configuration document not found: {_documentId}");
            config = null;
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error loading config from CosmosDB: {ex.Message}");
            config = null;
            return false;
        }
    }

    private void SetupPollingTimer()
    {
        _pollingTimer = new Timer(CheckForConfigChanges, null, 
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private async void CheckForConfigChanges(object? state)
    {
        try
        {
            ItemResponse<ConfigDocument> response = await _container.ReadItemAsync<ConfigDocument>(
                _documentId,
                new PartitionKey(_partitionKey)
            );

            if (response.ETag != _lastETag)
            {
                _lastETag = response.ETag;
                HotReloadConfig(RuntimeConfig?.IsDevelopmentMode() ?? false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking for config changes: {ex.Message}");
        }
    }

    public override string GetPublishedDraftSchemaLink()
    {
        // Return schema URL (same as FileSystemRuntimeConfigLoader)
        return "https://github.com/Azure/data-api-builder/releases/download/v{version}/dab.draft.schema.json";
    }

    private class ConfigDocument
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
```

### B. Startup.cs Modifications

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // ... existing code ...

    string configSource = Configuration.GetValue<string>("ConfigSource") ?? "FileSystem";
    RuntimeConfigLoader configLoader;

    switch (configSource.ToLower())
    {
        case "cosmosdb":
            string cosmosConnectionString = Configuration.GetValue<string>("CosmosDB:ConnectionString")
                ?? throw new InvalidOperationException("CosmosDB connection string not configured");
            string databaseName = Configuration.GetValue<string>("CosmosDB:DatabaseName") ?? "dab-config";
            string containerName = Configuration.GetValue<string>("CosmosDB:ContainerName") ?? "configurations";
            string documentId = Configuration.GetValue<string>("CosmosDB:DocumentId") ?? "runtime-config";
            string partitionKey = Configuration.GetValue<string>("CosmosDB:PartitionKey") ?? "default";

            CosmosClient cosmosClient = new CosmosClient(cosmosConnectionString);
            configLoader = new CosmosDbRuntimeConfigLoader(
                cosmosClient,
                databaseName,
                containerName,
                documentId,
                partitionKey,
                _hotReloadEventHandler,
                connectionString
            );
            break;

        case "filesystem":
        default:
            IFileSystem fileSystem = new FileSystem();
            configLoader = new FileSystemRuntimeConfigLoader(
                fileSystem,
                _hotReloadEventHandler,
                configFileName,
                connectionString
            );
            services.AddSingleton(fileSystem);
            break;
    }

    services.AddSingleton(configLoader);
    RuntimeConfigProvider configProvider = new(configLoader);
    services.AddSingleton(configProvider);

    // ... rest of existing code ...
}
```

---

**Document Version:** 1.0  
**Date:** November 21, 2025  
**Author:** GitHub Copilot Analysis  
**Status:** Ready for Review and Approval
