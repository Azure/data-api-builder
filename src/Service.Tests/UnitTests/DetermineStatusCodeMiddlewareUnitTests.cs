// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for DetermineStatusCodeMiddleware.DetermineHttpStatusCode method
    /// which maps DataApiBuilderException.SubStatusCodes to appropriate HTTP status codes.
    /// </summary>
    [TestClass]
    public class DetermineStatusCodeMiddlewareUnitTests
    {
        /// <summary>
        /// Verify that each SubStatusCode maps to the expected HttpStatusCode.
        /// </summary>
        [DataTestMethod]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.AuthenticationChallenge), HttpStatusCode.Unauthorized, DisplayName = "AuthenticationChallenge → 401")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed), HttpStatusCode.Forbidden, DisplayName = "AuthorizationCheckFailed → 403")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure), HttpStatusCode.Forbidden, DisplayName = "DatabasePolicyFailure → 403")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.AuthorizationCumulativeColumnCheckFailed), HttpStatusCode.Forbidden, DisplayName = "AuthorizationCumulativeColumnCheckFailed → 403")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.EntityNotFound), HttpStatusCode.NotFound, DisplayName = "EntityNotFound → 404")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.ItemNotFound), HttpStatusCode.NotFound, DisplayName = "ItemNotFound → 404")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.RelationshipNotFound), HttpStatusCode.NotFound, DisplayName = "RelationshipNotFound → 404")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.RelationshipFieldNotFound), HttpStatusCode.NotFound, DisplayName = "RelationshipFieldNotFound → 404")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.DataSourceNotFound), HttpStatusCode.NotFound, DisplayName = "DataSourceNotFound → 404")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.BadRequest), HttpStatusCode.BadRequest, DisplayName = "BadRequest → 400")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.DatabaseInputError), HttpStatusCode.BadRequest, DisplayName = "DatabaseInputError → 400")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.InvalidIdentifierField), HttpStatusCode.BadRequest, DisplayName = "InvalidIdentifierField → 400")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.ErrorProcessingData), HttpStatusCode.BadRequest, DisplayName = "ErrorProcessingData → 400")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.ExposedColumnNameMappingError), HttpStatusCode.BadRequest, DisplayName = "ExposedColumnNameMappingError → 400")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.UnsupportedClaimValueType), HttpStatusCode.BadRequest, DisplayName = "UnsupportedClaimValueType → 400")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.ErrorProcessingEasyAuthHeader), HttpStatusCode.BadRequest, DisplayName = "ErrorProcessingEasyAuthHeader → 400")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.NotSupported), HttpStatusCode.NotImplemented, DisplayName = "NotSupported → 501")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.GlobalRestEndpointDisabled), HttpStatusCode.NotImplemented, DisplayName = "GlobalRestEndpointDisabled → 501")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.GlobalMcpEndpointDisabled), HttpStatusCode.NotImplemented, DisplayName = "GlobalMcpEndpointDisabled → 501")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.OpenApiDocumentAlreadyExists), HttpStatusCode.Conflict, DisplayName = "OpenApiDocumentAlreadyExists → 409")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.ConfigValidationError), HttpStatusCode.InternalServerError, DisplayName = "ConfigValidationError → 500")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.ErrorInInitialization), HttpStatusCode.InternalServerError, DisplayName = "ErrorInInitialization → 500")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed), HttpStatusCode.InternalServerError, DisplayName = "DatabaseOperationFailed → 500")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.GraphQLMapping), HttpStatusCode.InternalServerError, DisplayName = "GraphQLMapping → 500")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.UnexpectedError), HttpStatusCode.InternalServerError, DisplayName = "UnexpectedError → 500")]
        [DataRow(nameof(DataApiBuilderException.SubStatusCodes.OpenApiDocumentCreationFailure), HttpStatusCode.InternalServerError, DisplayName = "OpenApiDocumentCreationFailure → 500")]
        public void SubStatusCodeMapsToExpectedHttpStatusCode(string errorCode, HttpStatusCode expectedStatusCode)
        {
            Mock<IError> mockError = new();
            mockError.SetupGet(e => e.Code).Returns(errorCode);

            List<IError> errors = new() { mockError.Object };

            HttpStatusCode? actualStatusCode = DetermineStatusCodeMiddleware.DetermineHttpStatusCode(errors);

            Assert.IsNotNull(actualStatusCode);
            Assert.AreEqual(expectedStatusCode, actualStatusCode.Value);
        }

        /// <summary>
        /// Verify that when no error has a recognized SubStatusCode,
        /// the method returns null.
        /// </summary>
        [TestMethod]
        public void UnrecognizedErrorCodeReturnsNull()
        {
            Mock<IError> mockError = new();
            mockError.SetupGet(e => e.Code).Returns("SomeUnknownCode");

            List<IError> errors = new() { mockError.Object };

            HttpStatusCode? actualStatusCode = DetermineStatusCodeMiddleware.DetermineHttpStatusCode(errors);

            Assert.IsNull(actualStatusCode);
        }

        /// <summary>
        /// Verify that when error code is null, the method returns null.
        /// </summary>
        [TestMethod]
        public void NullErrorCodeReturnsNull()
        {
            Mock<IError> mockError = new();
            mockError.SetupGet(e => e.Code).Returns(value: null);

            List<IError> errors = new() { mockError.Object };

            HttpStatusCode? actualStatusCode = DetermineStatusCodeMiddleware.DetermineHttpStatusCode(errors);

            Assert.IsNull(actualStatusCode);
        }

        /// <summary>
        /// Verify that the first recognized error code determines the status code.
        /// </summary>
        [TestMethod]
        public void FirstRecognizedErrorDeterminesStatusCode()
        {
            Mock<IError> mockUnknownError = new();
            mockUnknownError.SetupGet(e => e.Code).Returns("UnrecognizedCode");

            Mock<IError> mockNotFoundError = new();
            mockNotFoundError.SetupGet(e => e.Code).Returns(nameof(DataApiBuilderException.SubStatusCodes.EntityNotFound));

            Mock<IError> mockBadRequestError = new();
            mockBadRequestError.SetupGet(e => e.Code).Returns(nameof(DataApiBuilderException.SubStatusCodes.BadRequest));

            List<IError> errors = new() { mockUnknownError.Object, mockNotFoundError.Object, mockBadRequestError.Object };

            HttpStatusCode? actualStatusCode = DetermineStatusCodeMiddleware.DetermineHttpStatusCode(errors);

            Assert.IsNotNull(actualStatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, actualStatusCode.Value);
        }

        /// <summary>
        /// Verify that an empty error list returns null.
        /// </summary>
        [TestMethod]
        public void EmptyErrorListReturnsNull()
        {
            List<IError> errors = new();

            HttpStatusCode? actualStatusCode = DetermineStatusCodeMiddleware.DetermineHttpStatusCode(errors);

            Assert.IsNull(actualStatusCode);
        }
    }
}
