namespace Azure.DataGateway.Service.ServerTiming
{
    using Microsoft.AspNetCore.Http;

    public static class HttpResponseHeadersExtensions
    {
        public static void SetServerTiming(this HttpResponse response, params ServerTimingMetric[] metrics)
        {
            ServerTimingHeaderValue serverTiming = new();

            foreach (ServerTimingMetric metric in metrics)
            {
                serverTiming.Metrics.Add(metric);
            }

            response.Headers.Append("Server-Timing", serverTiming.ToString());
        }
    }
}
