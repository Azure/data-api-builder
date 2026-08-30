// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Core.Authorization;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class GraphQLAuthorizationHandlerCoverageTests
    {
        [TestMethod]
        public void AuthorizeAsync_EvaluatesAuthenticationHeaderRoleAndPolicyBranches()
        {
            Mock<IAuthorizationResolver> resolver = new();
            resolver.Setup(x => x.IsRoleAllowedByDirective("reader", It.IsAny<IReadOnlyList<string>?>()))
                .Returns(true);
            GraphQLAuthorizationHandler handler = new(resolver.Object);
            AuthorizeDirective roleDirective = CreateDirective(new[] { "reader" }, policy: null);
            AuthorizeDirective policyDirective = CreateDirective(new[] { "reader" }, policy: "unsupported-policy");

            Assert.AreEqual(
                AuthorizeResult.NotAuthenticated,
                handler.AuthorizeAsync(CreateContext(authenticated: false, includeHttpContext: false), roleDirective).Result);
            Assert.AreEqual(
                AuthorizeResult.NotAllowed,
                handler.AuthorizeAsync(CreateContext(authenticated: true, includeHttpContext: false), roleDirective).Result);
            Assert.AreEqual(
                AuthorizeResult.NotAllowed,
                handler.AuthorizeAsync(CreateContext(authenticated: true, includeHttpContext: true), roleDirective).Result);
            Assert.AreEqual(
                AuthorizeResult.Allowed,
                handler.AuthorizeAsync(CreateContext(authenticated: true, includeHttpContext: true, role: "reader"), roleDirective).Result);
            Assert.AreEqual(
                AuthorizeResult.NotAllowed,
                handler.AuthorizeAsync(CreateContext(authenticated: true, includeHttpContext: true, role: "reader"), policyDirective).Result);

            resolver.Setup(x => x.IsRoleAllowedByDirective("denied", It.IsAny<IReadOnlyList<string>?>()))
                .Returns(false);
            Assert.AreEqual(
                AuthorizeResult.NotAllowed,
                handler.AuthorizeAsync(CreateContext(authenticated: true, includeHttpContext: true, role: "denied"), roleDirective).Result);
        }

        [TestMethod]
        public void AuthorizeAsync_DirectiveListEvaluatesEveryBranch()
        {
            Mock<IAuthorizationResolver> resolver = new();
            resolver.Setup(x => x.IsRoleAllowedByDirective("reader", It.IsAny<IReadOnlyList<string>?>()))
                .Returns(true);
            resolver.Setup(x => x.IsRoleAllowedByDirective("denied", It.IsAny<IReadOnlyList<string>?>()))
                .Returns(false);
            GraphQLAuthorizationHandler handler = new(resolver.Object);
            AuthorizeDirective roleDirective = CreateDirective(new[] { "reader" }, policy: null);
            AuthorizeDirective policyDirective = CreateDirective(new[] { "reader" }, policy: "unsupported-policy");

            Assert.AreEqual(
                AuthorizeResult.NotAuthenticated,
                handler.AuthorizeAsync(
                    CreateAuthorizationContext(authenticated: false, includeHttpContext: false),
                    new[] { roleDirective }).Result);
            Assert.AreEqual(
                AuthorizeResult.Allowed,
                handler.AuthorizeAsync(
                    CreateAuthorizationContext(authenticated: true, includeHttpContext: false),
                    Array.Empty<AuthorizeDirective>()).Result);
            Assert.AreEqual(
                AuthorizeResult.NotAllowed,
                handler.AuthorizeAsync(
                    CreateAuthorizationContext(authenticated: true, includeHttpContext: false),
                    new[] { roleDirective }).Result);
            Assert.AreEqual(
                AuthorizeResult.NotAllowed,
                handler.AuthorizeAsync(
                    CreateAuthorizationContext(authenticated: true, includeHttpContext: true, role: "denied"),
                    new[] { roleDirective }).Result);
            Assert.AreEqual(
                AuthorizeResult.NotAllowed,
                handler.AuthorizeAsync(
                    CreateAuthorizationContext(authenticated: true, includeHttpContext: true, role: "reader"),
                    new[] { policyDirective }).Result);
            Assert.AreEqual(
                AuthorizeResult.Allowed,
                handler.AuthorizeAsync(
                    CreateAuthorizationContext(authenticated: true, includeHttpContext: true, role: "reader"),
                    new[] { roleDirective, roleDirective }).Result);
        }

        private static IMiddlewareContext CreateContext(bool authenticated, bool includeHttpContext, string? role = null)
        {
            Dictionary<string, object?> contextData = CreateContextData(authenticated, includeHttpContext, role);
            Mock<IMiddlewareContext> context = new();
            context.SetupGet(x => x.ContextData).Returns(contextData);
            return context.Object;
        }

        private static AuthorizationContext CreateAuthorizationContext(bool authenticated, bool includeHttpContext, string? role = null)
        {
            Dictionary<string, object?> contextData = CreateContextData(authenticated, includeHttpContext, role);
            ConstructorInfo constructor = typeof(AuthorizationContext)
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OrderBy(candidate => candidate.GetParameters().Length)
                .First();
            object?[] arguments = constructor.GetParameters()
                .Select(parameter => parameter.Name?.Contains("contextData", StringComparison.OrdinalIgnoreCase) == true ||
                    parameter.ParameterType.IsAssignableFrom(contextData.GetType())
                    ? contextData
                    : parameter.HasDefaultValue
                        ? parameter.DefaultValue
                        : parameter.ParameterType.IsValueType
                            ? Activator.CreateInstance(parameter.ParameterType)
                            : null)
                .ToArray();

            return (AuthorizationContext)constructor.Invoke(arguments);
        }

        private static Dictionary<string, object?> CreateContextData(bool authenticated, bool includeHttpContext, string? role)
        {
            ClaimsIdentity identity = authenticated
                ? new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "test-user") }, "test-authentication")
                : new ClaimsIdentity();
            Dictionary<string, object?> contextData = new()
            {
                [nameof(ClaimsPrincipal)] = new ClaimsPrincipal(identity)
            };

            if (includeHttpContext)
            {
                DefaultHttpContext httpContext = new();
                if (role is not null)
                {
                    httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = role;
                }

                contextData[nameof(HttpContext)] = httpContext;
            }

            return contextData;
        }

        private static AuthorizeDirective CreateDirective(IReadOnlyList<string> roles, string? policy)
        {
            ConstructorInfo constructor = typeof(AuthorizeDirective)
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OrderByDescending(candidate => candidate.GetParameters().Length)
                .First();
            object?[] arguments = constructor.GetParameters()
                .Select(parameter => parameter.Name?.ToLowerInvariant() switch
                {
                    "roles" => roles,
                    "policy" => policy,
                    _ when parameter.HasDefaultValue => parameter.DefaultValue,
                    _ when parameter.ParameterType.IsValueType => Activator.CreateInstance(parameter.ParameterType),
                    _ => null
                })
                .ToArray();

            return (AuthorizeDirective)constructor.Invoke(arguments);
        }
    }
}
