// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using AuthenticationOptions = Azure.DataApiBuilder.Config.ObjectModel.AuthenticationOptions;

namespace Azure.DataApiBuilder.Core.Resolvers;

/// <summary>
/// Provides SQL access tokens acquired using On-Behalf-Of (OBO) flow
/// for user-delegated authentication against Microsoft Entra ID.
/// Handles identity extraction, cache key construction, token caching,
/// and early refresh of tokens before expiration.
/// </summary>
public sealed class OboSqlTokenProvider : IOboTokenProvider
{
    private readonly IMsalClientWrapper _msalClient;
    private readonly ILogger<OboSqlTokenProvider> _logger;
    private readonly ConcurrentDictionary<string, CachedToken> _tokenCache = new();

    /// <summary>
    /// Minimum buffer before token expiry to trigger a refresh.
    /// Ensures tokens are refreshed before they expire during active operations.
    /// </summary>
    private const int MIN_EARLY_REFRESH_MINUTES = 5;

    /// <summary>
    /// Number of cache operations before triggering cleanup of expired tokens.
    /// </summary>
    private const int CLEANUP_INTERVAL = 100;

    /// <summary>
    /// Counter for cache operations to trigger periodic cleanup.
    /// </summary>
    private int _cacheOperationCount;

    /// <summary>
    /// Maximum duration to cache tokens before forcing a refresh.
    /// </summary>
    private readonly TimeSpan _tokenCacheDuration;

