// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config;

public static class DabConfigEvents
{
    public const string QUERY_MANAGER_FACTORY_ON_CONFIG_CHANGED = "QUERY_MANAGER_FACTORY_ON_CONFIG_CHANGED";
    public const string METADATA_PROVIDER_FACTORY_ON_CONFIG_CHANGED = "METADATA_PROVIDER_FACTORY_ON_CONFIG_CHANGED";
    public const string QUERY_ENGINE_FACTORY_ON_CONFIG_CHANGED = "QUERY_ENGINE_FACTORY_ON_CONFIG_CHANGED";
    public const string MUTATION_ENGINE_FACTORY_ON_CONFIG_CHANGED = "MUTATION_ENGINE_FACTORY_ON_CONFIG_CHANGED";
    public const string QUERY_EXECUTOR_ON_CONFIG_CHANGED = "QUERY_EXECUTOR_ON_CONFIG_CHANGED";
    public const string MSSQL_QUERY_EXECUTOR_ON_CONFIG_CHANGED = "MSSQL_QUERY_EXECUTOR_ON_CONFIG_CHANGED";
    public const string MYSQL_QUERY_EXECUTOR_ON_CONFIG_CHANGED = "MYSQL_QUERY_EXECUTOR_ON_CONFIG_CHANGED";
    public const string POSTGRESQL_QUERY_EXECUTOR_ON_CONFIG_CHANGED = "POSTGRESQL_QUERY_EXECUTOR_ON_CONFIG_CHANGED";
    public const string DOCUMENTOR_ON_CONFIG_CHANGED = "DOCUMENTOR_ON_CONFIG_CHANGED";
    public const string AUTHZ_RESOLVER_ON_CONFIG_CHANGED = "AUTHZ_RESOLVER_ON_CONFIG_CHANGED";
    public const string GRAPHQL_SCHEMA_ON_CONFIG_CHANGED = "GRAPHQL_SCHEMA_ON_CONFIG_CHANGED";
    public const string GRAPHQL_SCHEMA_EVICTION_ON_CONFIG_CHANGED = "GRAPHQL_SCHEMA_EVICTION_ON_CONFIG_CHANGED";
    public const string GRAPHQL_SCHEMA_CREATOR_ON_CONFIG_CHANGED = "GRAPHQL_SCHEMA_CREATOR_ON_CONFIG_CHANGED";

}
