// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.OpenApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OpenApiIntegration;

[TestClass]
public class SecuritySchemeTests
{
    [TestMethod]
    public void AddCustomHeadersToOperation_AddsRoleHeaderButNotAuthorizationHeader()
    {
        OpenApiOperation operation = new()
        {
            Parameters = new List<OpenApiParameter>()
        };

        OpenApiDocumentor.AddCustomHeadersToOperation(operation);

        Assert.IsFalse(operation.Parameters.Any(param =>
            param.In is ParameterLocation.Header &&
            AuthorizationResolver.AUTHORIZATION_HEADER.Equals(param.Name)));

        Assert.IsTrue(operation.Parameters.Any(param =>
            param.In is ParameterLocation.Header &&
            AuthorizationResolver.CLIENT_ROLE_HEADER.Equals(param.Name) &&
            param.Schema?.Type.Equals("string") is true &&
            param.Required is false));
    }

    [DataTestMethod]
    [DataRow("AppService")]
    [DataRow("StaticWebApps")]
    [DataRow("Simulator")]
    [DataRow("CustomJwt")]
    public void AddAuthenticationSecurity_AddsBearerSecuritySchemeForAuthenticatedProviders(string provider)
    {
        OpenApiDocument document = new()
        {
            Components = new OpenApiComponents()
        };

        OpenApiDocumentor.AddAuthenticationSecurity(document, new AuthenticationOptions(Provider: provider));

        Assert.IsNotNull(document.Components.SecuritySchemes);
        Assert.AreEqual(1, document.Components.SecuritySchemes.Count);

        OpenApiSecurityScheme securityScheme = document.Components.SecuritySchemes[OpenApiDocumentor.BEARER_AUTH_SECURITY_SCHEME];
        Assert.AreEqual(SecuritySchemeType.Http, securityScheme.Type);
        Assert.AreEqual("bearer", securityScheme.Scheme);
        Assert.AreEqual("JWT", securityScheme.BearerFormat);
        Assert.AreEqual(ParameterLocation.Header, securityScheme.In);
        Assert.AreEqual(AuthorizationResolver.AUTHORIZATION_HEADER, securityScheme.Name);

        Assert.IsNotNull(document.SecurityRequirements);
        Assert.AreEqual(1, document.SecurityRequirements.Count);
        Assert.IsTrue(document.SecurityRequirements.Single().Keys.Any(scheme =>
            scheme.Reference?.Type is ReferenceType.SecurityScheme &&
            OpenApiDocumentor.BEARER_AUTH_SECURITY_SCHEME.Equals(scheme.Reference.Id)));
    }

    [TestMethod]
    public void AddAuthenticationSecurity_DoesNotAddBearerSecuritySchemeForUnauthenticatedProvider()
    {
        OpenApiDocument document = new()
        {
            Components = new OpenApiComponents()
        };

        OpenApiDocumentor.AddAuthenticationSecurity(document, new AuthenticationOptions(Provider: AuthenticationOptions.UNAUTHENTICATED_AUTHENTICATION));

        Assert.IsTrue(document.Components.SecuritySchemes is null || document.Components.SecuritySchemes.Count is 0);
        Assert.IsTrue(document.SecurityRequirements is null || document.SecurityRequirements.Count is 0);
    }

    [TestMethod]
    public void AddAuthenticationSecurity_DoesNotAddBearerSecuritySchemeWhenAuthenticationOptionsAreNull()
    {
        OpenApiDocument document = new()
        {
            Components = new OpenApiComponents()
        };

        OpenApiDocumentor.AddAuthenticationSecurity(document, authenticationOptions: null);

        Assert.IsTrue(document.Components.SecuritySchemes is null || document.Components.SecuritySchemes.Count is 0);
        Assert.IsTrue(document.SecurityRequirements is null || document.SecurityRequirements.Count is 0);
    }
}
