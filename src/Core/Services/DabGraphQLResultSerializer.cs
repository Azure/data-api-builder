// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.AspNetCore.Serialization;
using HotChocolate.Execution;

namespace Azure.DataApiBuilder.Core.Services;

/// <summary>
/// The DabGraphQLResultSerializer inspects the IExecutionResult created by HotChocolate
/// and determines the appropriate HTTP error code to return based on the errors in the result.
/// 
/// By default, without this serializer, HotChocolate will return a 500 status code for errors.
/// This serializer maps DataApiBuilderException.SubStatusCodes to their appropriate HTTP status codes.
/// 
/// For example:
/// - Authentication/Authorization errors will return 401/403
/// - Database input validation errors will return 400 BadRequest
/// - Entity not found errors will return 404 NotFound
/// 
/// This ensures that GraphQL endpoints return appropriate and consistent HTTP status codes 
/// for all types of DataApiBuilderException errors.
/// </summary>
public class DabGraphQLResultSerializer : DefaultHttpResultSerializer
{
    public override HttpStatusCode GetStatusCode(IExecutionResult result)
    {
        if (result is IQueryResult queryResult && queryResult.Errors?.Count > 0)
        {
            // Check if any of the errors are from DataApiBuilderException by looking at error.Code
            foreach (var error in queryResult.Errors)
            {
                if (error.Code != null && 
                    Enum.TryParse<DataApiBuilderException.SubStatusCodes>(error.Code, out var subStatusCode))
                {
                    return MapSubStatusCodeToHttpStatusCode(subStatusCode);
                }
            }
        }

        return base.GetStatusCode(result);
    }

    /// <summary>
    /// Maps DataApiBuilderException.SubStatusCodes to appropriate HTTP status codes.
    /// </summary>
    private static HttpStatusCode MapSubStatusCodeToHttpStatusCode(DataApiBuilderException.SubStatusCodes subStatusCode) => subStatusCode switch
    {
        // Authentication/Authorization errors
        DataApiBuilderException.SubStatusCodes.AuthenticationChallenge
            => HttpStatusCode.Unauthorized, // 401

        DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed or
        DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure or
        DataApiBuilderException.SubStatusCodes.AuthorizationCumulativeColumnCheckFailed
            => HttpStatusCode.Forbidden, // 403

        // Not Found errors
        DataApiBuilderException.SubStatusCodes.EntityNotFound or
        DataApiBuilderException.SubStatusCodes.ItemNotFound or
        DataApiBuilderException.SubStatusCodes.RelationshipNotFound or
        DataApiBuilderException.SubStatusCodes.RelationshipFieldNotFound or
        DataApiBuilderException.SubStatusCodes.DataSourceNotFound
            => HttpStatusCode.NotFound, // 404

        // Bad Request errors
        DataApiBuilderException.SubStatusCodes.BadRequest or
        DataApiBuilderException.SubStatusCodes.DatabaseInputError or
        DataApiBuilderException.SubStatusCodes.InvalidIdentifierField or
        DataApiBuilderException.SubStatusCodes.ErrorProcessingData or
        DataApiBuilderException.SubStatusCodes.ExposedColumnNameMappingError or
        DataApiBuilderException.SubStatusCodes.UnsupportedClaimValueType or
        DataApiBuilderException.SubStatusCodes.ErrorProcessingEasyAuthHeader
            => HttpStatusCode.BadRequest, // 400

        // Not Implemented errors
        DataApiBuilderException.SubStatusCodes.NotSupported or
        DataApiBuilderException.SubStatusCodes.GlobalRestEndpointDisabled
            => HttpStatusCode.NotImplemented, // 501

        // Conflict errors
        DataApiBuilderException.SubStatusCodes.OpenApiDocumentAlreadyExists
            => HttpStatusCode.Conflict, // 409

        // Server errors - Internal Server Error (default)
        _ => HttpStatusCode.InternalServerError // 500
    };
}
