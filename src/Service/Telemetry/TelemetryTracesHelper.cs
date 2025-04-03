// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using OpenTelemetry.Trace;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    public static class TelemetryTracesHelper
    {
        public static readonly ActivitySource DABActivitySource = new("DataApiBuilder");

        public static void TrackRestControllerActivityStarted(this Activity activity,
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
                if (queryString != string.Empty)
                {
                    activity.SetTag("http.querystring", queryString);
                }

                activity.SetTag("user.role", userRole);
                activity.SetTag("api.type", apiType);
            }
        }

        public static void TrackQueryActivityStarted(this Activity activity,
            string databaseType,
            string dataSourceName)
        {
            if(activity.IsAllDataRequested)
            {
                activity.SetTag("data-source.type", databaseType);
                activity.SetTag("data-source.name", dataSourceName);
            }

        }

        public static void TrackRestControllerActivityFinished(
            this Activity activity,
            int statusCode)
        {
            if (activity.IsAllDataRequested)
            {
                activity.SetTag("status.code", statusCode);
            }
        }

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
