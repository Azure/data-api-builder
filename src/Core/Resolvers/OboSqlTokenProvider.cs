// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using ZiggyCreatures.Caching.Fusion;
using AuthenticationOptions = Azure.DataApiBuilder.Config.ObjectModel.AuthenticationOptions;

namespace Azure.DataApiBuilder.Core.Resolvers;

/// <summary>
/// Provides SQL access tokens acquired using On-Behalf-Of (OBO) flow
/// for user-delegated authentication against Microsoft Entra ID.
/// Uses FusionCache (L1 in-memory only) for token caching with automatic
/// expiration and eager refresh.
/// </summary>
public sealed class OboSqlTokenProvider : IOboTokenProvider
{
    private readonly IMsalClientWrapper _msalClient;
    private readonly ILogger<OboSqlTokenProvider> _logger;
    private readonly IFusionCache _cache;

    /// <summary>
    /// Cache key prefix for OBO tokens to isolate from other cached data.
    /// </summary>
    private const string CACHE_KEY_PREFIX = "obo:";

    /// <summary>
    /// Eager refresh threshold as a fraction of TTL.
    /// At 0.85, a token cached for 60 minutes will be eagerly refreshed after 51 minutes.
    /// </summary>
    private const float EAGER_REFRESH_THRESHOLD = 0.85f;

    /// <summary>
    /// Minimum buffer before token expiry to trigger a refresh (in minutes).
    /// </summary>
    private const int MIN_EARLY_REFRESH_MINUTES = 5;

    /// <summary>
    /// Initializes a new instance of OboSqlTokenProvider.
    /// </summary>
    /// <param name="msalClient">MSAL client wrapper for token acquisition.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="cache">FusionCache instance for token caching (L1 in-memory only).</param>
    public OboSqlTokenProvider(
        IMsalClientWrapper msalClient,
        ILogger<OboSqlTokenProvider> logger,
        IFusionCache cache)
    {
        _msalClient = msalClient ?? throw new ArgumentNullException(nameof(msalClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
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

        try
        {
            string[] scopes = [$"{databaseAudience.TrimEnd('/')}/.default"];

            // Track whether we had a cache hit for logging
            bool wasCacheMiss = false;

            // Use FusionCache GetOrSetAsync with factory pattern
            // The factory is only called on cache miss
            string? accessToken = await _cache.GetOrSetAsync<string>(
                key: cacheKey,
                factory: async (ctx, ct) =>
                {
                    wasCacheMiss = true;
                    _logger.LogInformation(
                        "OBO token cache MISS for subject {SubjectId} (tenant: {TenantId}). Acquiring new token from Azure AD.",
                        subjectId,
                        tenantId);

                    AuthenticationResult result = await _msalClient.AcquireTokenOnBehalfOfAsync(
                        scopes,
                        incomingJwtAssertion,
                        ct);

                    // Calculate TTL based on token expiry with early refresh buffer
                    TimeSpan tokenLifetime = result.ExpiresOn - DateTimeOffset.UtcNow;
                    TimeSpan cacheDuration = tokenLifetime - TimeSpan.FromMinutes(MIN_EARLY_REFRESH_MINUTES);

                    // Ensure minimum cache duration of 1 minute
                    if (cacheDuration < TimeSpan.FromMinutes(1))
                    {
                        cacheDuration = TimeSpan.FromMinutes(1);
                    }

                    // Set the cache duration based on actual token expiry
                    ctx.Options.SetDuration(cacheDuration);

                    // Enable eager refresh - token will be refreshed in background at threshold
                    ctx.Options.SetEagerRefresh(EAGER_REFRESH_THRESHOLD);

                    // Ensure tokens stay in L1 only (no distributed cache for security)
                    ctx.Options.SetSkipDistributedCache(true, true);

                    _logger.LogInformation(
                        "OBO token ACQUIRED for subject {SubjectId}. Expires: {ExpiresOn}, Cache TTL: {CacheDuration}.",
                        subjectId,
                        result.ExpiresOn,
                        cacheDuration);

                    return result.AccessToken;
                },
                token: cancellationToken);

            if (!string.IsNullOrEmpty(accessToken) && !wasCacheMiss)
            {
                _logger.LogInformation("OBO token cache HIT for subject {SubjectId}.", subjectId);
            }

            return accessToken;
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
    /// and computes a SHA-512 hash for use in the cache key.
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
            return ComputeSha512Hex(string.Empty);
        }

        values.Sort(StringComparer.OrdinalIgnoreCase);
        string canonical = string.Join("|", values);
        return ComputeSha512Hex(canonical);
    }

    /// <summary>
    /// Computes SHA-512 hash and returns as hex string.
    /// </summary>
    private static string ComputeSha512Hex(string input)
    {
        byte[] data = Encoding.UTF8.GetBytes(input ?? string.Empty);
        byte[] hash = SHA512.HashData(data);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Builds the cache key from subject, tenant, and authorization context hash.
    /// Format: obo:subjectId+tenantId+authContextHash
    /// </summary>
    private static string BuildCacheKey(string subjectId, string tenantId, string authContextHash)
    {
        return $"{CACHE_KEY_PREFIX}{subjectId}{tenantId}{authContextHash}";
    }
}
