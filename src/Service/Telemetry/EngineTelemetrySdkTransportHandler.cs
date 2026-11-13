// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// Public HttpClient boundary for the SDK: links worker cancellation and rejects unexpected
    /// requests/enrichment without rewriting the SDK payload or accessing its private APIs.
    /// </summary>
    internal sealed class EngineTelemetrySdkTransportHandler(
        HttpMessageHandler inner, AsyncLocal<EngineTelemetryExportAttempt?> attempt, Uri trackEndpoint) : DelegatingHandler(inner)
    {
        private const long MAX_PAYLOAD_BYTES = 64 * 1024;
        private const long MAX_RESPONSE_BYTES = 16 * 1024;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            EngineTelemetryExportAttempt? current = attempt.Value;
            if (current is null || !current.TryBeginRequest() || request.Method != HttpMethod.Post ||
                !string.Equals(request.RequestUri?.AbsoluteUri, trackEndpoint.AbsoluteUri, StringComparison.Ordinal))
            {
                throw new HttpRequestException("Product telemetry transport rejected a request.");
            }

            // AzureMonitorLogExporter.Export is synchronous and passes CancellationToken.None.
            // This public boundary carries the worker's token through send AND response-body I/O.
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(current.Token, cancellationToken);
            linked.Token.ThrowIfCancellationRequested();
            if (request.Content is null)
            {
                throw new HttpRequestException("Product telemetry transport requires a payload.");
            }

            await request.Content.LoadIntoBufferAsync(MAX_PAYLOAD_BYTES, linked.Token).ConfigureAwait(false);
            if (!HasOnlyApprovedEnvelopeTags(await request.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false)))
            {
                throw new HttpRequestException("Product telemetry transport rejected SDK enrichment.");
            }

            linked.Token.ThrowIfCancellationRequested();
            HttpResponseMessage response = await base.SendAsync(request, linked.Token).ConfigureAwait(false);
            try
            {
                // This temporary validation transport deliberately permits only the configured
                // endpoint. Reject before Azure Monitor's separate redirect policy can replay it.
                if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                {
                    throw new HttpRequestException("Product telemetry redirects are disabled during validation.");
                }

                await response.Content.LoadIntoBufferAsync(MAX_RESPONSE_BYTES, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        internal static bool HasOnlyApprovedEnvelopeTags(string payload)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("tags", out JsonElement tags) || tags.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                bool hasSuppressedIp = false;
                foreach (JsonProperty tag in tags.EnumerateObject())
                {
                    if (tag.Name == "ai.location.ip")
                    {
                        if (hasSuppressedIp || tag.Value.ValueKind != JsonValueKind.String ||
                            tag.Value.GetString() != ApplicationInsightsEventAttributes.SUPPRESSED_IP)
                        {
                            return false;
                        }

                        hasSuppressedIp = true;
                        continue;
                    }

                    if (tag.Name == "ai.internal.sdkVersion" && tag.Value.ValueKind == JsonValueKind.String)
                    {
                        continue;
                    }

                    if (tag.Name is not ("ai.cloud.role" or "ai.cloud.roleInstance" or "ai.application.ver") ||
                        tag.Value.ValueKind != JsonValueKind.Null)
                    {
                        return false;
                    }
                }

                return hasSuppressedIp;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
