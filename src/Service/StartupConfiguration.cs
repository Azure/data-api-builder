// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.AuthenticationHelpers;
using Azure.DataApiBuilder.Core.AuthenticationHelpers.AuthenticationSimulator;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CorsOptions = Azure.DataApiBuilder.Config.ObjectModel.CorsOptions;

namespace Azure.DataApiBuilder.Service;

/// <summary>
/// Configuration class that holds startup settings and helper methods.
/// Replaces the static state from the original Startup class.
/// </summary>
public static class StartupConfiguration
{
    public static LogLevel MinimumLogLevel = LogLevel.Error;
    public static bool IsLogLevelOverriddenByCli;
    public static AzureLogAnalyticsCustomLogCollector CustomLogCollector = new();
    public static ApplicationInsightsOptions AppInsightsOptions = new();
    public static OpenTelemetryOptions OpenTelemetryOptions = new();
    public static AzureLogAnalyticsOptions AzureLogAnalyticsOptions = new();
    public static FileSinkOptions FileSinkOptions = new();
    public const string NO_HTTPS_REDIRECT_FLAG = "--no-https-redirect";

    /// <summary>
    /// Useful in cases where we need to:
    /// Send telemetry data to a custom endpoint that is not supported by the default telemetry channel.
    /// Modify the telemetry data before it is sent, such as adding custom properties or filtering out sensitive data.
    /// Implement custom retry logic or error handling for telemetry data that fails to send.
    /// For testing purposes.
    /// </summary>
    public static Microsoft.ApplicationInsights.Channel.ITelemetryChannel? CustomTelemetryChannel { get; set; }

    /// <summary>
    /// If LogLevel is NOT overridden by CLI, attempts to find the
    /// minimum log level based on host.mode in the runtime config if available.
    /// Creates a logger factory with the minimum log level.
    /// </summary>
    public static ILoggerFactory CreateLoggerFactoryForHostedAndNonHostedScenario(
        System.IServiceProvider serviceProvider,
        Telemetry.LogLevelInitializer logLevelInitializer)
    {
        if (!IsLogLevelOverriddenByCli)
        {
            logLevelInitializer.SetLogLevel();
        }

        Microsoft.ApplicationInsights.TelemetryClient? appTelemetryClient = serviceProvider.GetService<Microsoft.ApplicationInsights.TelemetryClient>();
        Serilog.Core.Logger? serilogLogger = serviceProvider.GetService<Serilog.Core.Logger>();

        return Program.GetLoggerFactoryForLogLevel(logLevelInitializer.MinLogLevel, appTelemetryClient, logLevelInitializer, serilogLogger);
    }

    /// <summary>
    /// Add services necessary for Authentication Middleware and based on the loaded
    /// runtime configuration set the AuthenticationOptions to be either
    /// EasyAuth based (by default) or JwtBearerOptions.
    /// When no runtime configuration is set on engine startup, set the
    /// default authentication scheme to EasyAuth.
    /// </summary>
    public static void ConfigureAuthentication(IServiceCollection services, RuntimeConfigProvider runtimeConfigurationProvider)
    {
        if (runtimeConfigurationProvider.TryGetConfig(out Config.ObjectModel.RuntimeConfig? runtimeConfig) &&
            runtimeConfig.Runtime?.Host?.Authentication is not null)
        {
            Config.ObjectModel.AuthenticationOptions authOptions = runtimeConfig.Runtime.Host.Authentication;
            Config.ObjectModel.HostMode mode = runtimeConfig.Runtime.Host.Mode;
            
            if (!authOptions.IsAuthenticationSimulatorEnabled() && !authOptions.IsEasyAuthAuthenticationProvider())
            {
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.MapInboundClaims = false;
                        options.Audience = authOptions.Jwt!.Audience;
                        options.Authority = authOptions.Jwt!.Issuer;
                        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
                        {
                            RoleClaimType = Config.ObjectModel.AuthenticationOptions.ROLE_CLAIM_TYPE
                        };
                    });
            }
            else if (authOptions.IsEasyAuthAuthenticationProvider())
            {
                EasyAuthType easyAuthType = EnumExtensions.Deserialize<EasyAuthType>(runtimeConfig.Runtime.Host.Authentication.Provider);
                bool isProductionMode = mode != Config.ObjectModel.HostMode.Development;
                bool appServiceEnvironmentDetected = AppServiceAuthenticationInfo.AreExpectedAppServiceEnvVarsPresent();

                if (easyAuthType == EasyAuthType.AppService && !appServiceEnvironmentDetected)
                {
                    if (isProductionMode)
                    {
                        throw new DataApiBuilderException(
                            message: AppServiceAuthenticationInfo.APPSERVICE_PROD_MISSING_ENV_CONFIG,
                            statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                    }
                }

                services.AddAuthentication(EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME)
                    .AddEasyAuthAuthentication(easyAuthAuthenticationProvider: easyAuthType);
            }
            else if (mode == Config.ObjectModel.HostMode.Development && authOptions.IsAuthenticationSimulatorEnabled())
            {
                services.AddAuthentication(Core.AuthenticationHelpers.AuthenticationSimulator.SimulatorAuthenticationDefaults.AUTHENTICATIONSCHEME)
                    .AddSimulatorAuthentication();
            }
            else
            {
                throw new DataApiBuilderException(
                    message: "Authentication configuration not supported.",
                    statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
            }
        }
        else
        {
            SetStaticWebAppsAuthentication(services);
        }
    }

