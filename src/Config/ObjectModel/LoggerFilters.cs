// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    public class LoggerFilters
    {
        public const string RUNTIMECONFIGVALIDATORFILTER = "Azure.DataApiBuilder.Core.Configurations.RuntimeConfigValidator";
        public const string SQLQUERYENGINEFILTER = "Azure.DataApiBuilder.Core.Resolvers.SqlQueryEngine";
        public const string IQUERYEXECUTORFILTER = "Azure.DataApiBuilder.Core.Resolvers.IQueryExecutor";
        public const string ISQLMETADATAPROVIDERFILTER = "Azure.DataApiBuilder.Core.Services.ISqlMetadataProvider";
        public const string BASICHEALTHREPORTRESPONSEWRITERFILTER = "Azure.DataApiBuilder.Service.HealthCheck.BasicHealthReportResponseWriter";
        public const string COMPREHENSIVEHEALTHREPORTRESPONSEWRITERFILTER = "Azure.DataApiBuilder.Service.HealthCheck.ComprehensiveHealthReportResponseWriter";
        public const string RESTCONTROLLERFILTER = "Azure.DataApiBuilder.Service.Controllers.RestController";
        public const string CLIENTROLEHEADERAUTHENTICATIONMIDDLEWAREFILTER = "Azure.DataApiBuilder.Core.AuthenticationHelpers.ClientRoleHeaderAuthenticationMiddleware";
        public const string CONFIGURATIONCONTROLLERFILTER = "Azure.DataApiBuilder.Service.Controllers.ConfigurationController";
        public const string IAUTHORIZATIONHANDLERFILTER = "Microsoft.AspNetCore.Authorization.IAuthorizationHandler";
        public const string IAUTHORIZATIONRESOLVERFILTER = "Azure.DataApiBuilder.Auth.IAuthorizationResolver";
        public const string DEFAULTFILTER = "default";
    }
}
