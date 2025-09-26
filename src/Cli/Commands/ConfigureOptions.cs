// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Product;
using Cli.Constants;
using CommandLine;
using Microsoft.Extensions.Logging;
using Serilog;
using static Cli.Utils;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Cli.Commands
{
    /// <summary>
    /// Configure command options
    /// This command will be used to configure non-entity config properties.
    /// </summary>
    [Verb("configure", isDefault: false, HelpText = "Configure non-entity config properties", Hidden = false)]
    public class ConfigureOptions : Options
    {
        public ConfigureOptions(
            string? dataSourceDatabaseType = null,
            string? dataSourceConnectionString = null,
            string? dataSourceOptionsDatabase = null,
            string? dataSourceOptionsContainer = null,
            string? dataSourceOptionsSchema = null,
            bool? dataSourceOptionsSetSessionContext = null,
            int? depthLimit = null,
            bool? runtimeGraphQLEnabled = null,
            string? runtimeGraphQLPath = null,
            bool? runtimeGraphQLAllowIntrospection = null,
            bool? runtimeGraphQLMultipleMutationsCreateEnabled = null,
            bool? runtimeRestEnabled = null,
            string? runtimeRestPath = null,
            bool? runtimeRestRequestBodyStrict = null,
            bool? runtimeMcpEnabled = null,
            string? runtimeMcpPath = null,
            bool? runtimeMcpDmlToolsEnabled = null,
            bool? runtimeMcpDmlToolsDescribeEntitiesEnabled = null,
            bool? runtimeMcpDmlToolsCreateRecordEnabled = null,
            bool? runtimeMcpDmlToolsReadRecordsEnabled = null,
            bool? runtimeMcpDmlToolsUpdateRecordEnabled = null,
            bool? runtimeMcpDmlToolsDeleteRecordEnabled = null,
            bool? runtimeMcpDmlToolsExecuteEntityEnabled = null,
            bool? runtimeCacheEnabled = null,
            int? runtimeCacheTtl = null,
            HostMode? runtimeHostMode = null,
            IEnumerable<string>? runtimeHostCorsOrigins = null,
            bool? runtimeHostCorsAllowCredentials = null,
            string? runtimeHostAuthenticationProvider = null,
            string? runtimeHostAuthenticationJwtAudience = null,
            string? runtimeHostAuthenticationJwtIssuer = null,
            string? azureKeyVaultEndpoint = null,
            AKVRetryPolicyMode? azureKeyVaultRetryPolicyMode = null,
            int? azureKeyVaultRetryPolicyMaxCount = null,
            int? azureKeyVaultRetryPolicyDelaySeconds = null,
            int? azureKeyVaultRetryPolicyMaxDelaySeconds = null,
            int? azureKeyVaultRetryPolicyNetworkTimeoutSeconds = null,
            CliBool? azureLogAnalyticsEnabled = null,
            string? azureLogAnalyticsDabIdentifier = null,
            int? azureLogAnalyticsFlushIntervalSeconds = null,
            string? azureLogAnalyticsCustomTableName = null,
            string? azureLogAnalyticsDcrImmutableId = null,
            string? azureLogAnalyticsDceEndpoint = null,
            CliBool? fileSinkEnabled = null,
            string? fileSinkPath = null,
            RollingInterval? fileSinkRollingInterval = null,
            int? fileSinkRetainedFileCountLimit = null,
            long? fileSinkFileSizeLimitBytes = null,
            string? config = null)
            : base(config)
        {
            // Data Source
            DataSourceDatabaseType = dataSourceDatabaseType;
            DataSourceConnectionString = dataSourceConnectionString;
            DataSourceOptionsDatabase = dataSourceOptionsDatabase;
            DataSourceOptionsContainer = dataSourceOptionsContainer;
            DataSourceOptionsSchema = dataSourceOptionsSchema;
            DataSourceOptionsSetSessionContext = dataSourceOptionsSetSessionContext;
            // GraphQL
            DepthLimit = depthLimit;
            RuntimeGraphQLEnabled = runtimeGraphQLEnabled;
            RuntimeGraphQLPath = runtimeGraphQLPath;
            RuntimeGraphQLAllowIntrospection = runtimeGraphQLAllowIntrospection;
            RuntimeGraphQLMultipleMutationsCreateEnabled = runtimeGraphQLMultipleMutationsCreateEnabled;
            // Rest
            RuntimeRestEnabled = runtimeRestEnabled;
            RuntimeRestPath = runtimeRestPath;
            RuntimeRestRequestBodyStrict = runtimeRestRequestBodyStrict;
            // Mcp
            RuntimeMcpEnabled = runtimeMcpEnabled;
            RuntimeMcpPath = runtimeMcpPath;
            RuntimeMcpDmlToolsEnabled = runtimeMcpDmlToolsEnabled;
            RuntimeMcpDmlToolsDescribeEntitiesEnabled = runtimeMcpDmlToolsDescribeEntitiesEnabled;
            RuntimeMcpDmlToolsCreateRecordEnabled = runtimeMcpDmlToolsCreateRecordEnabled;
            RuntimeMcpDmlToolsReadRecordsEnabled = runtimeMcpDmlToolsReadRecordsEnabled;
            RuntimeMcpDmlToolsUpdateRecordEnabled = runtimeMcpDmlToolsUpdateRecordEnabled;
            RuntimeMcpDmlToolsDeleteRecordEnabled = runtimeMcpDmlToolsDeleteRecordEnabled;
            RuntimeMcpDmlToolsExecuteEntityEnabled = runtimeMcpDmlToolsExecuteEntityEnabled;
            // Cache
            RuntimeCacheEnabled = runtimeCacheEnabled;
            RuntimeCacheTTL = runtimeCacheTtl;
            // Host
            RuntimeHostMode = runtimeHostMode;
            RuntimeHostCorsOrigins = runtimeHostCorsOrigins;
            RuntimeHostCorsAllowCredentials = runtimeHostCorsAllowCredentials;
            RuntimeHostAuthenticationProvider = runtimeHostAuthenticationProvider;
            RuntimeHostAuthenticationJwtAudience = runtimeHostAuthenticationJwtAudience;
            RuntimeHostAuthenticationJwtIssuer = runtimeHostAuthenticationJwtIssuer;
            // Azure Key Vault
            AzureKeyVaultEndpoint = azureKeyVaultEndpoint;
            AzureKeyVaultRetryPolicyMode = azureKeyVaultRetryPolicyMode;
            AzureKeyVaultRetryPolicyMaxCount = azureKeyVaultRetryPolicyMaxCount;
            AzureKeyVaultRetryPolicyDelaySeconds = azureKeyVaultRetryPolicyDelaySeconds;
            AzureKeyVaultRetryPolicyMaxDelaySeconds = azureKeyVaultRetryPolicyMaxDelaySeconds;
            AzureKeyVaultRetryPolicyNetworkTimeoutSeconds = azureKeyVaultRetryPolicyNetworkTimeoutSeconds;
            // Azure Log Analytics
            AzureLogAnalyticsEnabled = azureLogAnalyticsEnabled;
            AzureLogAnalyticsDabIdentifier = azureLogAnalyticsDabIdentifier;
            AzureLogAnalyticsFlushIntervalSeconds = azureLogAnalyticsFlushIntervalSeconds;
            AzureLogAnalyticsCustomTableName = azureLogAnalyticsCustomTableName;
            AzureLogAnalyticsDcrImmutableId = azureLogAnalyticsDcrImmutableId;
            AzureLogAnalyticsDceEndpoint = azureLogAnalyticsDceEndpoint;
            // File
            FileSinkEnabled = fileSinkEnabled;
            FileSinkPath = fileSinkPath;
            FileSinkRollingInterval = fileSinkRollingInterval;
            FileSinkRetainedFileCountLimit = fileSinkRetainedFileCountLimit;
            FileSinkFileSizeLimitBytes = fileSinkFileSizeLimitBytes;
        }

        [Option("data-source.database-type", Required = false, HelpText = "Database type. Allowed values: MSSQL, PostgreSQL, CosmosDB_NoSQL, MySQL.")]
        public string? DataSourceDatabaseType { get; }

        [Option("data-source.connection-string", Required = false, HelpText = "Connection string for the data source.")]
        public string? DataSourceConnectionString { get; }

        [Option("data-source.options.database", Required = false, HelpText = "Database name for Cosmos DB for NoSql.")]
        public string? DataSourceOptionsDatabase { get; }

        [Option("data-source.options.container", Required = false, HelpText = "Container name for Cosmos DB for NoSql.")]
        public string? DataSourceOptionsContainer { get; }

        [Option("data-source.options.schema", Required = false, HelpText = "Schema path for Cosmos DB for NoSql.")]
        public string? DataSourceOptionsSchema { get; }

        [Option("data-source.options.set-session-context", Required = false, HelpText = "Enable session context. Allowed values: true (default), false.")]
        public bool? DataSourceOptionsSetSessionContext { get; }

        [Option("runtime.graphql.depth-limit", Required = false, HelpText = "Max allowed depth of the nested query. Allowed values: (0,2147483647] inclusive. Default is infinity. Use -1 to remove limit.")]
        public int? DepthLimit { get; }

        [Option("runtime.graphql.enabled", Required = false, HelpText = "Enable DAB's GraphQL endpoint. Default: true (boolean).")]
        public bool? RuntimeGraphQLEnabled { get; }

        [Option("runtime.graphql.path", Required = false, HelpText = "Customize DAB's GraphQL endpoint path. Allowed values: string. Conditions: Prefix with '/', no spaces and no reserved characters.")]
        public string? RuntimeGraphQLPath { get; }

        [Option("runtime.graphql.allow-introspection", Required = false, HelpText = "Allow/Deny GraphQL introspection requests in GraphQL Schema. Default: true (boolean).")]
        public bool? RuntimeGraphQLAllowIntrospection { get; }

        [Option("runtime.graphql.multiple-mutations.create.enabled", Required = false, HelpText = "Enable/Disable multiple-mutation create operations on DAB's generated GraphQL schema. Default: true (boolean).")]
        public bool? RuntimeGraphQLMultipleMutationsCreateEnabled { get; }

        [Option("runtime.rest.enabled", Required = false, HelpText = "Enable DAB's Rest endpoint. Default: true (boolean).")]
        public bool? RuntimeRestEnabled { get; }

        [Option("runtime.rest.path", Required = false, HelpText = "Customize DAB's REST endpoint path. Default: '/api' Conditions: Prefix path with '/'.")]
        public string? RuntimeRestPath { get; }

        [Option("runtime.rest.request-body-strict", Required = false, HelpText = "Prohibit extraneous REST request body fields. Default: true (boolean).")]
        public bool? RuntimeRestRequestBodyStrict { get; }

        [Option("runtime.mcp.enabled", Required = false, HelpText = "Enable DAB's MCP endpoint. Default: true (boolean).")]
        public bool? RuntimeMcpEnabled { get; }

        [Option("runtime.mcp.path", Required = false, HelpText = "Customize DAB's MCP endpoint path. Default: '/mcp' Conditions: Prefix path with '/'.")]
        public string? RuntimeMcpPath { get; }

        [Option("runtime.mcp.dml-tools.enabled", Required = false, HelpText = "Enable DAB's MCP DML tools endpoint. Default: true (boolean).")]
        public bool? RuntimeMcpDmlToolsEnabled { get; }

        [Option("runtime.mcp.dml-tools.describe-entities.enabled", Required = false, HelpText = "Enable DAB's MCP describe entities tool. Default: true (boolean).")]
        public bool? RuntimeMcpDmlToolsDescribeEntitiesEnabled { get; }

        [Option("runtime.mcp.dml-tools.create-record.enabled", Required = false, HelpText = "Enable DAB's MCP create record tool. Default: true (boolean).")]
        public bool? RuntimeMcpDmlToolsCreateRecordEnabled { get; }

        [Option("runtime.mcp.dml-tools.read-records.enabled", Required = false, HelpText = "Enable DAB's MCP read record tool. Default: true (boolean).")]
        public bool? RuntimeMcpDmlToolsReadRecordsEnabled { get; }

        [Option("runtime.mcp.dml-tools.update-record.enabled", Required = false, HelpText = "Enable DAB's MCP update record tool. Default: true (boolean).")]
        public bool? RuntimeMcpDmlToolsUpdateRecordEnabled { get; }

        [Option("runtime.mcp.dml-tools.delete-record.enabled", Required = false, HelpText = "Enable DAB's MCP delete record tool. Default: true (boolean).")]
        public bool? RuntimeMcpDmlToolsDeleteRecordEnabled { get; }

        [Option("runtime.mcp.dml-tools.execute-entity.enabled", Required = false, HelpText = "Enable DAB's MCP execute entity tool. Default: true (boolean).")]
        public bool? RuntimeMcpDmlToolsExecuteEntityEnabled { get; }

        [Option("runtime.cache.enabled", Required = false, HelpText = "Enable DAB's cache globally. (You must also enable each entity's cache separately.). Default: false (boolean).")]
        public bool? RuntimeCacheEnabled { get; }

        [Option("runtime.cache.ttl-seconds", Required = false, HelpText = "Customize the DAB cache's global default time to live in seconds. Default: 5 seconds (Integer).")]
        public int? RuntimeCacheTTL { get; }

        [Option("runtime.host.mode", Required = false, HelpText = "Set the host running mode of DAB in Development or Production. Default: Development.")]
        public HostMode? RuntimeHostMode { get; }

        [Option("runtime.host.cors.origins", Required = false, HelpText = "Overwrite Allowed Origins in CORS. Default: [] (Space separated array of strings).")]
        public IEnumerable<string>? RuntimeHostCorsOrigins { get; }

        [Option("runtime.host.cors.allow-credentials", Required = false, HelpText = "Set value for Access-Control-Allow-Credentials header in Host.Cors. Default: false (boolean).")]
        public bool? RuntimeHostCorsAllowCredentials { get; }

        [Option("runtime.host.authentication.provider", Required = false, HelpText = "Configure the name of authentication provider. Default: StaticWebApps.")]
        public string? RuntimeHostAuthenticationProvider { get; }

        [Option("runtime.host.authentication.jwt.audience", Required = false, HelpText = "Configure the intended recipient(s) of the Jwt Token.")]
        public string? RuntimeHostAuthenticationJwtAudience { get; }

        [Option("runtime.host.authentication.jwt.issuer", Required = false, HelpText = "Configure the entity that issued the Jwt Token.")]
        public string? RuntimeHostAuthenticationJwtIssuer { get; }

        [Option("azure-key-vault.endpoint", Required = false, HelpText = "Configure the Azure Key Vault endpoint URL.")]
        public string? AzureKeyVaultEndpoint { get; }

        [Option("azure-key-vault.retry-policy.mode", Required = false, HelpText = "Configure the retry policy mode. Allowed values: fixed, exponential. Default: exponential.")]
        public AKVRetryPolicyMode? AzureKeyVaultRetryPolicyMode { get; }

        [Option("azure-key-vault.retry-policy.max-count", Required = false, HelpText = "Configure the maximum number of retry attempts. Default: 3.")]
        public int? AzureKeyVaultRetryPolicyMaxCount { get; }

        [Option("azure-key-vault.retry-policy.delay-seconds", Required = false, HelpText = "Configure the initial delay between retries in seconds. Default: 1.")]
        public int? AzureKeyVaultRetryPolicyDelaySeconds { get; }

        [Option("azure-key-vault.retry-policy.max-delay-seconds", Required = false, HelpText = "Configure the maximum delay between retries in seconds (for exponential mode). Default: 60.")]
        public int? AzureKeyVaultRetryPolicyMaxDelaySeconds { get; }

        [Option("azure-key-vault.retry-policy.network-timeout-seconds", Required = false, HelpText = "Configure the network timeout for requests in seconds. Default: 60.")]
        public int? AzureKeyVaultRetryPolicyNetworkTimeoutSeconds { get; }

        [Option("runtime.telemetry.azure-log-analytics.enabled", Required = false, HelpText = "Enable/Disable Azure Log Analytics. Default: False (boolean)")]
        public CliBool? AzureLogAnalyticsEnabled { get; }

        [Option("runtime.telemetry.azure-log-analytics.dab-identifier", Required = false, HelpText = "Configure DAB Identifier to allow user to differentiate which logs come from DAB in Azure Log Analytics . Default: DABLogs")]
        public string? AzureLogAnalyticsDabIdentifier { get; }

        [Option("runtime.telemetry.azure-log-analytics.flush-interval-seconds", Required = false, HelpText = "Configure Flush Interval in seconds for Azure Log Analytics to specify the time interval to send the telemetry data. Default: 5")]
        public int? AzureLogAnalyticsFlushIntervalSeconds { get; }

        [Option("runtime.telemetry.azure-log-analytics.auth.custom-table-name", Required = false, HelpText = "Configure Custom Table Name for Azure Log Analytics used to find table to connect")]
        public string? AzureLogAnalyticsCustomTableName { get; }

        [Option("runtime.telemetry.azure-log-analytics.auth.dcr-immutable-id", Required = false, HelpText = "Configure DCR Immutable ID for Azure Log Analytics to find the data collection rule that defines how data is collected")]
        public string? AzureLogAnalyticsDcrImmutableId { get; }

        [Option("runtime.telemetry.azure-log-analytics.auth.dce-endpoint", Required = false, HelpText = "Configure DCE Endpoint for Azure Log Analytics to find table to send telemetry data")]
        public string? AzureLogAnalyticsDceEndpoint { get; }

        [Option("runtime.telemetry.file.enabled", Required = false, HelpText = "Enable/Disable File Sink logging. Default: False (boolean)")]
        public CliBool? FileSinkEnabled { get; }

        [Option("runtime.telemetry.file.path", Required = false, HelpText = "Configure path for File Sink logging. Default: /logs/dab-log.txt")]
        public string? FileSinkPath { get; }

        [Option("runtime.telemetry.file.rolling-interval", Required = false, HelpText = "Configure rolling interval for File Sink logging. Default: Day")]
        public RollingInterval? FileSinkRollingInterval { get; }

        [Option("runtime.telemetry.file.retained-file-count-limit", Required = false, HelpText = "Configure maximum number of retained files. Default: 1")]
        public int? FileSinkRetainedFileCountLimit { get; }

        [Option("runtime.telemetry.file.file-size-limit-bytes", Required = false, HelpText = "Configure maximum file size limit in bytes. Default: 1048576")]
        public long? FileSinkFileSizeLimitBytes { get; }

        public int Handler(ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            logger.LogInformation("{productName} {version}", PRODUCT_NAME, ProductInfo.GetProductVersion());
            bool isSuccess = ConfigGenerator.TryConfigureSettings(this, loader, fileSystem);
            if (isSuccess)
            {
                logger.LogInformation("Successfully updated runtime settings in the config file.");
                return CliReturnCode.SUCCESS;
            }
            else
            {
                logger.LogError("Failed to update runtime settings in the config file.");
                return CliReturnCode.GENERAL_ERROR;
            }
        }
    }
}
