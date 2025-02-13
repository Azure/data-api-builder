using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Product;

public static class VersionChecker
{
    private const string PackageName = "Microsoft.DataApiBuilder";
    private const string NuGetApiUrl = "https://api.nuget.org/v3-flatcontainer/{0}/index.json";

    public static (string? LatestVersion, string? CurrentVersion) GetVersions()
    {
        var latestVersion = FetchLatestNuGetVersion();
        var currentVersion = GetCurrentVersionFromAssembly(Assembly.GetExecutingAssembly());
        return (latestVersion, currentVersion);
    }

    private static string? FetchLatestNuGetVersion()
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) }; 
            var url = string.Format(NuGetApiUrl, PackageName.ToLower());
            var versionData = httpClient.GetFromJsonAsync<NuGetVersionResponse>(url).GetAwaiter().GetResult();

            return versionData?.Versions
                ?.Where(v => !v.Contains("-rc"))
                .Max(); // Get the latest stable version
        }
        catch
        {
            return null; // Assume no update available on failure
        }
    }

    private static string? GetCurrentVersionFromAssembly(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return !string.IsNullOrEmpty(version) ? version.Split('+')[0] : assembly.GetName().Version?.ToString();
    }

    private class NuGetVersionResponse
    {
        [JsonPropertyName("versions")]
        public string[]? Versions { get; set; }
    }
}
