// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;

namespace Azure.DataApiBuilder.Service.Exceptions
{
    /// <summary>
    /// Represents an exception thrown from the DataApiBuilder service.
    /// Message and http statusCode will be returned to the user but
    /// subStatus code is not returned.
    /// </summary>
#pragma warning disable CA1032 // Supressing since we only use the 3 argument constructor
    public class DataApiBuilderException : Exception
    {
        public const string CONNECTION_STRING_ERROR_MESSAGE = "A valid Connection String should be provided.";
        public const string GRAPHQL_FILTER_ENTITY_AUTHZ_FAILURE = "Access forbidden to the target entity described in the filter.";
        public const string GRAPHQL_FILTER_FIELD_AUTHZ_FAILURE = "Access forbidden to a field referenced in the filter.";

        public enum SubStatusCodes
        {
            /// <summary>
            /// The given request was invalid and could not be handled. This only includes
            /// validation errors that do not require access to the database. So only the server config and the request itself
            /// </summary>
            BadRequest,
            /// <summary>
            /// The entity for which an operation was requested does not exist.
            /// </summary>
            EntityNotFound,
            /// <summary>
            /// Request failed authentication. i.e. No/Invalid JWT token
            /// </summary>
            AuthenticationChallenge,
            /// <summary>
            /// Request failed authorization.
            /// </summary>
            AuthorizationCheckFailed,
            /// <summary>
            /// The requested operation failed on the database.
            /// </summary>
            DatabaseOperationFailed,
            /// <summary>
            /// Unexpected error.
            /// </summary>,
            UnexpectedError,
            /// <summary>
            /// Error mapping database information to GraphQL information
            /// </summary>
            GraphQLMapping,
            /// <summary>
            /// Error due to trying to use unsupported feature
            /// </summary>
            NotSupported,
            /// <summary>
            /// Error encountered while initializing.
            /// </summary>
            ErrorInInitialization,
            /// <summary>
            /// Cumulative column check of QueryString (OData filter parsing) failure.
            /// </summary>
            AuthorizationCumulativeColumnCheckFailed,
            /// <summary>
            /// Requested exposedColumnName does not map to backingColumnName for entity.
            /// </summary>
            ExposedColumnNameMappingError,
            /// <summary>
            /// The runtime config is invalid semantically.
            /// </summary>
            ConfigValidationError,
            /// <summary>
            /// Provided EasyAuth header is non-existent or malformed.
            /// </summary>
            ErrorProcessingEasyAuthHeader,
            /// <summary>
            /// One of the claim belonging to the user has unsupported claim value type.
            /// </summary>
            UnsupportedClaimValueType,
            /// <summary>
            /// Error encountered while doing data type conversions.
            /// </summary>
            ErrorProcessingData,
            /// <summary>
            /// Attempting to generate OpenAPI document when one already exists.
            /// </summary>
            OpenApiDocumentAlreadyExists,
            /// <summary>
            /// Attempting to generate OpenAPI document failed.
            /// </summary>
            OpenApiDocumentGenerationFailure
        }

        public HttpStatusCode StatusCode { get; }
        public SubStatusCodes SubStatusCode { get; }

        public DataApiBuilderException(string message,
            HttpStatusCode statusCode,
            SubStatusCodes subStatusCode,
            Exception? innerException = null)
            : base(message, innerException: innerException)
        {
            StatusCode = statusCode;
            SubStatusCode = subStatusCode;
        }
    }
}
