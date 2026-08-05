// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Auth;

/// <summary>
/// Resolves database authorization policies while keeping claim values separate
/// from policy text.
/// </summary>
public interface IResolvedDatabasePolicyProvider
{
    /// <summary>
    /// Resolves claim references to parameter aliases and returns their typed values separately.
    /// </summary>
    /// <param name="entityName">Entity from the request.</param>
    /// <param name="roleName">Role defined in the client role header.</param>
    /// <param name="operation">Operation type: Create, Read, Update, Delete.</param>
    /// <param name="httpContext">Contains the authenticated user's token claims.</param>
    /// <returns>The policy text and typed claim values to bind to it.</returns>
    public ResolvedDatabasePolicy ResolveDBPolicy(
        string entityName,
        string roleName,
        EntityActionOperation operation,
        HttpContext httpContext);
}
