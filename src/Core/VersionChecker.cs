using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Product;

namespace Azure.DataApiBuilder.Core;

/// <summary>
/// A class to test the local version of the engine against the NuGet version.
/// </summary>
/// <remarks>
/// This is used in startup to suggest upgrading the engine.
/// </remarks>
public static class VersionChecker
{
    private const string NUGETURL = "https://api.nuget.org/v3-flatcontainer/microsoft.dataapibuilder/index.json";

    /// <summary>
    /// Checks if the current local version of the product matches the latest version available on NuGet.
    /// </summary>
    /// <param name="nugetVersion">Outputs the latest version available on NuGet.</param>
    /// <param name="localVersion">Outputs the current local version of the product.</param>
    /// <returns>
    /// Returns <c>true</c> if the local version matches the latest NuGet version or if the NuGet version is not available;
    /// otherwise, returns <c>false</c>.
    /// </returns>
    /// <remarks>
    //  If the internet is unavailable or NuGet is down or the HTTP request fails for any reason
    /// (there is a 2 second Timeout on the request), then the NuGet version will be <c>null</c> and
    //  this method will return <c>true</c>. This is mostly because this check is a user courtesy.
    // </remarks>
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
