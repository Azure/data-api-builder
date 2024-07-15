// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.AuthenticationHelpers;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authentication
{
    /// <summary>
    /// Tests that JwtMiddleware properly return 401 when a JWT token
    /// is not valid nor trusted based on config values and token content.
    /// - Usage of RSASecurityKey to simulate JWT signed with alg: PS256,
    ///   as RSA spec requires the alg in new token signing apps.
    ///   https://datatracker.ietf.org/doc/html/rfc8017#section-8
    /// - Usage of X509Certificate2 to simulate signed tokens that include {kid} claim in JWT header
    ///   This claim is optional, though used in providers like Azure AD.
    /// - Exception language from aspnetcore:
    ///   https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/JwtBearer/src/JwtBearerHandler.cs#L309-L339
    /// </summary>
    [TestClass]
    public class JwtTokenAuthenticationUnitTests
    {
        private const string AUDIENCE = "d727a7e8-1af4-4ce0-8c56-f3107f10bbfd";
        private const string BAD_AUDIENCE = "1337-314159";
        private const string ISSUER = "https://login.microsoftonline.com/291bf275-ea78-4cde-84ea-21309a43a567/v2.0";
        private const string LOCAL_ISSUER = "https://goodissuer.com";
        private const string BAD_ISSUER = "https://badactor.com";
        private const string CHALLENGE_HEADER = "WWW-Authenticate";

        #region Positive Tests

        /// <summary>
        /// JWT is valid as it contains no errors caught by negative tests
        /// library(Microsoft.AspNetCore.Authentication.JwtBearer) validation methods
        /// </summary>
        [DataTestMethod]
        [DataRow(null, DisplayName = "Authenticated role - X-MS-API-ROLE is not sent")]
        [DataRow("author", DisplayName = "Authenticated role - existing X-MS-API-ROLE is honored")]
        [TestMethod]
        public async Task TestValidToken(string clientRoleHeader)
        {
            RsaSecurityKey key = new(RSA.Create(2048));
            string token = CreateJwt(
                audience: AUDIENCE,
                issuer: LOCAL_ISSUER,
                notBefore: DateTime.UtcNow.AddDays(-1),
                expirationTime: DateTime.UtcNow.AddDays(1),
                signingKey: key
                );

            HttpContext postMiddlewareContext =
                await SendRequestAndGetHttpContextState(
                    key,
                    token,
                    clientRoleHeader);
            Assert.IsTrue(postMiddlewareContext.User.Identity.IsAuthenticated);
            Assert.AreEqual(
                expected: (int)HttpStatusCode.OK,
                actual: postMiddlewareContext.Response.StatusCode);
            Assert.AreEqual(
                expected: clientRoleHeader is not null ? clientRoleHeader : AuthorizationType.Authenticated.ToString(),
                actual: postMiddlewareContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER],
                ignoreCase: true);
        }

        /// <summary>
        /// Test to validate that the user request is treated with anonymous role when
        /// the jwt token is missing.
        /// </summary>
        /// <returns></returns>
        [DataTestMethod]
        [DataRow(null, DisplayName = "Anonymous role - X-MS-API-ROLE is not sent")]
        [DataRow("author", DisplayName = "Anonymous role - existing X-MS-API-ROLE is not honored")]
        [TestMethod]
        public async Task TestMissingJwtToken(string clientRoleHeader)
        {
            RsaSecurityKey key = new(RSA.Create(2048));
            string token = null;
            HttpContext postMiddlewareContext
                = await SendRequestAndGetHttpContextState(key, token, clientRoleHeader);
            Assert.IsFalse(postMiddlewareContext.User.Identity.IsAuthenticated);
            Assert.AreEqual(
                expected: (int)HttpStatusCode.OK,
                actual: postMiddlewareContext.Response.StatusCode);
            Assert.AreEqual(
                expected: AuthorizationType.Anonymous.ToString(),
                actual: postMiddlewareContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER],
                ignoreCase: true);
        }

        /// <summary>
        /// JWT is expired and should not be accepted.
        /// </summary>
        [TestMethod]
        public async Task TestInvalidToken_LifetimeExpired()
        {
            RsaSecurityKey key = new(RSA.Create(2048));
            string token = CreateJwt(
                audience: AUDIENCE,
                issuer: LOCAL_ISSUER,
                notBefore: DateTime.UtcNow.AddDays(-2),
                expirationTime: DateTime.UtcNow.AddDays(-1),
                signingKey: key
                );

            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(key, token);
            Assert.AreEqual(
                expected: (int)HttpStatusCode.Unauthorized,
                actual: postMiddlewareContext.Response.StatusCode);
            Assert.IsFalse(postMiddlewareContext.User.Identity.IsAuthenticated);
            StringValues headerValue = GetChallengeHeader(postMiddlewareContext);
            Assert.IsTrue(headerValue[0].Contains("invalid_token") && headerValue[0].Contains($"The token expired at"));
        }

        /// <summary>
        /// JWT notBefore date is in the future.
        /// JWT is not YET valid and causes validation failure.
        /// </summary>
        [TestMethod]
        public async Task TestInvalidToken_NotYetValid()
        {
            RsaSecurityKey key = new(RSA.Create(2048));
            string token = CreateJwt(
                audience: AUDIENCE,
                issuer: LOCAL_ISSUER,
                notBefore: DateTime.UtcNow.AddDays(1),
                expirationTime: DateTime.UtcNow.AddDays(2),
                signingKey: key
                );

            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(key, token);
            Assert.AreEqual(expected: (int)HttpStatusCode.Unauthorized, actual: postMiddlewareContext.Response.StatusCode);
            Assert.IsFalse(postMiddlewareContext.User.Identity.IsAuthenticated);
            StringValues headerValue = GetChallengeHeader(postMiddlewareContext);
            Assert.IsTrue(headerValue[0].Contains("invalid_token") && headerValue[0].Contains($"The token is not valid before"));
        }

        /// <summary>
        /// JWT contains audience not configured in TestServer Authentication options.
        /// Mismatch to configuration causes validation failure.
        /// </summary>
        [TestMethod]
        public async Task TestInvalidToken_BadAudience()
        {
            RsaSecurityKey key = new(RSA.Create(2048));
            string token = CreateJwt(audience: BAD_AUDIENCE, issuer: LOCAL_ISSUER, signingKey: key);

            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(key, token);
            Assert.AreEqual(expected: (int)HttpStatusCode.Unauthorized, actual: postMiddlewareContext.Response.StatusCode);
            Assert.IsFalse(postMiddlewareContext.User.Identity.IsAuthenticated);
            StringValues headerValue = GetChallengeHeader(postMiddlewareContext);
            Assert.IsTrue(headerValue[0].Contains("invalid_token") && headerValue[0].Contains($"The audience '{BAD_AUDIENCE}' is invalid"));
        }

        /// <summary>
        /// JWT contains issuer not configured in TestServer Authentication options.
        /// Mismatch to configuration causes validation failure.
        /// </summary>
        [TestMethod]
        public async Task TestInvalidToken_BadIssuer()
        {
            RsaSecurityKey key = new(RSA.Create(2048));
            string token = CreateJwt(audience: AUDIENCE, issuer: BAD_ISSUER, signingKey: key);

            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(key, token);
            Assert.AreEqual(expected: (int)HttpStatusCode.Unauthorized, actual: postMiddlewareContext.Response.StatusCode);
            Assert.IsFalse(postMiddlewareContext.User.Identity.IsAuthenticated);
            StringValues headerValue = GetChallengeHeader(postMiddlewareContext);
            Assert.IsTrue(headerValue[0].Contains("invalid_token") && headerValue[0].Contains($"The issuer '{BAD_ISSUER}' is invalid"));
        }

        /// <summary>
        /// JWT signed with unrecognized/unconfigured cert.
        /// Resulting in unrecognized (kid) claim value
        /// </summary>
        [TestMethod]
        public async Task TestInvalidToken_InvalidSigningKey()
        {
            X509Certificate2 selfSignedCert = AuthTestCertHelper.CreateSelfSignedCert(hostName: LOCAL_ISSUER);
            X509Certificate2 altCert = AuthTestCertHelper.CreateSelfSignedCert(hostName: BAD_ISSUER);

            SecurityKey key = new X509SecurityKey(selfSignedCert);
            SecurityKey badKey = new X509SecurityKey(altCert);

            string token = CreateJwt(audience: AUDIENCE, issuer: LOCAL_ISSUER, signingKey: badKey);

            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(key, token);
            Assert.AreEqual(expected: (int)HttpStatusCode.Unauthorized, actual: postMiddlewareContext.Response.StatusCode);
            Assert.IsFalse(postMiddlewareContext.User.Identity.IsAuthenticated);
            StringValues headerValue = GetChallengeHeader(postMiddlewareContext);
            Assert.IsTrue(headerValue[0].Contains("invalid_token") && headerValue[0].Contains($"The signature key was not found"));
        }

        /// <summary>
        /// JWT signed with key not registered in the server should result in failed authentication
        /// characterized with an HTTP 401 Unauthorized response due to an "invalid signature" error.
        /// If this test fails, check the console output for the error:
        /// "Bearer was not authenticated. Failure message: IDX10503: Signature validation failed."
        /// </summary>
        [TestMethod("JWT signed with unrecognized/unconfigured key, results in signature key not found")]
        public async Task TestInvalidToken_InvalidSignature()
        {
            // Arrange
            RsaSecurityKey tokenIssuerSigningKey = CreateRsaSigningKeyForTest();

            // Create a JWT token with a signing key differnt than the key
            // used by the server to validate the IssuerSigningKey
            // -> Exercises the ValidateIssuerSigningKey validator
            // https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/wiki/ValidatingTokens
            RsaSecurityKey badKey = CreateRsaSigningKeyForTest();
            string badToken = CreateJwt(audience: AUDIENCE, issuer: LOCAL_ISSUER, signingKey: badKey);

            // Act
            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(
                key: tokenIssuerSigningKey,
                token: badToken);

            // Assert
            Assert.AreEqual(expected: (int)HttpStatusCode.Unauthorized, actual: postMiddlewareContext.Response.StatusCode);
            Assert.IsFalse(postMiddlewareContext.User.Identity.IsAuthenticated);
            StringValues headerValue = GetChallengeHeader(postMiddlewareContext);

            bool isResponseContentValid = headerValue[0].Contains("invalid_token") && headerValue[0].Contains("The signature key was not found");
            Assert.IsTrue(condition: isResponseContentValid, message: "Expected JWT signature validation failure.");
        }

        /// <summary>
        /// JWT with intentionally scrambled signature.
        /// JWT signed with cert adding KID (keyID) claim to token.
        /// Even with valid key, invalid signature still fails validation.
        /// </summary>
        [TestMethod]
        public async Task TestInvalidToken_InvalidSignatureUsingCert()
        {
            X509Certificate2 selfSignedCert = AuthTestCertHelper.CreateSelfSignedCert(hostName: LOCAL_ISSUER);
            SecurityKey key = new X509SecurityKey(selfSignedCert);

            string token = CreateJwt(audience: AUDIENCE, issuer: LOCAL_ISSUER, signingKey: key);
            string tokenForgedSignature = ModifySignature(token, removeSig: false);

            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(key, tokenForgedSignature);
            Assert.AreEqual(expected: (int)HttpStatusCode.Unauthorized, actual: postMiddlewareContext.Response.StatusCode);
            Assert.IsFalse(postMiddlewareContext.User.Identity.IsAuthenticated);
            StringValues headerValue = GetChallengeHeader(postMiddlewareContext);
            Assert.IsTrue(headerValue[0].Contains("invalid_token"));
        }

        /// <summary>
        /// JWT token striped of signature should fail (401) even if all other validation passes.
        /// Challenge header (WWW-Authenticate) only states invalid_token here.
        /// </summary>
        [TestMethod("JWT with no signature should result in 401")]
        public async Task TestInvalidToken_NoSignature()
        {
            RsaSecurityKey key = new(RSA.Create(2048));
            string token = CreateJwt(audience: AUDIENCE, issuer: LOCAL_ISSUER, signingKey: key);
            string tokenNoSignature = ModifySignature(token, removeSig: true);

            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(key, tokenNoSignature);
            Assert.AreEqual(expected: (int)HttpStatusCode.Unauthorized, actual: postMiddlewareContext.Response.StatusCode);
            Assert.IsFalse(postMiddlewareContext.User.Identity.IsAuthenticated);
            StringValues headerValue = GetChallengeHeader(postMiddlewareContext);
            Assert.IsTrue(headerValue[0].Contains("invalid_token"));
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Configures test server with bare minimum middleware
        /// and configures Authentication options with passed in SecurityKey
        /// </summary>
        /// <param name="key"></param>
        /// <returns>IHost</returns>
        private static async Task<IHost> CreateWebHostCustomIssuer(SecurityKey key)
        {
            // Setup RuntimeConfigProvider object for the pipeline.
            MockFileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider runtimeConfigProvider = new(loader);

            return await new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .ConfigureServices(services =>
                        {
                            services.AddAuthentication(defaultScheme: JwtBearerDefaults.AuthenticationScheme)
                                .AddJwtBearer(options =>
                                {
                                    options.Audience = AUDIENCE;
                                    options.TokenValidationParameters = new()
                                    {
                                        // Valiate the JWT Audience (aud) claim
                                        ValidAudience = AUDIENCE,
                                        ValidateAudience = true,

                                        // Validate the JWT Issuer (iss) claim
                                        ValidIssuer = LOCAL_ISSUER,
                                        ValidateIssuer = true,

                                        // The signing key must match
                                        ValidateIssuerSigningKey = true,
                                        IssuerSigningKey = key,

                                        // Lifetime
                                        ValidateLifetime = true
                                    };
                                });
                            services.AddAuthorization();
                            services.AddSingleton(runtimeConfigProvider);
                        })
                        .ConfigureLogging(o =>
                        {
                            o.AddFilter(levelFilter => levelFilter <= LogLevel.Information);
                            o.AddDebug();
                            o.AddConsole();
                        })
                        .Configure(app =>
                        {
                            app.UseAuthentication();
                            app.UseClientRoleHeaderAuthenticationMiddleware();

                            // app.Run acts as terminating middleware to return 200 if we reach it. Without this,
                            // the Middleware pipeline will return 404 by default.
                            app.Run(async (context) =>
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.OK;
                                await context.Response.WriteAsync("Successfully validated token!");
                                await context.Response.StartAsync();
                            });
                        });
                })
                .StartAsync();
        }

        /// <summary>
        /// Creates the TestServer with the minimum middleware setup necessary to
        /// test JwtAuthenticationMiddlware
        /// Sends a request with the passed in token to the TestServer created.
        /// </summary>
        /// <param name="key">The signing key used for TestServer's IssuerSigningKey field.</param>
        /// <param name="token">The JWT value to test against the TestServer</param>
        /// <returns></returns>
        private static async Task<HttpContext> SendRequestAndGetHttpContextState(
            SecurityKey key,
            string token,
            string clientRoleHeader = null)
        {
            using IHost host = await CreateWebHostCustomIssuer(key);
            TestServer server = host.GetTestServer();

            return await server.SendAsync(context =>
            {
                if (token is not null)
                {
                    StringValues headerValue = new(new string[] { $"Bearer {token}" });
                    KeyValuePair<string, StringValues> authHeader = new("Authorization", headerValue);
                    context.Request.Headers.Add(authHeader);
                }

                if (clientRoleHeader is not null)
                {
                    KeyValuePair<string, StringValues> easyAuthHeader =
                        new(AuthorizationResolver.CLIENT_ROLE_HEADER, clientRoleHeader);
                    context.Request.Headers.Add(easyAuthHeader);
                }

                context.Request.Scheme = "https";
            });
        }

        /// <summary>
        /// Creates a JWT token with self signed cert or RSAKey.
        /// Resources:
        /// https://devblogs.microsoft.com/dotnet/jwt-validation-and-authorization-in-asp-net-core/
        /// https://stackoverflow.com/questions/59255124/postman-returns-401-despite-the-valid-token-distributed-for-a-secure-endpoint
        /// https://jasonwatmore.com/post/2020/07/21/aspnet-core-3-create-and-validate-jwt-tokens-use-custom-jwt-middleware
        /// </summary>
        /// <param name="signingCert"></param>
        /// <returns></returns>
        private static string CreateJwt(
            string audience = AUDIENCE,
            string issuer = ISSUER,
            DateTime? notBefore = null,
            DateTime? expirationTime = null,
            SecurityKey signingKey = null)
        {
            JsonWebTokenHandler jsonWebTokenHandler = new();
            SecurityTokenDescriptor tokenDescriptor = new()
            {
                Audience = audience,
                Issuer = issuer,
                Subject = new ClaimsIdentity(new[] { new Claim("id", "1337-314159"), new Claim("userId", "777"), new Claim(ClaimTypes.Name, "ladybird") }),
                NotBefore = notBefore,
                Expires = expirationTime,
                SigningCredentials = new(key: signingKey, algorithm: SecurityAlgorithms.RsaSha256)
            };

            return jsonWebTokenHandler.CreateToken(tokenDescriptor);
        }

        /// <summary>
        /// The JWS representation of a JWT is formatted as: Header.Payload.Signature
        /// RFC: https://www.rfc-editor.org/rfc/rfc7515.html#appendix-A.3.1
        /// 
        /// Scramble or arbitrarily set the Signature value after the second period (.)
        /// This method assumes a properly formatted, but not necessarily valid, JWT.
        /// 
        /// remove(false) -> JWT of form Header.Payload.ModifiedSignature
        /// remove(true) -> JWT of form Header.Payload
        /// </summary>
        /// <param name="token"></param>
        /// <returns>Modified JWT</returns>
        private static string ModifySignature(string token, bool removeSig)
        {
            int headerEnd = token.IndexOf('.');
            int signatureBegin = token.IndexOf('.', headerEnd + 1);

            if (removeSig)
            {
                return token.Remove(signatureBegin);
            }
            else
            {
                return token.Insert(signatureBegin + 1, "abcdefg");
            }
        }

        /// <summary>
        /// Returns the value of the challenge header
        /// index[0] value:
        /// "Bearer error=\"invalid_token\", error_description=\"The audience '1337-314159' is invalid\""
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        private static StringValues GetChallengeHeader(HttpContext context)
        {
            Assert.IsTrue(context.Response.Headers.ContainsKey(CHALLENGE_HEADER));
            return context.Response.Headers[CHALLENGE_HEADER];
        }

        private static RsaSecurityKey CreateRsaSigningKeyForTest()
        {
            RsaSecurityKey key = new(RSA.Create(2048))
            {
                KeyId = Guid.NewGuid().ToString()
            };

            return key;
        }
        #endregion
    }
}
