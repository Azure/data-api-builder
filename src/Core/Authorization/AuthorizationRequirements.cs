// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Authorization;

namespace Azure.DataApiBuilder.Core.Authorization;

/// <summary>
/// Instructs the authorization handler to check that:
///    - The client role header maps to a role claim on the authenticated user.
///
/// Implements IAuthorizationRequirement, which is an empty marker interface.
/// https://docs.microsoft.com/aspnet/core/security/authorization/policies#requirements
/// </summary>
public class RoleContextPermissionsRequirement : IAuthorizationRequirement { }

/// <summary>
/// Instructs the authorization handler to check that:
///     - The entity has an entry for the role defined in the client role header.
///     - The discovered role entry has an entry for the operationtype of the request.
/// 
/// Implements IAuthorizationRequirement, which is an empty marker interface.
/// https://docs.microsoft.com/aspnet/core/security/authorization/policies#requirements
/// </summary>
public class EntityRoleOperationPermissionsRequirement : IAuthorizationRequirement { }

/// <summary>
/// Instructs the authorization handler to check that:
///     - The columns included in the request are allowed to be accessed by the authenticated user.
/// For requests on *Many requests, restricts the results to only include fields allowed to be
/// accessed by the authenticated user.
///
/// Implements IAuthorizationRequirement, which is an empty marker interface.
/// https://docs.microsoft.com/aspnet/core/security/authorization/policies#requirements
/// </summary>
public class ColumnsPermissionsRequirement : IAuthorizationRequirement { }

/// <summary>
/// Instructs the authorization handler to check that:
///     - The stored procedure that has been requested to execute is allowed to be accessed by the authenticated user.
///
/// Implements IAuthorizationRequirement, which is an empty marker interface.
/// https://docs.microsoft.com/aspnet/core/security/authorization/policies#requirements
/// </summary>
public class StoredProcedurePermissionsRequirement : IAuthorizationRequirement { }
