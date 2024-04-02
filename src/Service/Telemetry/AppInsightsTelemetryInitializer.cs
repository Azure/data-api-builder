using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Product;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

public class AppInsightsTelemetryInitializer : ITelemetryInitializer
{
    public static readonly IReadOnlyDictionary<string, string> GlobalProperties = new Dictionary<string, string>
    {
        { "ProductName", $"{ProductInfo.DAB_USER_AGENT}"},
        { "UserAgent", $"{ProductInfo.GetDataApiBuilderUserAgent()}" }
        // Add more custom properties here
    };

    /// <summary>
    /// Initializes the telemetry context.
    /// </summary>
    /// <param name="telemetry">The telemetry object to initialize</param>
    public void Initialize(ITelemetry telemetry)
    {
        telemetry.Context.Cloud.RoleName = ProductInfo.CLOUD_ROLE_NAME;
        telemetry.Context.Session.Id = Guid.NewGuid().ToString();
        telemetry.Context.Component.Version = ProductInfo.GetProductVersion();

        foreach (KeyValuePair<string, string> property in GlobalProperties)
        {
            telemetry.Context.GlobalProperties.TryAdd(property.Key, property.Value);
        }
    }
}
