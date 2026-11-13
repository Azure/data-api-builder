// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.Telemetry;

/// <summary>
/// Categorical host information captured once when a telemetry token is computed. Detection is
/// offline and best effort: no metadata endpoints, file-system probes, or credential resolution.
/// Environment values are never retained in the snapshot or included in the token.
/// </summary>
internal readonly record struct ApplicationNameTelemetryEnvironment(
    char OperatingSystem,
    char RunningInContainer,
    char HostingEnvironment,
    char AzureHostingService)
{
    /// <summary>
    /// Reads current host signals without caching them across configuration loads. The optional reader
    /// lets tests supply an isolated environment without mutating process-wide variables.
    /// </summary>
    internal static ApplicationNameTelemetryEnvironment Capture(Func<string, string?>? readEnvironmentVariable = null)
    {
        readEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        char operatingSystem = System.OperatingSystem.IsWindows() ? 'W'
            : System.OperatingSystem.IsLinux() ? 'L'
            : System.OperatingSystem.IsMacOS() ? 'M'
            : 'O';

        bool? container = ParseBoolean(readEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"));
        bool? containers = ParseBoolean(readEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS"));
        // Either supported variable can supply the answer. Contradictory valid values, or no valid
        // value at all, are unknown; missing flags do not prove that this is outside a container.
        char runningInContainer = container.HasValue && containers.HasValue && container != containers
            ? 'M'
            : (container ?? containers) switch { true => '1', false => '0', null => 'M' };

        (char hostingEnvironment, char azureHostingService) = DetectHosting(readEnvironmentVariable);
        return new(operatingSystem, runningInContainer, hostingEnvironment, azureHostingService);
    }

    private static bool? ParseBoolean(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "1" or "TRUE" => true,
        "0" or "FALSE" => false,
        _ => null,
    };

    private static (char HostingEnvironment, char AzureHostingService) DetectHosting(Func<string, string?> read)
    {
        bool HasValue(string name) => !string.IsNullOrWhiteSpace(read(name));

        char? hostingOverride = ParseHostingOverride(read(ApplicationNameTelemetry.HOSTING_ENVIRONMENT_ENV_VAR));
        char? serviceOverride = ParseAzureServiceOverride(read(ApplicationNameTelemetry.AZURE_HOSTING_SERVICE_ENV_VAR));

        bool containerApps = HasValue("CONTAINER_APP_NAME") || HasValue("CONTAINER_APP_REVISION")
            || HasValue("CONTAINER_APP_JOB_NAME") || HasValue("CONTAINER_APP_JOB_EXECUTION_NAME");
        bool appService = HasValue("WEBSITE_SITE_NAME") || HasValue("WEBSITE_INSTANCE_ID");
        // An explicit Not Azure service suppresses stale automatic Azure signals as well.
        bool azure = serviceOverride != 'N' && (containerApps || appService);
        bool aws = HasValue("AWS_EXECUTION_ENV") || HasValue("AWS_LAMBDA_FUNCTION_NAME")
            || HasValue("ECS_CONTAINER_METADATA_URI") || HasValue("ECS_CONTAINER_METADATA_URI_V4");
        bool gcp = HasValue("CLOUD_RUN_JOB") || HasValue("CLOUD_RUN_WORKER_POOL") || HasValue("GAE_ENV");

        // SDK credentials/project/region settings also exist on developer machines. Do not use them
        // to infer a cloud, and do not treat generic Kubernetes as proof of AKS (or any other cloud).
        int cloudCount = (azure ? 1 : 0) + (aws ? 1 : 0) + (gcp ? 1 : 0);
        char detectedHost = cloudCount > 1 ? 'M' : azure ? 'A' : aws ? 'W' : gcp ? 'G'
            // K_SERVICE is part of the portable Knative contract, not a GCP-specific signal.
            : HasValue("KUBERNETES_SERVICE_HOST") || HasValue("K_SERVICE") ? 'O' : 'L';
        char detectedAzureService = containerApps && appService ? 'M'
            : containerApps ? 'C' : appService ? 'S' : 'M';

        // An explicit Azure service also identifies the cloud, unless an explicit hosting override
        // says otherwise. AKS and ACI require this override: neither has a universal runtime marker.
        char host = hostingOverride ?? (serviceOverride is 'C' or 'K' or 'S' or 'I' or 'O' ? 'A' : detectedHost);
        char service = (hostingOverride, serviceOverride) switch
        {
            // Only an EXPLICIT cloud override outranks an explicit service override. Automatic
            // cloud inference must not erase a deliberate Missing or Not Azure service value.
            (not null, _) => host switch
            {
                'A' => serviceOverride == 'N' ? 'M' : serviceOverride ?? detectedAzureService,
                'M' => 'M',
                _ => 'N',
            },
            (null, not null) => serviceOverride.Value,
            _ => host == 'A' ? detectedAzureService : host == 'M' ? 'M' : 'N',
        };

        return (host, service);
    }

    // Blank overrides are absent. Nonblank invalid overrides deliberately produce Missing rather
    // than silently falling back to automatic detection. Only the bounded code is ever emitted.
    private static char? ParseHostingOverride(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        null or "" => null,
        "L" or "LOCAL" => 'L',
        "A" or "AZURE" => 'A',
        "W" or "AWS" => 'W',
        "G" or "GCP" => 'G',
        "O" or "OTHER" => 'O',
        _ => 'M',
    };

    private static char? ParseAzureServiceOverride(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        null or "" => null,
        "C" or "CONTAINERAPPS" or "CONTAINER APPS" => 'C',
        "K" or "AKS" => 'K',
        "S" or "APPSERVICE" or "APP SERVICE" => 'S',
        "I" or "ACI" or "CONTAINERINSTANCES" or "CONTAINER INSTANCES" => 'I',
        "O" or "OTHER" => 'O',
        "N" or "NOTAZURE" or "NOT AZURE" => 'N',
        _ => 'M',
    };
}
