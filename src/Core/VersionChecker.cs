using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Core;

public static class VersionChecker
{
    private const string NUGETURL = "https://api.nuget.org/v3/registration5-semver1/azure.dataapibuilder/index.json";

    public static void GetVersions(out string? latestVersion, out string? currentVersion)
    {
        latestVersion = FetchLatestNuGetVersion();
        currentVersion = GetCurrentVersionFromAssembly(Assembly.GetCallingAssembly());
    }

    private static string? FetchLatestNuGetVersion()
    {
        try
        {
            using HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
            NuGetVersionResponse? versionData = httpClient
                .GetFromJsonAsync<NuGetVersionResponse>(NUGETURL)
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

    private static string? GetCurrentVersionFromAssembly(Assembly assembly)
    {
        string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return version is { Length: > 0 } && version.Contains('+')
            ? version[..version.IndexOf('+')] // Slice version string before '+'
            : version ?? assembly.GetName().Version?.ToString();
    }

    private class NuGetVersionResponse
    {
        [JsonPropertyName("versions")]
        public string[]? Versions { get; set; }
    }
}
