// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using AuthenticationOptions = Azure.DataApiBuilder.Config.ObjectModel.AuthenticationOptions;

namespace Azure.DataApiBuilder.Service.Tests.Authentication
{
    /// <summary>
    /// Unit tests for <see cref="OboSqlTokenProvider"/> which handles On-Behalf-Of (OBO) token
    /// acquisition for delegated user authentication to Azure SQL Database.
    /// Tests cover: input validation, claim extraction, token caching, scope formatting, and error handling.
    /// </summary>
    [TestClass]
    public class OboSqlTokenProviderUnitTests
    {
        private const string TEST_DATABASE_AUDIENCE = "https://database.windows.net/";
        private const string TEST_SUBJECT_OID = "00000000-0000-0000-0000-000000000001";
        private const string TEST_SUBJECT_SUB = "00000000-0000-0000-0000-000000000002";
        private const string TEST_TENANT_ID = "11111111-1111-1111-1111-111111111111";
        private const string TEST_ACCESS_TOKEN = "mock-sql-access-token";
        private const string TEST_INCOMING_JWT = "incoming.jwt.assertion";

        private Mock<IMsalClientWrapper> _msalMock;
        private Mock<ILogger<OboSqlTokenProvider>> _loggerMock;
        private OboSqlTokenProvider _provider;

        /// <summary>
        /// Initializes mocks and provider before each test.
        /// </summary>
        [TestInitialize]
        public void TestInit()
        {
            _msalMock = new Mock<IMsalClientWrapper>();
            _loggerMock = new Mock<ILogger<OboSqlTokenProvider>>();
            _provider = new OboSqlTokenProvider(_msalMock.Object, _loggerMock.Object);
        }

        #region Input Validation Tests

        /// <summary>
        /// Verifies that null/empty inputs return null without calling MSAL.
        /// </summary>
        [DataTestMethod]
        [DataRow(null, TEST_INCOMING_JWT, DisplayName = "Null principal")]
        [DataRow("valid", "", DisplayName = "Empty JWT assertion")]
        [DataRow("valid", null, DisplayName = "Null JWT assertion")]
        public async Task GetAccessTokenOnBehalfOfAsync_InvalidInput_ReturnsNull(
            string principalMarker, string jwtAssertion)
        {
            // Arrange
            ClaimsPrincipal principal = principalMarker == null
                ? null
                : CreatePrincipalWithOid(TEST_SUBJECT_OID, TEST_TENANT_ID);

            // Act
            string result = await _provider.GetAccessTokenOnBehalfOfAsync(
                principal: principal,
                incomingJwtAssertion: jwtAssertion,
                databaseAudience: TEST_DATABASE_AUDIENCE);

            // Assert
            Assert.IsNull(result);
            _msalMock.Verify(
                m => m.AcquireTokenOnBehalfOfAsync(It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "MSAL should not be called for invalid input.");
        }

        /// <summary>
        /// Verifies that missing required claims (oid/sub or tid) throw DataApiBuilderException.
        /// </summary>
        [DataTestMethod]
        [DataRow(null, null, TEST_TENANT_ID, "OBO_IDENTITY_CLAIMS_MISSING", DisplayName = "Missing oid and sub")]
        [DataRow(TEST_SUBJECT_OID, null, null, "OBO_TENANT_CLAIM_MISSING", DisplayName = "Missing tenant id")]
        public async Task GetAccessTokenOnBehalfOfAsync_MissingRequiredClaims_ThrowsUnauthorized(
            string oid, string sub, string tid, string expectedErrorConstant)
        {
            // Arrange
            ClaimsIdentity identity = new();
            if (oid != null)
            {
                identity.AddClaim(new Claim("oid", oid));
            }

            if (sub != null)
            {
                identity.AddClaim(new Claim("sub", sub));
            }

            if (tid != null)
            {
                identity.AddClaim(new Claim("tid", tid));
            }

            ClaimsPrincipal principal = new(identity);

            // Act & Assert
            DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                async () => await _provider.GetAccessTokenOnBehalfOfAsync(
                    principal: principal,
                    incomingJwtAssertion: TEST_INCOMING_JWT,
                    databaseAudience: TEST_DATABASE_AUDIENCE));

            Assert.AreEqual(HttpStatusCode.Unauthorized, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.OboAuthenticationFailure, ex.SubStatusCode);

            // Verify the correct error message constant is used
            string expectedMessage = expectedErrorConstant == "OBO_IDENTITY_CLAIMS_MISSING"
                ? DataApiBuilderException.OBO_IDENTITY_CLAIMS_MISSING
                : DataApiBuilderException.OBO_TENANT_CLAIM_MISSING;
            Assert.AreEqual(expectedMessage, ex.Message);
        }

