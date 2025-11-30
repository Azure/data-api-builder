// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.AspNetCore;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Azure.DataApiBuilder.Service;

/// <summary>
/// Extension methods for configuring GraphQL services
/// </summary>
public static class GraphQLServiceExtensions
{
    /// <summary>
    /// Configure GraphQL services within the service collection of the
    /// request pipeline.
    /// </summary>
    public static IServiceCollection AddGraphQLServices(
        this IServiceCollection services,
        GraphQLRuntimeOptions? graphQLRuntimeOptions)
    {
        IRequestExecutorBuilder server = services.AddGraphQLServer()
            .AddInstrumentation()
            .AddType(new DateTimeType(disableFormatCheck: graphQLRuntimeOptions?.EnableLegacyDateTimeScalar ?? true))
            .AddHttpRequestInterceptor<DefaultHttpRequestInterceptor>()
            .ConfigureSchema((serviceProvider, schemaBuilder) =>
            {
                GraphQLSchemaCreator graphQLService = serviceProvider.GetRootServiceProvider()
                    .GetRequiredService<GraphQLSchemaCreator>();
                graphQLService.InitializeSchemaAndResolvers(schemaBuilder);
            })
            .AddHttpRequestInterceptor<IntrospectionInterceptor>()
            .AddAuthorizationHandler<GraphQLAuthorizationHandler>()
            .BindRuntimeType<TimeOnly, HotChocolate.Types.NodaTime.LocalTimeType>()
            .BindScalarType<HotChocolate.Types.NodaTime.LocalTimeType>("LocalTime")
            .AddTypeConverter<LocalTime, TimeOnly>(
                from => new TimeOnly(from.Hour, from.Minute, from.Second, from.Millisecond))
            .AddTypeConverter<TimeOnly, LocalTime>(
                from => new LocalTime(from.Hour, from.Minute, from.Second, from.Millisecond));

        if (graphQLRuntimeOptions is not null && graphQLRuntimeOptions.DepthLimit is > 0)
        {
            server = server.AddMaxExecutionDepthRule(
                maxAllowedExecutionDepth: graphQLRuntimeOptions.DepthLimit.Value,
                skipIntrospectionFields: true);
        }

        server.AddErrorFilter(error =>
        {
            var loggerFactory = services.BuildServiceProvider().GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger("GraphQLError");
            if (error.Exception is not null)
            {
                logger?.LogError(error.Exception, "A GraphQL request execution error occurred.");
                return error.WithMessage(error.Exception.Message);
            }

            if (error.Code is not null)
            {
                logger?.LogError("Error code: {errorCode}\nError message: {errorMessage}", error.Code, error.Message);
                return error.WithMessage(error.Message);
            }

            return error;
        })
        .AddErrorFilter(error =>
        {
            if (error.Exception is DataApiBuilderException thrownException)
            {
                error = error
                    .WithException(null)
                    .WithMessage(thrownException.Message)
                    .WithCode($"{thrownException.SubStatusCode}");

                if (!thrownException.StatusCode.IsClientError())
                {
                    error = error.WithLocations(Array.Empty<HotChocolate.Location>());
                }
            }

            return error;
        })
        .UseRequest<DetermineStatusCodeMiddleware>()
        .UseRequest<BuildRequestStateMiddleware>()
        .UseDefaultPipeline();

        return services;
    }

    /// <summary>
    /// Determines if the HTTP status code indicates a client error (4xx).
    /// </summary>
    private static bool IsClientError(this HttpStatusCode statusCode)
    {
        int statusCodeValue = (int)statusCode;
        return statusCodeValue >= 400 && statusCodeValue < 500;
    }
}

