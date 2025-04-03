// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using OpenTelemetry.Trace;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    public static class TelemetryTracesHelper
    {
        /// <summary>
        /// Activity source for Data API Builder telemetry.
        /// </summary>
        public static readonly ActivitySource DABActivitySource = new("DataApiBuilder");

        /// <summary>
        /// Tracks the start of a REST controller activity.
        /// </summary>
        /// <param name="activity">The activity instance.</param>
        /// <param name="httpMethod">The HTTP method of the request (e.g., GET, POST).</param>
        /// <param name="userAgent">The user agent string from the request.</param>
        /// <param name="actionType">The type of action being performed (e.g. Read).</param>
        /// <param name="httpURL">The URL of the request.</param>
        /// <param name="queryString">The query string of the request, if any.</param>
        /// <param name="userRole">The role of the user making the request.</param>
        /// <param name="apiType">The type of API being used (e.g., REST, GraphQL).</param>
        public static void TrackRestControllerActivityStarted(
            this Activity activity,
            string httpMethod,
            string userAgent,
            string actionType,
            string httpURL,
            string? queryString,
            string? userRole,
            string apiType)
        {
            if (activity.IsAllDataRequested)
            {
                activity.SetTag("http.method", httpMethod);
                activity.SetTag("user-agent", userAgent);
                activity.SetTag("action.type", actionType);
                activity.SetTag("http.url", httpURL);
                if (queryString is not null && queryString != string.Empty)
                {
                    activity.SetTag("http.querystring", queryString);
                }

                activity.SetTag("user.role", userRole);
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
            string databaseType,
            string dataSourceName)
        {
            if(activity.IsAllDataRequested)
            {
                activity.SetTag("data-source.type", databaseType);
                activity.SetTag("data-source.name", dataSourceName);
            }

        }

        /// <summary>
        /// Tracks the completion of a REST controller activity.
        /// </summary>
        /// <param name="activity">The activity instance.</param>
        /// <param name="statusCode">The HTTP status code of the response.</param>
        public static void TrackRestControllerActivityFinished(
            this Activity activity,
            int statusCode)
        {
            if (activity.IsAllDataRequested)
            {
                activity.SetTag("status.code", statusCode);
            }
        }

        /// <summary>
        /// Tracks the completion of a REST controller activity with an exception.
        /// </summary>
        /// <param name="activity">The activity instance.</param>
        /// <param name="ex">The exception that occurred.</param>
        /// <param name="statusCode">The HTTP status code of the response.</param>
        public static void TrackRestControllerActivityFinishedWithWithException(
            this Activity activity,
            Exception ex,
            int statusCode)
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
