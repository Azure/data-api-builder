// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    /// <summary>
    /// Creates a JSON response for the health check endpoint using the provided health report.
    /// If the response has already been created, it will be reused.
    /// </summary>
    public class HealthReportResponseWriter
    {
        private byte[]? _responseBytes;
        private const string JSON_CONTENT_TYPE = "application/json; charset=utf-8";
        private const string DAB_VERSION_KEY = "version";
        private const string DAB_APPNAME_KEY = "appName";

        /// <summary>
        /// Function provided to the health check middleware to write the response.
        /// </summary>
        /// <param name="context">HttpContext for writing the response.</param>
        /// <param name="healthReport">Result of health check(s).</param>
        /// <returns>Writes the http response to the http context.</returns>
        public Task WriteResponse(HttpContext context, HealthReport healthReport)
        {

            context.Response.ContentType = JSON_CONTENT_TYPE;

            if (_responseBytes is null)
            {
                _responseBytes = CreateResponse(healthReport);
            }

            return context.Response.WriteAsync(Encoding.UTF8.GetString(_responseBytes));
        }

        /// <summary>
        /// Using the provided health report, creates the JSON byte array to be returned and cached.
        /// Currently, checks for the custom DabHealthCheck result and adds the version and app name to the response.
        /// The result of the response returned for the health endpoint would be:
        /// {
        ///     "status": "Healthy",
        ///     "version": "Major.Minor.Patch",
        ///     "appName": "dab_oss_Major.Minor.Patch"
        /// }
        /// </summary>
        /// <param name="healthReport">Collection of Health Check results calculated by dotnet HealthCheck endpoint.</param>
        /// <returns>Byte array with JSON response contents.</returns>
        public byte[] CreateResponse(HealthReport healthReport)
        {
            JsonWriterOptions options = new() { Indented = true };

            using MemoryStream memoryStream = new();
            using (Utf8JsonWriter jsonWriter = new(memoryStream, options))
            {
                jsonWriter.WriteStartObject();
                jsonWriter.WriteString("status", healthReport.Status.ToString());

                if (healthReport.Entries.TryGetValue(key: typeof(DabHealthCheck).Name, out HealthReportEntry healthReportEntry))
                {
                    if (healthReportEntry.Data.TryGetValue(DAB_VERSION_KEY, out object? versionValue) && versionValue is string versionNumber)
                    {
                        jsonWriter.WriteString(DAB_VERSION_KEY, versionNumber);
                    }

                    if (healthReportEntry.Data.TryGetValue(DAB_APPNAME_KEY, out object? appNameValue) && appNameValue is string appName)
                    {
                        jsonWriter.WriteString(DAB_APPNAME_KEY, appName);
                    }
                }

                jsonWriter.WriteEndObject();
            }

            return memoryStream.ToArray();
        }
    }
}