        #endregion

        #region Claim Extraction Tests

        /// <summary>
        /// Verifies that 'oid' claim is preferred over 'sub' when both are present.
        /// </summary>
        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_PrefersOidOverSub()
        {
            // Arrange
            SetupMsalSuccess();

            ClaimsIdentity identity = new();
            identity.AddClaim(new Claim("oid", TEST_SUBJECT_OID));
            identity.AddClaim(new Claim("sub", TEST_SUBJECT_SUB));
            identity.AddClaim(new Claim("tid", TEST_TENANT_ID));
            ClaimsPrincipal principal = new(identity);

            // Act
            string result = await _provider.GetAccessTokenOnBehalfOfAsync(
                principal: principal,
                incomingJwtAssertion: TEST_INCOMING_JWT,
                databaseAudience: TEST_DATABASE_AUDIENCE);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(TEST_ACCESS_TOKEN, result);
        }

        /// <summary>
        /// Verifies that 'sub' claim is used when 'oid' is not present.
        /// </summary>
        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_FallsBackToSub_WhenOidMissing()
        {
            // Arrange
            SetupMsalSuccess();

            ClaimsIdentity identity = new();
            identity.AddClaim(new Claim("sub", TEST_SUBJECT_SUB));
            identity.AddClaim(new Claim("tid", TEST_TENANT_ID));
            ClaimsPrincipal principal = new(identity);

            // Act
            string result = await _provider.GetAccessTokenOnBehalfOfAsync(
                principal: principal,
                incomingJwtAssertion: TEST_INCOMING_JWT,
                databaseAudience: TEST_DATABASE_AUDIENCE);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(TEST_ACCESS_TOKEN, result);
        }

        #endregion

        #region Token Caching Tests

        /// <summary>
        /// Verifies that tokens are cached and reused for identical requests.
        /// </summary>
        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_CachesToken_AndReturnsCachedOnSecondCall()
        {
            // Arrange
            CallCounter counter = new();
            OboSqlTokenProvider provider = CreateProviderWithCallCounter(counter);
            ClaimsPrincipal principal = CreatePrincipalWithOid(TEST_SUBJECT_OID, TEST_TENANT_ID);

            // Act
            string result1 = await provider.GetAccessTokenOnBehalfOfAsync(principal, TEST_INCOMING_JWT, TEST_DATABASE_AUDIENCE);
            string result2 = await provider.GetAccessTokenOnBehalfOfAsync(principal, TEST_INCOMING_JWT, TEST_DATABASE_AUDIENCE);

            // Assert
            Assert.IsNotNull(result1);
            Assert.AreEqual(result1, result2);
            Assert.AreEqual(1, counter.Count, "OBO should only be called once due to caching.");
        }

