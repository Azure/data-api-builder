using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Product;

namespace Azure.DataApiBuilder.Core;

public static class VersionChecker
{
    private const string NUGETURL = "https://api.nuget.org/v3-flatcontainer/microsoft.dataapibuilder/index.json";

    public static bool IsCurrentVersion(out string? nugetVersion, out string? localVersion)
    {
        nugetVersion = FetchLatestNuGetVersion();
        localVersion = ProductInfo.GetProductVersion(false);
        return string.IsNullOrEmpty(nugetVersion) || nugetVersion == localVersion;
    }

    private static string? FetchLatestNuGetVersion()
    {
        try
        {
            using HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
            NuGetVersionResponse? versionData = httpClient
                .GetFromJsonAsync<NuGetVersionResponse>(new Uri(NUGETURL).ToString())
                .GetAwaiter().GetResult();

            return versionData?.Versions
                ?.Where(version => !version.Contains("-rc")) // Filter out pre-release versions
                .Select(version => new Version(version))     // Convert to Version objects
                .Max()?.ToString();                          // Get the latest 
        }
        catch (Exception)
        {
            return null; // Assume no update available on failure
        }
    }

    private class NuGetVersionResponse
    {
        [JsonPropertyName("versions")]
        public string[]? Versions { get; set; }
    }
}
