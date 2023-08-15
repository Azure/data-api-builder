using System;
using Azure.DataApiBuilder.Service;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

public class MyTelemetryInitializer : ITelemetryInitializer
{
    public void Initialize(ITelemetry telemetry)
    {
        telemetry.Context.Cloud.RoleName = "DataApiBuilder";
        telemetry.Context.Session.Id = Guid.NewGuid().ToString();
        telemetry.Context.Component.Version = ProductInfo.GetProductVersion();
        telemetry.Context.Device.OperatingSystem = Environment.OSVersion.ToString();
        telemetry.Context.Device.Type = "Container";
        telemetry.Context.User.Id = "dab_user_1";
    }
}