        /// <summary>
        /// Verifies that different roles produce different cache keys.
        /// </summary>
        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_DifferentRoles_ProducesDifferentCacheKeys()
        {
            // Arrange
            CallCounter counter = new();
            OboSqlTokenProvider provider = CreateProviderWithCallCounter(counter, useUniqueTokens: true);

            ClaimsPrincipal principalReader = CreatePrincipalWithRoles(TEST_SUBJECT_OID, TEST_TENANT_ID, "reader");
            ClaimsPrincipal principalWriter = CreatePrincipalWithRoles(TEST_SUBJECT_OID, TEST_TENANT_ID, "writer");

            // Act
            string resultReader = await provider.GetAccessTokenOnBehalfOfAsync(principalReader, TEST_INCOMING_JWT, TEST_DATABASE_AUDIENCE);
            string resultWriter = await provider.GetAccessTokenOnBehalfOfAsync(principalWriter, TEST_INCOMING_JWT, TEST_DATABASE_AUDIENCE);

            // Assert
            Assert.IsNotNull(resultReader);
            Assert.IsNotNull(resultWriter);
            Assert.AreEqual(2, counter.Count, "Different roles should produce different cache keys.");
        }

        /// <summary>
        /// Verifies token refresh behavior based on cache duration and token expiry.
        /// </summary>
        [DataTestMethod]
        [DataRow(1, 60, 2, DisplayName = "Short cache duration forces refresh")]
        [DataRow(60, 3, 2, DisplayName = "Near-expiry token forces refresh")]
        [DataRow(45, 60, 1, DisplayName = "Valid cache and token uses cached value")]
        public async Task GetAccessTokenOnBehalfOfAsync_TokenRefresh_BasedOnCacheAndExpiry(
            int cacheDurationMinutes, int tokenExpiryMinutes, int expectedOboCallCount)
        {
            // Arrange
            CallCounter counter = new();
            OboSqlTokenProvider provider = CreateProviderWithCallCounter(
                counter,
                useUniqueTokens: true,
                tokenExpiryMinutes: tokenExpiryMinutes,
                cacheDurationMinutes: cacheDurationMinutes);
            ClaimsPrincipal principal = CreatePrincipalWithOid(TEST_SUBJECT_OID, TEST_TENANT_ID);

            // Act - make two calls
            await provider.GetAccessTokenOnBehalfOfAsync(principal, TEST_INCOMING_JWT, TEST_DATABASE_AUDIENCE);
            await provider.GetAccessTokenOnBehalfOfAsync(principal, TEST_INCOMING_JWT, TEST_DATABASE_AUDIENCE);

            // Assert
            Assert.AreEqual(expectedOboCallCount, counter.Count);
        }

        #endregion

        #region Scope Formatting Tests

