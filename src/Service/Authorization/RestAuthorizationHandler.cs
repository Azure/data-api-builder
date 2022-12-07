using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Authorization
{
    /// <summary>
    /// Make authorization decisions for REST requests.
    /// Checks a requirement against an object(resource) to decide whether
    /// a request should continue processing.
    /// </summary>
    public class RestAuthorizationHandler : IAuthorizationHandler
    {
        private IAuthorizationResolver _authorizationResolver;
        private IHttpContextAccessor _contextAccessor;
        private readonly ILogger<IAuthorizationHandler> _logger;

        public RestAuthorizationHandler(
            IAuthorizationResolver authorizationResolver,
            IHttpContextAccessor httpContextAccessor,
            ILogger<IAuthorizationHandler> logger
            )
        {
            _authorizationResolver = authorizationResolver;
            _contextAccessor = httpContextAccessor;
            _logger = logger;
        }

        /// <summary>
        /// Executed by the internal ASP.NET authorization engine
        /// whenever client code calls authorizationService.AuthorizeAsync()
        /// Execution of this method calls either 
        ///     .Succeed(IRequirement)
        ///     .Failure()
        /// To set the result of authorization. If Faiure() is called, this
        /// ensures Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext.HasSucceeded
        //  will never return true.
        /// </summary>
        /// <param name="context">Contains the requirement and object(resource)
        /// to determine an authorization decision. </param>
        /// <returns>No object is returned.</returns>
        /// <exception cref="DataApiBuilderException"></exception>
        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            // Catch clause to ensure multiple requirements are not sent at one time, to ensure
            // that requirements are evaluated in order, and fail the request upon first requirement failure.
            // Order not maintained by pendingRequirements as ASP.NET Core implementation is HashSet.
            // This will prevent extraneous computation on later authorization steps that shouldn't occur for a request
            // that has already been evaluated as Unauthorized.
            if (context.PendingRequirements.Count() > 1)
            {
                throw new DataApiBuilderException(
                    message: "Multiple requirements are not supported.",
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
                );
            }

            HttpContext? httpContext = _contextAccessor.HttpContext;

            if (httpContext is null)
            {
                throw new DataApiBuilderException(
                    message: "HTTP Context Unavailable, Something went wrong",
                    statusCode: System.Net.HttpStatusCode.Unauthorized,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError
                    );
            }

            // DataApiBuilder requires that only 1 requirement be processed at a time.
            IAuthorizationRequirement requirement = context.PendingRequirements.First();

            if (requirement is RoleContextPermissionsRequirement)
            {
                if (_authorizationResolver.IsValidRoleContext(httpContext))
                {
                    context.Succeed(requirement);
                }
            }
            else if (requirement is EntityRoleOperationPermissionsRequirement)
            {
                if (context.Resource is not null)
                {
                    string? entityName = context.Resource as string;

                    if (entityName is null)
                    {
                        throw new DataApiBuilderException(
                            message: "restContext Resource Null, Something went wrong",
                            statusCode: HttpStatusCode.Unauthorized,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError
                        );
                    }

                    string roleName = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
                    IEnumerable<Config.Operation> operations = HttpVerbToOperations(httpContext.Request.Method);

                    foreach (Config.Operation operation in operations)
                    {
                        bool isAuthorized = _authorizationResolver.AreRoleAndOperationDefinedForEntity(entityName, roleName, operation);
                        if (!isAuthorized)
                        {
                            context.Fail();
                            break;
                        }
                    }

                    // All requirement checks must pass.
                    if (!context.HasFailed)
                    {
                        context.Succeed(requirement);
                    }
                }
                else
                {
                    context.Fail();
                }
            }
            else if (requirement is ColumnsPermissionsRequirement)
            {
                if (context.Resource is not null)
                {
                    RestRequestContext? restContext = context.Resource as RestRequestContext;

                    if (restContext is null)
                    {
                        throw new DataApiBuilderException(
                            message: "restContext Resource Null, Something went wrong",
                            statusCode: HttpStatusCode.Unauthorized,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError
                        );
                    }

                    string entityName = restContext.EntityName;
                    string roleName = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
                    IEnumerable<Config.Operation> operations = HttpVerbToOperations(httpContext.Request.Method);

                    // Delete operations do not have column level restrictions.
                    // If the operation is allowed for the role, the column requirement is implicitly successful,
                    // and the authorization check can be short circuited here.
                    if (operations.Count() == 1 && operations.Contains(Config.Operation.Delete))
                    {
                        context.Succeed(requirement);
                        return Task.CompletedTask;
                    }

                    // Attempts to get list of unique columns present in request metadata.
                    restContext.CalculateCumulativeColumns(_logger);

                    // Two operations must be checked when HTTP operation is PUT or PATCH,
                    // otherwise, just one operation is checked.
                    // PUT and PATCH resolve to operations 'Create' and 'Update'.
                    // A user must fulfill all operations' permissions requirements to proceed.
                    foreach (Config.Operation operation in operations)
                    {
                        // Get a list of all columns present in a request that need to be authorized.
                        IEnumerable<string> columnsToCheck = restContext.CumulativeColumns;

                        // When Issue #XX for REST Column Aliases is merged, the request field names(which may be aliases)
                        // will be converted to field names denoted in the permissions config.
                        // i.e. columnsToCheck = convertExposedNamesToBackingColumns()

                        // Authorize field names present in a request.
                        if (columnsToCheck.Count() > 0 && _authorizationResolver.AreColumnsAllowedForOperation(entityName, roleName, operation, columnsToCheck))
                        {
                            // Find operations with no column filter in the query string will have FieldsToBeReturned == 0.
                            // Then, the "allowed columns" resolved, will be set on FieldsToBeReturned.
                            // When FieldsToBeReturned is originally >=1 column, the field is NOT modified here.
                            if (restContext.FieldsToBeReturned.Count == 0 && restContext.OperationType == Config.Operation.Read)
                            {
                                // Union performed to avoid duplicate field names in FieldsToBeReturned.
                                IEnumerable<string> fieldsReturnedForFind = _authorizationResolver.GetAllowedExposedColumns(entityName, roleName, operation);
                                restContext.UpdateReturnFields(fieldsReturnedForFind);
                            }
                        }
                        else if (columnsToCheck.Count() == 0 && restContext.OperationType is Config.Operation.Read)
                        {
                            // - Find operations typically return all metadata of a database record.
                            // This check resolves all 'included' columns defined in permissions
                            // so only those included columns are present in the result(s).
                            // - For other operation types, columnsToCheck is a result of identifying
                            // any reference to a column in all parts of a request (body, URL, querystring)
                            IEnumerable<string> fieldsReturnedForFind = _authorizationResolver.GetAllowedExposedColumns(entityName, roleName, operation);
                            if (fieldsReturnedForFind.Count() == 0)
                            {
                                // READ operations with no accessible fields fail authorization.
                                context.Fail();
                            }

                            restContext.UpdateReturnFields(fieldsReturnedForFind);
                        }
                        else
                        {
                            context.Fail();
                        }
                    }

                    if (!context.HasFailed)
                    {
                        context.Succeed(requirement);
                    }
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Converts httpverb type of a RestRequestContext object to the
        /// matching CRUD operation(s), to facilitate authorization checks.
        /// </summary>
        /// <param name="httpVerb"></param>
        /// <returns>A collection of Operation types resolved from the http verb type of the request.</returns>
        private static IEnumerable<Config.Operation> HttpVerbToOperations(string httpVerb)
        {
            switch (httpVerb)
            {
                case HttpConstants.POST:
                    return new List<Config.Operation>(new Config.Operation[] { Config.Operation.Create });
                case HttpConstants.PUT:
                case HttpConstants.PATCH:
                    return new List<Config.Operation>(new Config.Operation[] { Config.Operation.Create, Config.Operation.Update });
                case HttpConstants.DELETE:
                    return new List<Config.Operation>(new Config.Operation[] { Config.Operation.Delete });
                case HttpConstants.GET:
                    return new List<Config.Operation>(new Config.Operation[] { Config.Operation.Read });
                default:
                    throw new DataApiBuilderException(
                        message: "Unsupported operation type.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest
                    );
            }
        }
    }
}
