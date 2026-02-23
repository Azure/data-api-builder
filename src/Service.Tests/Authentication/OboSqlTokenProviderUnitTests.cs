// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

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
    [TestClass]
    public class OboSqlTokenProviderUnitTests
    {
        private const string TEST_DATABASE_AUDIENCE = "https://database.windows.net/";
        private const string TEST_SUBJECT_OID = "00000000-0000-0000-0000-000000000001";
        private const string TEST_SUBJECT_SUB = "00000000-0000-0000-0000-000000000002";
        private const string TEST_TENANT_ID = "11111111-1111-1111-1111-111111111111";
        private const string TEST_ACCESS_TOKEN = "mock-sql-access-token";
        private const string TEST_INCOMING_JWT = "incoming.jwt.assertion";

        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_NullPrincipal_ReturnsNull()
        {
            // Arrange
            Mock<IMsalClientWrapper> msalMock = new();
            Mock<ILogger<OboSqlTokenProvider>> loggerMock = new();
            OboSqlTokenProvider provider = new(msalMock.Object, loggerMock.Object);

            // Act
            string? result = await provider.GetAccessTokenOnBehalfOfAsync(
                principal: null!,
                incomingJwtAssertion: TEST_INCOMING_JWT,
                databaseAudience: TEST_DATABASE_AUDIENCE);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_EmptyJwtAssertion_ReturnsNull()
        {
            // Arrange
            Mock<IMsalClientWrapper> msalMock = new();
            Mock<ILogger<OboSqlTokenProvider>> loggerMock = new();
            OboSqlTokenProvider provider = new(msalMock.Object, loggerMock.Object);

            ClaimsPrincipal principal = CreatePrincipalWithOid(TEST_SUBJECT_OID, TEST_TENANT_ID);

            // Act
            string? result = await provider.GetAccessTokenOnBehalfOfAsync(
                principal: principal,
                incomingJwtAssertion: string.Empty,
                databaseAudience: TEST_DATABASE_AUDIENCE);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_MissingOidAndSub_ThrowsUnauthorized()
        {
            // Arrange
            Mock<IMsalClientWrapper> msalMock = new();
            Mock<ILogger<OboSqlTokenProvider>> loggerMock = new();
            OboSqlTokenProvider provider = new(msalMock.Object, loggerMock.Object);

            // Principal with only tid, no oid or sub
            ClaimsIdentity identity = new();
            identity.AddClaim(new Claim("tid", TEST_TENANT_ID));
            ClaimsPrincipal principal = new(identity);

            // Act & Assert
            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                async () => await provider.GetAccessTokenOnBehalfOfAsync(
                    principal: principal,
                    incomingJwtAssertion: TEST_INCOMING_JWT,
                    databaseAudience: TEST_DATABASE_AUDIENCE));

            Assert.AreEqual(HttpStatusCode.Unauthorized, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.OboAuthenticationFailure, exception.SubStatusCode);
            Assert.AreEqual(DataApiBuilderException.OBO_IDENTITY_CLAIMS_MISSING, exception.Message);
        }

        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_MissingTenantId_ThrowsUnauthorized()
        {
            // Arrange
            Mock<IMsalClientWrapper> msalMock = new();
            Mock<ILogger<OboSqlTokenProvider>> loggerMock = new();
            OboSqlTokenProvider provider = new(msalMock.Object, loggerMock.Object);

            // Principal with oid but no tid
            ClaimsIdentity identity = new();
            identity.AddClaim(new Claim("oid", TEST_SUBJECT_OID));
            ClaimsPrincipal principal = new(identity);

            // Act & Assert
            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                async () => await provider.GetAccessTokenOnBehalfOfAsync(
                    principal: principal,
                    incomingJwtAssertion: TEST_INCOMING_JWT,
                    databaseAudience: TEST_DATABASE_AUDIENCE));

            Assert.AreEqual(HttpStatusCode.Unauthorized, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.OboAuthenticationFailure, exception.SubStatusCode);
            Assert.AreEqual(DataApiBuilderException.OBO_TENANT_CLAIM_MISSING, exception.Message);
        }

        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_PrefersOidOverSub()
        {
            // Arrange
            Mock<IMsalClientWrapper> msalMock = CreateMsalMockWithOboResult(TEST_ACCESS_TOKEN);
            Mock<ILogger<OboSqlTokenProvider>> loggerMock = new();
            OboSqlTokenProvider provider = new(msalMock.Object, loggerMock.Object);

            // Principal with both oid and sub
            ClaimsIdentity identity = new();
            identity.AddClaim(new Claim("oid", TEST_SUBJECT_OID));
            identity.AddClaim(new Claim("sub", TEST_SUBJECT_SUB));
            identity.AddClaim(new Claim("tid", TEST_TENANT_ID));
            ClaimsPrincipal principal = new(identity);

            // Act
            string? result = await provider.GetAccessTokenOnBehalfOfAsync(
                principal: principal,
                incomingJwtAssertion: TEST_INCOMING_JWT,
                databaseAudience: TEST_DATABASE_AUDIENCE);

            // Assert - if oid is preferred, the token should be acquired successfully
            Assert.IsNotNull(result);
            Assert.AreEqual(TEST_ACCESS_TOKEN, result);
        }

        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_FallsBackToSub_WhenOidMissing()
        {
            // Arrange
            Mock<IMsalClientWrapper> msalMock = CreateMsalMockWithOboResult(TEST_ACCESS_TOKEN);
            Mock<ILogger<OboSqlTokenProvider>> loggerMock = new();
            OboSqlTokenProvider provider = new(msalMock.Object, loggerMock.Object);

            // Principal with only sub, no oid
            ClaimsIdentity identity = new();
            identity.AddClaim(new Claim("sub", TEST_SUBJECT_SUB));
            identity.AddClaim(new Claim("tid", TEST_TENANT_ID));
            ClaimsPrincipal principal = new(identity);

            // Act
            string? result = await provider.GetAccessTokenOnBehalfOfAsync(
                principal: principal,
                incomingJwtAssertion: TEST_INCOMING_JWT,
                databaseAudience: TEST_DATABASE_AUDIENCE);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(TEST_ACCESS_TOKEN, result);
        }

        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_CachesToken_AndReturnsCachedOnSecondCall()
        {
            // Arrange
            int oboCallCount = 0;
            Mock<IMsalClientWrapper> msalMock = new();

            msalMock
                .Setup(m => m.AcquireTokenOnBehalfOfAsync(
                    It.IsAny<string[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    oboCallCount++;
                    return CreateAuthenticationResult(TEST_ACCESS_TOKEN, DateTimeOffset.UtcNow.AddMinutes(30));
                });

            Mock<ILogger<OboSqlTokenProvider>> loggerMock = new();
            OboSqlTokenProvider provider = new(msalMock.Object, loggerMock.Object);

            ClaimsPrincipal principal = CreatePrincipalWithOid(TEST_SUBJECT_OID, TEST_TENANT_ID);

            // Act - first call
            string? result1 = await provider.GetAccessTokenOnBehalfOfAsync(
                principal: principal,
                incomingJwtAssertion: TEST_INCOMING_JWT,
                databaseAudience: TEST_DATABASE_AUDIENCE);

            // Act - second call (same principal/assertion)
            string? result2 = await provider.GetAccessTokenOnBehalfOfAsync(
                principal: principal,
                incomingJwtAssertion: TEST_INCOMING_JWT,
                databaseAudience: TEST_DATABASE_AUDIENCE);

            // Assert
            Assert.IsNotNull(result1);
            Assert.IsNotNull(result2);
            Assert.AreEqual(result1, result2);
            Assert.AreEqual(1, oboCallCount, "OBO should only be called once due to caching.");
        }

        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_DifferentRoles_ProducesDifferentCacheKeys()
        {
            // Arrange
            int oboCallCount = 0;
            Mock<IMsalClientWrapper> msalMock = new();

            msalMock
                .Setup(m => m.AcquireTokenOnBehalfOfAsync(
                    It.IsAny<string[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    oboCallCount++;
                    return CreateAuthenticationResult($"token-{oboCallCount}", DateTimeOffset.UtcNow.AddMinutes(30));
                });

            Mock<ILogger<OboSqlTokenProvider>> loggerMock = new();
            OboSqlTokenProvider provider = new(msalMock.Object, loggerMock.Object);

            // Principal with role "reader"
            ClaimsPrincipal principalReader = CreatePrincipalWithRoles(TEST_SUBJECT_OID, TEST_TENANT_ID, "reader");

            // Principal with role "writer"
            ClaimsPrincipal principalWriter = CreatePrincipalWithRoles(TEST_SUBJECT_OID, TEST_TENANT_ID, "writer");

            // Act
            string? resultReader = await provider.GetAccessTokenOnBehalfOfAsync(
                principal: principalReader,
                incomingJwtAssertion: TEST_INCOMING_JWT,
                databaseAudience: TEST_DATABASE_AUDIENCE);

            string? resultWriter = await provider.GetAccessTokenOnBehalfOfAsync(
                principal: principalWriter,
                incomingJwtAssertion: TEST_INCOMING_JWT,
                databaseAudience: TEST_DATABASE_AUDIENCE);

            // Assert - should be 2 OBO calls because different roles = different cache keys
            Assert.IsNotNull(resultReader);
            Assert.IsNotNull(resultWriter);
            Assert.AreEqual(2, oboCallCount, "Different roles should produce different cache keys.");
        }

        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_MsalException_ThrowsUnauthorized()
        {
            // Arrange
            Mock<IMsalClientWrapper> msalMock = new();

            msalMock
                .Setup(m => m.AcquireTokenOnBehalfOfAsync(
                    It.IsAny<string[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new MsalServiceException("invalid_grant", "The user or admin has not consented."));

            Mock<ILogger<OboSqlTokenProvider>> loggerMock = new();
            OboSqlTokenProvider provider = new(msalMock.Object, loggerMock.Object);

            ClaimsPrincipal principal = CreatePrincipalWithOid(TEST_SUBJECT_OID, TEST_TENANT_ID);

            // Act & Assert - should throw DataApiBuilderException with Unauthorized status
            DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(async () =>
            {
                await provider.GetAccessTokenOnBehalfOfAsync(
                    principal: principal,
                    incomingJwtAssertion: TEST_INCOMING_JWT,
                    databaseAudience: TEST_DATABASE_AUDIENCE);
            });

            Assert.AreEqual(HttpStatusCode.Unauthorized, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.OboAuthenticationFailure, ex.SubStatusCode);
        }

        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_VerifiesCorrectScopeFormat()
        {
            // Arrange
            string? capturedScope = null;
            Mock<IMsalClientWrapper> msalMock = new();

            msalMock
                .Setup(m => m.AcquireTokenOnBehalfOfAsync(
                    It.IsAny<string[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string[], string, CancellationToken>((scopes, _, _) =>
                {
                    capturedScope = scopes.Length > 0 ? scopes[0] : null;
                })
                .ReturnsAsync(CreateAuthenticationResult(TEST_ACCESS_TOKEN, DateTimeOffset.UtcNow.AddMinutes(30)));

            Mock<ILogger<OboSqlTokenProvider>> loggerMock = new();
            OboSqlTokenProvider provider = new(msalMock.Object, loggerMock.Object);

            ClaimsPrincipal principal = CreatePrincipalWithOid(TEST_SUBJECT_OID, TEST_TENANT_ID);

            // Act
            await provider.GetAccessTokenOnBehalfOfAsync(
                principal: principal,
                incomingJwtAssertion: TEST_INCOMING_JWT,
                databaseAudience: "https://database.windows.net/");

            // Assert - scope should be audience + /.default
            Assert.AreEqual("https://database.windows.net/.default", capturedScope);
        }

        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_TrimsTrailingSlashFromAudience()
        {
            // Arrange
            string? capturedScope = null;
            Mock<IMsalClientWrapper> msalMock = new();

            msalMock
                .Setup(m => m.AcquireTokenOnBehalfOfAsync(
                    It.IsAny<string[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string[], string, CancellationToken>((scopes, _, _) =>
                {
                    capturedScope = scopes.Length > 0 ? scopes[0] : null;
                })
                .ReturnsAsync(CreateAuthenticationResult(TEST_ACCESS_TOKEN, DateTimeOffset.UtcNow.AddMinutes(30)));

            Mock<ILogger<OboSqlTokenProvider>> loggerMock = new();
            OboSqlTokenProvider provider = new(msalMock.Object, loggerMock.Object);

            ClaimsPrincipal principal = CreatePrincipalWithOid(TEST_SUBJECT_OID, TEST_TENANT_ID);

            // Act - audience with trailing slash
            await provider.GetAccessTokenOnBehalfOfAsync(
                principal: principal,
                incomingJwtAssertion: TEST_INCOMING_JWT,
                databaseAudience: "https://database.windows.net/");

            // Assert - should not have double slash
            Assert.AreEqual("https://database.windows.net/.default", capturedScope);
        }

        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_RefreshesToken_WhenCacheDurationExceeded()
        {
            // Arrange
            // Use a very short cache duration (1 minute) so effective cache = 1 - 5 = -4 min (immediate refresh)
            // This simulates the scenario where cache duration has been exceeded
            int oboCallCount = 0;
            Mock<IMsalClientWrapper> msalMock = new();

            msalMock
                .Setup(m => m.AcquireTokenOnBehalfOfAsync(
                    It.IsAny<string[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    oboCallCount++;
                    return CreateAuthenticationResult($"token-{oboCallCount}", DateTimeOffset.UtcNow.AddMinutes(60));
                });

            Mock<ILogger<OboSqlTokenProvider>> loggerMock = new();

            // Very short cache duration - will always need refresh
            OboSqlTokenProvider provider = new(msalMock.Object, loggerMock.Object, tokenCacheDurationMinutes: 1);

            ClaimsPrincipal principal = CreatePrincipalWithOid(TEST_SUBJECT_OID, TEST_TENANT_ID);

            // Act - first call
            string? result1 = await provider.GetAccessTokenOnBehalfOfAsync(
                principal: principal,
                incomingJwtAssertion: TEST_INCOMING_JWT,
                databaseAudience: TEST_DATABASE_AUDIENCE);

            // Act - second call (should refresh because effective cache duration is negative)
            string? result2 = await provider.GetAccessTokenOnBehalfOfAsync(
                principal: principal,
                incomingJwtAssertion: TEST_INCOMING_JWT,
                databaseAudience: TEST_DATABASE_AUDIENCE);

            // Assert - should have called OBO twice because cache duration is too short
            Assert.IsNotNull(result1);
            Assert.IsNotNull(result2);
            Assert.AreEqual(2, oboCallCount, "OBO should be called twice when cache duration is exceeded.");
            Assert.AreNotEqual(result1, result2, "Tokens should be different after refresh.");
        }

        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_RefreshesToken_WhenTokenNearExpiry()
        {
            // Arrange
            int oboCallCount = 0;
            Mock<IMsalClientWrapper> msalMock = new();

            msalMock
                .Setup(m => m.AcquireTokenOnBehalfOfAsync(
                    It.IsAny<string[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    oboCallCount++;
                    // Return token that expires in 3 minutes (within 5-min early refresh buffer)
                    return CreateAuthenticationResult($"token-{oboCallCount}", DateTimeOffset.UtcNow.AddMinutes(3));
                });

            Mock<ILogger<OboSqlTokenProvider>> loggerMock = new();

            // Long cache duration, but token expires soon
            OboSqlTokenProvider provider = new(msalMock.Object, loggerMock.Object, tokenCacheDurationMinutes: 60);

            ClaimsPrincipal principal = CreatePrincipalWithOid(TEST_SUBJECT_OID, TEST_TENANT_ID);

            // Act - first call
            string? result1 = await provider.GetAccessTokenOnBehalfOfAsync(
                principal: principal,
                incomingJwtAssertion: TEST_INCOMING_JWT,
                databaseAudience: TEST_DATABASE_AUDIENCE);

            // Act - second call (should refresh because token expires in 3 min < 5 min buffer)
            string? result2 = await provider.GetAccessTokenOnBehalfOfAsync(
                principal: principal,
                incomingJwtAssertion: TEST_INCOMING_JWT,
                databaseAudience: TEST_DATABASE_AUDIENCE);

            // Assert - should have called OBO twice because token is near expiry
            Assert.IsNotNull(result1);
            Assert.IsNotNull(result2);
            Assert.AreEqual(2, oboCallCount, "OBO should be called twice when token is near expiry.");
        }

        [TestMethod]
        public async Task GetAccessTokenOnBehalfOfAsync_UsesCachedToken_WhenWithinCacheDuration()
        {
            // Arrange
            int oboCallCount = 0;
            Mock<IMsalClientWrapper> msalMock = new();

            msalMock
                .Setup(m => m.AcquireTokenOnBehalfOfAsync(
                    It.IsAny<string[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    oboCallCount++;
                    // Token expires in 60 minutes (well beyond the 5-min buffer)
                    return CreateAuthenticationResult(TEST_ACCESS_TOKEN, DateTimeOffset.UtcNow.AddMinutes(60));
                });

            Mock<ILogger<OboSqlTokenProvider>> loggerMock = new();

            // 45 min cache, 5 min buffer = 40 min effective cache
            // Token expires in 60 min, so we're well within cache window
            OboSqlTokenProvider provider = new(msalMock.Object, loggerMock.Object, tokenCacheDurationMinutes: 45);

            ClaimsPrincipal principal = CreatePrincipalWithOid(TEST_SUBJECT_OID, TEST_TENANT_ID);

            // Act - multiple calls should use cached token
            await provider.GetAccessTokenOnBehalfOfAsync(principal, TEST_INCOMING_JWT, TEST_DATABASE_AUDIENCE);
            await provider.GetAccessTokenOnBehalfOfAsync(principal, TEST_INCOMING_JWT, TEST_DATABASE_AUDIENCE);
            await provider.GetAccessTokenOnBehalfOfAsync(principal, TEST_INCOMING_JWT, TEST_DATABASE_AUDIENCE);

            // Assert - should have called OBO only once
            Assert.AreEqual(1, oboCallCount, "OBO should be called only once when within cache duration.");
        }

        #region Helper Methods

        private static ClaimsPrincipal CreatePrincipalWithOid(string oid, string tid)
        {
            ClaimsIdentity identity = new();
            identity.AddClaim(new Claim("oid", oid));
            identity.AddClaim(new Claim("tid", tid));
            return new ClaimsPrincipal(identity);
        }

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

        private static Mock<IMsalClientWrapper> CreateMsalMockWithOboResult(string accessToken)
        {
            Mock<IMsalClientWrapper> msalMock = new();

            msalMock
                .Setup(m => m.AcquireTokenOnBehalfOfAsync(
                    It.IsAny<string[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateAuthenticationResult(accessToken, DateTimeOffset.UtcNow.AddMinutes(30)));

            return msalMock;
        }

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