        /// <summary>
        /// Verifies that scope is correctly formatted from audience (with or without trailing slash).
        /// </summary>
        [DataTestMethod]
        [DataRow("https://database.windows.net/", "https://database.windows.net/.default", DisplayName = "With trailing slash")]
        [DataRow("https://database.windows.net", "https://database.windows.net/.default", DisplayName = "Without trailing slash")]
        public async Task GetAccessTokenOnBehalfOfAsync_FormatsScope_CorrectlyFromAudience(
            string databaseAudience, string expectedScope)
        {
            // Arrange
            string capturedScope = null;
            Mock<IMsalClientWrapper> msalMock = new();

            msalMock
                .Setup(m => m.AcquireTokenOnBehalfOfAsync(It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string[], string, CancellationToken>((scopes, _, _) => capturedScope = scopes[0])
                .ReturnsAsync(CreateAuthenticationResult(TEST_ACCESS_TOKEN, DateTimeOffset.UtcNow.AddMinutes(30)));

            OboSqlTokenProvider provider = new(msalMock.Object, _loggerMock.Object);
            ClaimsPrincipal principal = CreatePrincipalWithOid(TEST_SUBJECT_OID, TEST_TENANT_ID);

            // Act
            await provider.GetAccessTokenOnBehalfOfAsync(principal, TEST_INCOMING_JWT, databaseAudience);

            // Assert
            Assert.AreEqual(expectedScope, capturedScope);
        }

        #endregion

        #region Error Handling Tests

        /// <summary>
        /// Verifies that MSAL exceptions are wrapped in DataApiBuilderException with Unauthorized status.
        /// </summary>
        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_MsalException_ThrowsUnauthorized()
        {
            // Arrange
            _msalMock
                .Setup(m => m.AcquireTokenOnBehalfOfAsync(It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new MsalServiceException("invalid_grant", "The user or admin has not consented."));

            ClaimsPrincipal principal = CreatePrincipalWithOid(TEST_SUBJECT_OID, TEST_TENANT_ID);

            // Act & Assert
            DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                async () => await _provider.GetAccessTokenOnBehalfOfAsync(principal, TEST_INCOMING_JWT, TEST_DATABASE_AUDIENCE));

            Assert.AreEqual(HttpStatusCode.Unauthorized, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.OboAuthenticationFailure, ex.SubStatusCode);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Simple counter wrapper to allow incrementing inside lambdas.
        /// </summary>
        private class CallCounter
        {
            public int Count { get; set; }
        }

        /// <summary>
        /// Sets up the class-level MSAL mock to return a successful token acquisition result.
        /// </summary>
        private void SetupMsalSuccess(int tokenExpiryMinutes = 30)
        {
            _msalMock
                .Setup(m => m.AcquireTokenOnBehalfOfAsync(It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateAuthenticationResult(TEST_ACCESS_TOKEN, DateTimeOffset.UtcNow.AddMinutes(tokenExpiryMinutes)));
        }

        /// <summary>
        /// Creates a provider with a mock that tracks call count for verifying caching behavior.
        /// </summary>
        private OboSqlTokenProvider CreateProviderWithCallCounter(
            CallCounter counter,
            bool useUniqueTokens = false,
            int tokenExpiryMinutes = 30,
            int cacheDurationMinutes = 45)
        {
            Mock<IMsalClientWrapper> msalMock = new();

            msalMock
                .Setup(m => m.AcquireTokenOnBehalfOfAsync(It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => counter.Count++)
                .Returns<string[], string, CancellationToken>((_, _, _) =>
                {
                    string token = useUniqueTokens ? $"token-{counter.Count}" : TEST_ACCESS_TOKEN;
                    return Task.FromResult(CreateAuthenticationResult(token, DateTimeOffset.UtcNow.AddMinutes(tokenExpiryMinutes)));
                });

            return new OboSqlTokenProvider(msalMock.Object, _loggerMock.Object, tokenCacheDurationMinutes: cacheDurationMinutes);
        }

        /// <summary>
        /// Creates a ClaimsPrincipal with oid and tid claims.
        /// </summary>
        private static ClaimsPrincipal CreatePrincipalWithOid(string oid, string tid)
        {
            ClaimsIdentity identity = new();
            identity.AddClaim(new Claim("oid", oid));
            identity.AddClaim(new Claim("tid", tid));
            return new ClaimsPrincipal(identity);
        }

        /// <summary>
        /// Creates a ClaimsPrincipal with oid, tid, and role claims.
        /// </summary>
        private static ClaimsPrincipal CreatePrincipalWithRoles(string oid, string tid, params string[] roles)
        {
            ClaimsIdentity identity = new();
            identity.AddClaim(new Claim("oid", oid));
            identity.AddClaim(new Claim("tid", tid));
            foreach (string role in roles)
            {
                identity.AddClaim(new Claim(AuthenticationOptions.ROLE_CLAIM_TYPE, role));
            }

            return new ClaimsPrincipal(identity);
        }

        /// <summary>
        /// Creates a mock AuthenticationResult for testing.
        /// </summary>
        private static AuthenticationResult CreateAuthenticationResult(string accessToken, DateTimeOffset expiresOn)
        {
            return new AuthenticationResult(
                accessToken: accessToken,
                isExtendedLifeTimeToken: false,
                uniqueId: Guid.NewGuid().ToString(),
                expiresOn: expiresOn,
                extendedExpiresOn: expiresOn,
                tenantId: TEST_TENANT_ID,
                account: null,
                idToken: null,
                scopes: new[] { $"{TEST_DATABASE_AUDIENCE}.default" },
                correlationId: Guid.NewGuid());
        }

        #endregion
    }
}
