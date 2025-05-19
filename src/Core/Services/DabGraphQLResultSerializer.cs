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
                    // Map SubStatusCodes to appropriate HTTP status codes
                    switch (subStatusCode)
                    {
                        // Authentication/Authorization errors
                        case DataApiBuilderException.SubStatusCodes.AuthenticationChallenge:
                            return HttpStatusCode.Unauthorized; // 401
                        case DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed:
                        case DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure:
                        case DataApiBuilderException.SubStatusCodes.AuthorizationCumulativeColumnCheckFailed:
                            return HttpStatusCode.Forbidden; // 403

                        // Not Found errors
                        case DataApiBuilderException.SubStatusCodes.EntityNotFound:
                        case DataApiBuilderException.SubStatusCodes.ItemNotFound:
                        case DataApiBuilderException.SubStatusCodes.RelationshipNotFound:
                        case DataApiBuilderException.SubStatusCodes.RelationshipFieldNotFound:
                        case DataApiBuilderException.SubStatusCodes.DataSourceNotFound:
                            return HttpStatusCode.NotFound; // 404

                        // Bad Request errors
                        case DataApiBuilderException.SubStatusCodes.BadRequest:
                        case DataApiBuilderException.SubStatusCodes.DatabaseInputError:
                        case DataApiBuilderException.SubStatusCodes.InvalidIdentifierField:
                        case DataApiBuilderException.SubStatusCodes.ErrorProcessingData:
                        case DataApiBuilderException.SubStatusCodes.ExposedColumnNameMappingError:
                        case DataApiBuilderException.SubStatusCodes.UnsupportedClaimValueType:
                        case DataApiBuilderException.SubStatusCodes.ErrorProcessingEasyAuthHeader:
                            return HttpStatusCode.BadRequest; // 400

                        // Not Supported errors
                        case DataApiBuilderException.SubStatusCodes.NotSupported:
                        case DataApiBuilderException.SubStatusCodes.GlobalRestEndpointDisabled:
                            return HttpStatusCode.NotImplemented; // 501

                        // Conflict errors
                        case DataApiBuilderException.SubStatusCodes.OpenApiDocumentAlreadyExists:
                            return HttpStatusCode.Conflict; // 409

                        // Server errors - Internal Server Error
                        case DataApiBuilderException.SubStatusCodes.ConfigValidationError:
                        case DataApiBuilderException.SubStatusCodes.ErrorInInitialization:
                        case DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed:
                        case DataApiBuilderException.SubStatusCodes.GraphQLMapping:
                        case DataApiBuilderException.SubStatusCodes.UnexpectedError:
                        case DataApiBuilderException.SubStatusCodes.OpenApiDocumentCreationFailure:
                        default:
                            return HttpStatusCode.InternalServerError; // 500
                    }
                }
            }
        }

        return base.GetStatusCode(result);
    }
}