    /// <summary>
    /// Registers all DAB supported authentication providers (schemes) so that at request time,
    /// DAB can use the runtime config's defined provider to authenticate requests.
    /// </summary>
    public static void ConfigureAuthenticationV2(IServiceCollection services, RuntimeConfigProvider runtimeConfigProvider)
    {
        services.AddSingleton<IOptionsChangeTokenSource<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>(new JwtBearerOptionsChangeTokenSource(runtimeConfigProvider));
        services.AddSingleton<IConfigureOptions<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>, ConfigureJwtBearerOptions>();
        services.AddAuthentication()
            .AddEnvDetectedEasyAuth()
            .AddJwtBearer()
            .AddSimulatorAuthentication();
    }

    /// <summary>
    /// Sets Static Web Apps EasyAuth as the authentication scheme for the engine.
    /// </summary>
    private static void SetStaticWebAppsAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME)
            .AddEasyAuthAuthentication(EasyAuthType.StaticWebApps);
    }

    /// <summary>
    /// Build a CorsPolicy to be consumed by the useCors function, allowing requests with any methods or headers
    /// Used both for app startup and testing purposes
    /// </summary>
    public static Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicy ConfigureCors(CorsPolicyBuilder builder, CorsOptions corsConfig)
    {
        if (corsConfig.AllowCredentials)
        {
            return builder
                .WithOrigins(corsConfig.Origins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .SetIsOriginAllowedToAllowWildcardSubdomains()
                .AllowCredentials()
                .Build();
        }

        return builder
            .WithOrigins(corsConfig.Origins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .SetIsOriginAllowedToAllowWildcardSubdomains()
            .Build();
    }

    /// <summary>
    /// Indicates whether to provide UI visualization of REST(via Swagger) or GraphQL (via Banana CakePop).
    /// </summary>
    public static bool IsUIEnabled(Config.ObjectModel.RuntimeConfig? runtimeConfig, IWebHostEnvironment env)
    {
        return (runtimeConfig is not null && runtimeConfig.IsDevelopmentMode()) || env.IsDevelopment();
    }

    /// <summary>
    /// Adds all of the class namespaces that have loggers that the user is able to change
    /// </summary>
    public static void AddValidFilters()
    {
        LoggerFilters.AddFilter(typeof(RuntimeConfigValidator).FullName);
        LoggerFilters.AddFilter(typeof(Core.Resolvers.SqlQueryEngine).FullName);
        LoggerFilters.AddFilter(typeof(Core.Resolvers.IQueryExecutor).FullName);
        LoggerFilters.AddFilter(typeof(Core.Services.ISqlMetadataProvider).FullName);
        LoggerFilters.AddFilter(typeof(HealthCheck.BasicHealthReportResponseWriter).FullName);
        LoggerFilters.AddFilter(typeof(HealthCheck.ComprehensiveHealthReportResponseWriter).FullName);
        LoggerFilters.AddFilter(typeof(Controllers.RestController).FullName);
        LoggerFilters.AddFilter(typeof(Core.AuthenticationHelpers.ClientRoleHeaderAuthenticationMiddleware).FullName);
        LoggerFilters.AddFilter(typeof(Controllers.ConfigurationController).FullName);
        LoggerFilters.AddFilter(typeof(IAuthorizationHandler).FullName);
        LoggerFilters.AddFilter(typeof(Auth.IAuthorizationResolver).FullName);
        LoggerFilters.AddFilter("default");
    }

    /// <summary>
    /// Helper function that returns if AzureLogAnalytics feature is enabled and properly configured.
    /// </summary>
    public static bool IsAzureLogAnalyticsAvailable(AzureLogAnalyticsOptions azureLogAnalyticsOptions)
    {
        return azureLogAnalyticsOptions.Auth is not null
            && azureLogAnalyticsOptions.Enabled
            && !string.IsNullOrWhiteSpace(azureLogAnalyticsOptions.Auth.CustomTableName)
            && !string.IsNullOrWhiteSpace(azureLogAnalyticsOptions.Auth.DcrImmutableId)
            && !string.IsNullOrWhiteSpace(azureLogAnalyticsOptions.Auth.DceEndpoint);
    }

    /// <summary>
    /// Evicts the GraphQL schema from the request executor resolver.
    /// </summary>
    public static void EvictGraphQLSchema(HotChocolate.Execution.IRequestExecutorManager requestExecutorResolver)
    {
        Console.WriteLine("Evicting old GraphQL schema.");
        requestExecutorResolver.EvictExecutor();
    }
}

