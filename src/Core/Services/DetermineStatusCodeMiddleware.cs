// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Net;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Execution;

/// <summary>
/// Inspects the IExecutionResult created by HotChocolate and determines the appropriate
/// HTTP status code to return based on the errors in the result.
///
/// By default, HotChocolate returns a 500 status code for all errors.
/// This middleware maps DataApiBuilderException.SubStatusCodes to their appropriate HTTP status codes:
/// - Authentication errors → 401 Unauthorized
/// - Authorization errors → 403 Forbidden
/// - Not found errors → 404 NotFound
/// - Input validation errors → 400 BadRequest
/// - Unsupported feature errors → 501 NotImplemented
/// - Conflict errors → 409 Conflict
/// - Server errors → 500 InternalServerError
/// </summary>
public sealed class DetermineStatusCodeMiddleware(RequestDelegate next)
{
    public async ValueTask InvokeAsync(RequestContext context)
    {
        await next(context).ConfigureAwait(false);

        if (context.Result is OperationResult { Errors.Count: > 0 } singleResult)
        {
            HttpStatusCode? statusCode = DetermineHttpStatusCode(singleResult.Errors);

            if (statusCode is not null)
            {
                ImmutableDictionary<string, object?>.Builder contextData =
                    ImmutableDictionary.CreateBuilder<string, object?>();

                if (singleResult.ContextData is not null)
                {
                    contextData.AddRange(singleResult.ContextData);
                }

                contextData[ExecutionContextData.HttpStatusCode] = statusCode.Value;
                context.Result = singleResult.WithContextData(contextData.ToImmutable());
            }
        }
    }

    /// <summary>
    /// Determines the HTTP status code to return based on the first recognized
    /// DataApiBuilderException.SubStatusCode found in the error collection.
    /// </summary>
    /// <param name="errors">The collection of errors from the GraphQL result.</param>
    /// <returns>The mapped HttpStatusCode, or null if no recognized error code is found.</returns>
    internal static HttpStatusCode? DetermineHttpStatusCode(IReadOnlyList<IError> errors)
    {
        foreach (IError error in errors)
        {
            if (error.Code is not null &&
                Enum.TryParse<DataApiBuilderException.SubStatusCodes>(error.Code, out DataApiBuilderException.SubStatusCodes subStatusCode))
            {
                return subStatusCode switch
                {
                    // 401 Unauthorized
                    DataApiBuilderException.SubStatusCodes.AuthenticationChallenge
                        => HttpStatusCode.Unauthorized,

                    // 403 Forbidden
                    DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed or
                    DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure or
                    DataApiBuilderException.SubStatusCodes.AuthorizationCumulativeColumnCheckFailed
                        => HttpStatusCode.Forbidden,

                    // 404 Not Found
                    DataApiBuilderException.SubStatusCodes.EntityNotFound or
                    DataApiBuilderException.SubStatusCodes.ItemNotFound or
                    DataApiBuilderException.SubStatusCodes.RelationshipNotFound or
                    DataApiBuilderException.SubStatusCodes.RelationshipFieldNotFound or
                    DataApiBuilderException.SubStatusCodes.DataSourceNotFound
                        => HttpStatusCode.NotFound,

                    // 400 Bad Request
                    DataApiBuilderException.SubStatusCodes.BadRequest or
                    DataApiBuilderException.SubStatusCodes.DatabaseInputError or
                    DataApiBuilderException.SubStatusCodes.InvalidIdentifierField or
                    DataApiBuilderException.SubStatusCodes.ErrorProcessingData or
                    DataApiBuilderException.SubStatusCodes.ExposedColumnNameMappingError or
                    DataApiBuilderException.SubStatusCodes.UnsupportedClaimValueType or
                    DataApiBuilderException.SubStatusCodes.ErrorProcessingEasyAuthHeader
                        => HttpStatusCode.BadRequest,

                    // 501 Not Implemented
                    DataApiBuilderException.SubStatusCodes.NotSupported or
                    DataApiBuilderException.SubStatusCodes.GlobalRestEndpointDisabled or
                    DataApiBuilderException.SubStatusCodes.GlobalMcpEndpointDisabled
                        => HttpStatusCode.NotImplemented,

                    // 409 Conflict
                    DataApiBuilderException.SubStatusCodes.OpenApiDocumentAlreadyExists
                        => HttpStatusCode.Conflict,

                    // 500 Internal Server Error (default for server-side errors)
                    _ => HttpStatusCode.InternalServerError,
                };
            }
        }

        return null;
    }
}
