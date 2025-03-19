// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    public class LoggerFilters
    {
        public const string RUNTIME_CONFIG_VALIDATOR_FILTER = "Azure.DataApiBuilder.Core.Configurations.RuntimeConfigValidator";
        public const string SQL_QUERY_ENGINE_FILTER = "Azure.DataApiBuilder.Core.Resolvers.SqlQueryEngine";
        public const string IQUERY_EXECUTOR_FILTER = "Azure.DataApiBuilder.Core.Resolvers.IQueryExecutor";
        public const string ISQL_METADATA_PROVIDER_FILTER = "Azure.DataApiBuilder.Core.Services.ISqlMetadataProvider";
        public const string BASIC_HEALTH_REPORT_RESPONSE_WRITER_FILTER = "Azure.DataApiBuilder.Service.HealthCheck.BasicHealthReportResponseWriter";
        public const string COMPREHENSIVE_HEALTH_REPORT_RESPONSE_WRITER_FILTER = "Azure.DataApiBuilder.Service.HealthCheck.ComprehensiveHealthReportResponseWriter";
        public const string REST_CONTROLLER_FILTER = "Azure.DataApiBuilder.Service.Controllers.RestController";
        public const string CLIENT_ROLE_HEADER_AUTHENTICATION_MIDDLEWARE_FILTER = "Azure.DataApiBuilder.Core.AuthenticationHelpers.ClientRoleHeaderAuthenticationMiddleware";
        public const string CONFIGURATION_CONTROLLER_FILTER = "Azure.DataApiBuilder.Service.Controllers.ConfigurationController";
        public const string IAUTHORIZATION_HANDLER_FILTER = "Microsoft.AspNetCore.Authorization.IAuthorizationHandler";
        public const string IAUTHORIZATION_RESOLVER_FILTER = "Azure.DataApiBuilder.Auth.IAuthorizationResolver";
        public const string DEFAULT_FILTER = "default";
    }
}
