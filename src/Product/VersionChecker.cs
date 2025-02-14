using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Product;

public static class VersionChecker
{
    private const string NuGetApiUrl = "https://api.nuget.org/v3-flatcontainer/Microsoft.DataApiBuilder/index.json";

    public static void GetVersions(out string? latestVersion, out string? currentVersion)
    {
        latestVersion = FetchLatestNuGetVersion();
        currentVersion = GetCurrentVersionFromAssembly(Assembly.GetExecutingAssembly());
    }

    private static string? FetchLatestNuGetVersion()
    {
        try
        {
            using HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            NuGetVersionResponse? versionData = httpClient.GetFromJsonAsync<NuGetVersionResponse>(NuGetApiUrl)
                .GetAwaiter().GetResult();

            return versionData?.Versions
                ?.Where(version => !version.Contains("-rc")) // Filter out pre-release versions
                .Select(version => new Version(version))     // Convert to Version objects
                .Max()?.ToString();                          // Get the latest 
        }
        catch
        {
            return null; // Assume no update available on failure
        }
    }

    private static string? GetCurrentVersionFromAssembly(Assembly assembly)
    {
        string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return !string.IsNullOrEmpty(version) ? version.Split('+')[0] : assembly.GetName().Version?.ToString();
    }

    private class NuGetVersionResponse
    {
        [JsonPropertyName("versions")]
        public string[]? Versions { get; set; }
    }
}