    /// <summary>
    /// Initializes a new instance of OboSqlTokenProvider.
    /// </summary>
    /// <param name="msalClient">MSAL client wrapper for token acquisition.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="tokenCacheDurationMinutes">
    /// Maximum duration in minutes to cache tokens before forcing a refresh.
    /// Defaults to <see cref="UserDelegatedAuthOptions.DEFAULT_TOKEN_CACHE_DURATION_MINUTES"/>.
    /// </param>
    public OboSqlTokenProvider(
        IMsalClientWrapper msalClient,
        ILogger<OboSqlTokenProvider> logger,
        int tokenCacheDurationMinutes = UserDelegatedAuthOptions.DEFAULT_TOKEN_CACHE_DURATION_MINUTES)
    {
        _msalClient = msalClient ?? throw new ArgumentNullException(nameof(msalClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tokenCacheDuration = TimeSpan.FromMinutes(tokenCacheDurationMinutes);
    }

    /// <inheritdoc />
    public async Task<string?> GetAccessTokenOnBehalfOfAsync(
        ClaimsPrincipal principal,
        string incomingJwtAssertion,
        string databaseAudience,
        CancellationToken cancellationToken = default)
    {
        if (principal is null)
        {
            _logger.LogWarning("Cannot acquire OBO token: ClaimsPrincipal is null.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(incomingJwtAssertion))
        {
            _logger.LogWarning("Cannot acquire OBO token: Incoming JWT assertion is null or empty.");
            return null;
        }

        // Extract identity claims
        string? subjectId = ExtractSubjectId(principal);
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            _logger.LogWarning("Cannot acquire OBO token: Neither 'oid' nor 'sub' claim found in token.");
            throw new DataApiBuilderException(
                message: DataApiBuilderException.OBO_IDENTITY_CLAIMS_MISSING,
                statusCode: HttpStatusCode.Unauthorized,
                subStatusCode: DataApiBuilderException.SubStatusCodes.OboAuthenticationFailure);
        }

        string? tenantId = principal.FindFirst("tid")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning("Cannot acquire OBO token: 'tid' (tenant id) claim not found or empty in token.");
            throw new DataApiBuilderException(
                message: DataApiBuilderException.OBO_TENANT_CLAIM_MISSING,
                statusCode: HttpStatusCode.Unauthorized,
                subStatusCode: DataApiBuilderException.SubStatusCodes.OboAuthenticationFailure);
        }

        string authContextHash = ComputeAuthorizationContextHash(principal);
        string cacheKey = BuildCacheKey(subjectId, tenantId, authContextHash);

        // Check cache for valid token
        // Refresh if: token older than cache duration OR token expires within early refresh buffer
        if (_tokenCache.TryGetValue(cacheKey, out CachedToken? cached) && !ShouldRefresh(cached))
        {
            _logger.LogDebug("OBO token cache hit for subject {SubjectId}.", subjectId);
            return cached.AccessToken;
        }

        // Acquire new token via OBO
        // Note: The incoming JWT assertion has already been validated by ASP.NET Core's JWT Bearer
        // authentication middleware (issuer, audience, expiry, signature). MSAL will also validate
        // the assertion as part of the OBO flow with Azure AD - invalid tokens will result in
        // MsalServiceException which we catch and convert to DataApiBuilderException below.
        try
        {
            string[] scopes = [$"{databaseAudience.TrimEnd('/')}/.default"];

            AuthenticationResult result = await _msalClient.AcquireTokenOnBehalfOfAsync(
                scopes,
                incomingJwtAssertion,
                cancellationToken);

            CachedToken newCachedToken = new(
                AccessToken: result.AccessToken,
                ExpiresOn: result.ExpiresOn,
                CachedAt: DateTimeOffset.UtcNow);

            _tokenCache[cacheKey] = newCachedToken;

            // Periodically clean up expired tokens to prevent unbounded memory growth
            CleanupExpiredTokensIfNeeded();

            _logger.LogDebug(
                "OBO token acquired for subject {SubjectId}, expires at {ExpiresOn}.",
                subjectId,
                result.ExpiresOn);

            return result.AccessToken;
        }
        catch (MsalException ex)
        {
            _logger.LogError(
                ex,
                "Failed to acquire OBO token for subject {SubjectId}. Error: {ErrorCode} - {Message}",
                subjectId,
                ex.ErrorCode,
                ex.Message);
            throw new DataApiBuilderException(
                message: DataApiBuilderException.OBO_TOKEN_ACQUISITION_FAILED,
                statusCode: HttpStatusCode.Unauthorized,
                subStatusCode: DataApiBuilderException.SubStatusCodes.OboAuthenticationFailure,
                innerException: ex);
        }
    }

    /// <summary>
    /// Extracts the subject identifier from the principal.
    /// Prefers 'oid' claim (object ID) over 'sub' claim.
    /// </summary>
    private static string? ExtractSubjectId(ClaimsPrincipal principal)
    {
        string? oid = principal.FindFirst("oid")?.Value;
        if (!string.IsNullOrWhiteSpace(oid))
        {
            return oid;
        }

        return principal.FindFirst("sub")?.Value;
    }

    /// <summary>
    /// Builds a canonical representation of permission-affecting claims (roles and scopes)
    /// and computes a SHA-256 hash for use in the cache key.
    /// </summary>
    private static string ComputeAuthorizationContextHash(ClaimsPrincipal principal)
    {
        List<string> values = [];

        foreach (Claim claim in principal.Claims)
        {
            if (claim.Type.Equals(AuthenticationOptions.ROLE_CLAIM_TYPE, StringComparison.OrdinalIgnoreCase) ||
                claim.Type.Equals("scp", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = claim.Value.Split(
                    [' ', ','],
                    StringSplitOptions.RemoveEmptyEntries);

                foreach (string part in parts)
                {
                    values.Add(part);
                }
            }
        }

        if (values.Count == 0)
        {
            return ComputeSha256Hex(string.Empty);
        }

        values.Sort(StringComparer.OrdinalIgnoreCase);
        string canonical = string.Join("|", values);
        return ComputeSha256Hex(canonical);
    }

    /// <summary>
    /// Computes SHA-256 hash and returns as hex string.
    /// </summary>
    private static string ComputeSha256Hex(string input)
    {
        byte[] data = Encoding.UTF8.GetBytes(input ?? string.Empty);
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Builds the cache key from subject, tenant, and authorization context hash.
    /// Format: subjectId + tenantId + authContextHash (strict concatenation, no separators).
    /// </summary>
    private static string BuildCacheKey(string subjectId, string tenantId, string authContextHash)
    {
        return $"{subjectId}{tenantId}{authContextHash}";
    }

    /// <summary>
    /// Determines if a cached token should be refreshed.
    /// With a cache duration of N minutes and early refresh of M minutes,
    /// the token is refreshed at (N - M) minutes after caching.
    /// This ensures proactive refresh before the cache duration expires.
    /// </summary>
    private bool ShouldRefresh(CachedToken cached)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Refresh at: cachedAt + (cacheDuration - earlyRefreshBuffer)
        // Example: 45 min cache, 5 min buffer → refresh after 40 min
        TimeSpan effectiveCacheDuration = _tokenCacheDuration - TimeSpan.FromMinutes(MIN_EARLY_REFRESH_MINUTES);
        if (now > cached.CachedAt + effectiveCacheDuration)
        {
            return true;
        }

        // Also refresh if token is about to expire (safety check)
        if (now > cached.ExpiresOn.AddMinutes(-MIN_EARLY_REFRESH_MINUTES))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Periodically removes expired tokens from the cache to prevent unbounded memory growth.
    /// Cleanup runs every CLEANUP_INTERVAL cache operations to amortize the cost.
    /// </summary>
    private void CleanupExpiredTokensIfNeeded()
    {
        int count = Interlocked.Increment(ref _cacheOperationCount);
        if (count % CLEANUP_INTERVAL != 0)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        int removedCount = 0;

        foreach (KeyValuePair<string, CachedToken> entry in _tokenCache)
        {
            // Remove tokens that have expired
            if (now > entry.Value.ExpiresOn)
            {
                if (_tokenCache.TryRemove(entry.Key, out _))
                {
                    removedCount++;
                }
            }
        }

        if (removedCount > 0)
        {
            _logger.LogDebug("OBO token cache cleanup: removed {RemovedCount} expired tokens.", removedCount);
        }
    }

    /// <summary>
    /// Represents a cached OBO access token with its expiration and cache timestamps.
    /// </summary>
    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresOn, DateTimeOffset CachedAt);
}
