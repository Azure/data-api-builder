using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Azure.DataGateway.Service
{
    public class DraftDevConfig
    {
        // Schema used for validation
        [JsonPropertyName("$schema")]
        public string? Schema { get; set; }

        // Backend database type and connection string
        [JsonPropertyName("data-source")]
        public DataSource? DataSource { get; set; }

        // Backend database types
        public Dictionary<string, object>? Cosmos { get; set; }
        public Dictionary<string, object>? Mssql { get; set; }
        public Dictionary<string, object>? Postgressql { get; set; }
        public Dictionary<string, object>? Mysql { get; set; }

        [JsonPropertyName("runtime")]
        public Dictionary<RuntimeType, object>? RuntimeSettings { get; set; }
        [JsonPropertyName("entities")]
        public Dictionary<string, DataGatewayEntity> Entities { get; set; }
        [JsonPropertyName("relationships")]
        public Dictionary<string, DataGatewayRelationship>? Relationships { get; set; }
        [JsonPropertyName("permissions")]
        public DataGatewayPermission[]? Permissions { get; set; }
    }

    /// <summary>
    /// Defines the backend database type
    /// and holds the connection string
    /// </summary>
    public class DataSource
    {
        public Database DatabaseType { get; set; }
        public string? ConnectionString { get; set; }
    }

    /// <summary>
    /// Defines the Entities that are exposed
    /// </summary>
    public class DataGatewayEntity
    {
        public string? Source { get; set; }
        [JsonPropertyName("permissions")]
        public DataGatewayPermission[]? Permissions { get; set; }
    }

    /// <summary>
    /// Defines the relationships between entities
    /// that can not be infered
    /// </summary>
    public class DataGatewayRelationship
    {
        public string Cardinality { get; set; }
        [JsonPropertyName("target.entity")]
        public string TargetEntity { get; set; }
        [JsonPropertyName("source.fields")]
        public string[] SourceFields { get; set; }
        [JsonPropertyName("target.fields")]
        public string[] TargetFields { get; set; }
        [JsonPropertyName("linking.[object]")]
        public string LinkingObject { get; set; }
        [JsonPropertyName("linking.[entity]")]
        public string LinkingEntity { get; set; }
        [JsonPropertyName("linking.source.fields")]
        public string[] LinkingSourceFields { get; set; }
        [JsonPropertyName("linking.target.fields")]
        public string[] LinkingTargetFields { get; set; }
    }

    /// <summary>
    /// Defines the security rules
    /// </summary>
    public class DataGatewayPermission
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }
        [JsonPropertyName("actions")]
        public Object Actions { get; set; }
        [JsonPropertyName("policies")]
        public string[]? Policies { get; set; }
    }

    /// <summary>
    /// Different runtime configuration types
    /// </summary>
    public enum RuntimeType
    {
        Rest,
        GraphQL,
        Host

    }

    public enum Database
    {
        Cosmos,
        MsSql,
        PostgreSql,
        MySql
    }

    public class ActionType
    {
        public string? Action { get; set; }
        public Field? Fields { get; set; }
    }

    public class Field
    {
        public List<string>? Include { get; set; }
        public List<string>? Exclude { get; set; }
    }
}
