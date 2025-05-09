// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using OpenTelemetry.Trace;
using Kestral = Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpMethod;

namespace Azure.DataApiBuilder.Core.Telemetry
{
    public static class TelemetryTracesHelper
    {
        /// <summary>
        /// Activity source for Data API Builder telemetry.
        /// </summary>
        public static readonly ActivitySource DABActivitySource = new("DataApiBuilder");

        /// <summary>
        /// Tracks the start of the main controller activity.
        /// </summary>
        /// <param name="activity">The activity instance.</param>
        /// <param name="httpMethod">The HTTP method of the request (e.g., GET, POST).</param>
        /// <param name="userAgent">The user agent string from the request.</param>
        /// <param name="actionType">The type of action being performed (e.g. Read).</param>
        /// <param name="httpURL">The URL of the request.</param>
        /// <param name="queryString">The query string of the request, if any.</param>
        /// <param name="userRole">The role of the user making the request.</param>
        /// <param name="apiType">The type of API being used (e.g., REST, GraphQL).</param>
        public static void TrackMainControllerActivityStarted(
            this Activity activity,
            Kestral httpMethod,
            string userAgent,
            string actionType, // CRUD(EntityActionOperation) for REST, Query|Mutation(OperationType) for GraphQL
            string httpURL,
            string? queryString,
            string? userRole,
            ApiType apiType)
        {
            if (activity.IsAllDataRequested)
            {
                activity.SetTag("http.method", httpMethod);
                activity.SetTag("user-agent", userAgent);
                activity.SetTag("action.type", actionType);
                activity.SetTag("http.url", httpURL);
                if (!string.IsNullOrEmpty(queryString))
                {
                    activity.SetTag("http.querystring", queryString);
                }

                if (!string.IsNullOrEmpty(userRole))
                {
                    activity.SetTag("user.role", userRole);
                }

                activity.SetTag("api.type", apiType);
            }
        }

        /// <summary>
        /// Tracks the start of a query activity.
        /// </summary>
        /// <param name="activity">The activity instance.</param>
        /// <param name="databaseType">The type of database being queried.</param>
        /// <param name="dataSourceName">The name of the data source being queried.</param>
        public static void TrackQueryActivityStarted(
            this Activity activity,
            DatabaseType databaseType,
            string dataSourceName)
        {
            if (activity.IsAllDataRequested)
            {
                activity.SetTag("data-source.type", databaseType);
                activity.SetTag("data-source.name", dataSourceName);
            }

        }

        /// <summary>
        /// Tracks the completion of the main controller activity without any exceptions.
        /// </summary>
        /// <param name="activity">The activity instance.</param>
        /// <param name="statusCode">The HTTP status code of the response.</param>
        public static void TrackMainControllerActivityFinished(
            this Activity activity,
            HttpStatusCode statusCode)
        {
            if (activity.IsAllDataRequested)
            {
                activity.SetTag("status.code", statusCode);
            }
        }

        /// <summary>
        /// Tracks the completion of the main controller activity with an exception.
        /// </summary>
        /// <param name="activity">The activity instance.</param>
        /// <param name="ex">The exception that occurred.</param>
        /// <param name="statusCode">The HTTP status code of the response.</param>
        public static void TrackMainControllerActivityFinishedWithException(
            this Activity activity,
            Exception ex,
            HttpStatusCode statusCode)
        {
            if (activity.IsAllDataRequested)
            {
                activity.SetStatus(Status.Error.WithDescription(ex.Message));
                activity.RecordException(ex);
                activity.SetTag("error.type", ex.GetType().Name);
                activity.SetTag("error.message", ex.Message);
                activity.SetTag("status.code", statusCode);
            }
        }
    }
}
