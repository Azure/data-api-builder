using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Product;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

public class AppInsightsTelemetryInitializer : ITelemetryInitializer
{
    public static readonly IReadOnlyDictionary<string, string> GlobalProperties = new Dictionary<string, string>
    {
        { "ProductName", "Data API builder"},
        { "UserAgent", $"{ProductInfo.GetDataApiBuilderUserAgent()}" }
        // Add more custom properties here
    };

    public void Initialize(ITelemetry telemetry)
    {
        telemetry.Context.Cloud.RoleName = ProductInfo.GetDataApiBuilderUserAgent();
        telemetry.Context.Session.Id = Guid.NewGuid().ToString();
        telemetry.Context.Component.Version = ProductInfo.GetProductVersion();
        telemetry.Context.Device.OperatingSystem = Environment.OSVersion.ToString();
        telemetry.Context.User.Id = $"{Environment.MachineName}_{Environment.UserName}";

        foreach (KeyValuePair<string, string> property in GlobalProperties)
        {
            telemetry.Context.GlobalProperties.Add(property.Key, property.Value);
        }
    }
}
