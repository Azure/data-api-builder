// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for DabGraphQLResultSerializer
    /// </summary>
    [TestClass]
    public class DabGraphQLResultSerializerTests
    {
        private DabGraphQLResultSerializer _serializer = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _serializer = new DabGraphQLResultSerializer();
        }

        #region Authentication/Authorization Tests

        /// <summary>
        /// Verify that AuthenticationChallenge maps to 401 Unauthorized
        /// </summary>
        [TestMethod]
        public void AuthenticationChallenge_Returns401()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.AuthenticationChallenge);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.Unauthorized, statusCode);
        }

        /// <summary>
        /// Verify that AuthorizationCheckFailed maps to 403 Forbidden
        /// </summary>
        [TestMethod]
        public void AuthorizationCheckFailed_Returns403()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.Forbidden, statusCode);
        }

        /// <summary>
        /// Verify that DatabasePolicyFailure maps to 403 Forbidden
        /// </summary>
        [TestMethod]
        public void DatabasePolicyFailure_Returns403()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.Forbidden, statusCode);
        }

        /// <summary>
        /// Verify that AuthorizationCumulativeColumnCheckFailed maps to 403 Forbidden
        /// </summary>
        [TestMethod]
        public void AuthorizationCumulativeColumnCheckFailed_Returns403()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.AuthorizationCumulativeColumnCheckFailed);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.Forbidden, statusCode);
        }

        #endregion

        #region Not Found Tests

        /// <summary>
        /// Verify that EntityNotFound maps to 404 NotFound
        /// </summary>
        [TestMethod]
        public void EntityNotFound_Returns404()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.EntityNotFound);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.NotFound, statusCode);
        }

        /// <summary>
        /// Verify that ItemNotFound maps to 404 NotFound
        /// </summary>
        [TestMethod]
        public void ItemNotFound_Returns404()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.ItemNotFound);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.NotFound, statusCode);
        }

        /// <summary>
        /// Verify that RelationshipNotFound maps to 404 NotFound
        /// </summary>
        [TestMethod]
        public void RelationshipNotFound_Returns404()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.RelationshipNotFound);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.NotFound, statusCode);
        }

        /// <summary>
        /// Verify that RelationshipFieldNotFound maps to 404 NotFound
        /// </summary>
        [TestMethod]
        public void RelationshipFieldNotFound_Returns404()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.RelationshipFieldNotFound);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.NotFound, statusCode);
        }

        /// <summary>
        /// Verify that DataSourceNotFound maps to 404 NotFound
        /// </summary>
        [TestMethod]
        public void DataSourceNotFound_Returns404()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.DataSourceNotFound);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.NotFound, statusCode);
        }

        #endregion

        #region Bad Request Tests

        /// <summary>
        /// Verify that BadRequest maps to 400 BadRequest
        /// </summary>
        [TestMethod]
        public void BadRequest_Returns400()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.BadRequest);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.BadRequest, statusCode);
        }

        /// <summary>
        /// Verify that DatabaseInputError maps to 400 BadRequest
        /// </summary>
        [TestMethod]
        public void DatabaseInputError_Returns400()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.DatabaseInputError);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.BadRequest, statusCode);
        }

        /// <summary>
        /// Verify that InvalidIdentifierField maps to 400 BadRequest
        /// </summary>
        [TestMethod]
        public void InvalidIdentifierField_Returns400()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.InvalidIdentifierField);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.BadRequest, statusCode);
        }

        /// <summary>
        /// Verify that ErrorProcessingData maps to 400 BadRequest
        /// </summary>
        [TestMethod]
        public void ErrorProcessingData_Returns400()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.ErrorProcessingData);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.BadRequest, statusCode);
        }

        /// <summary>
        /// Verify that ExposedColumnNameMappingError maps to 400 BadRequest
        /// </summary>
        [TestMethod]
        public void ExposedColumnNameMappingError_Returns400()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.ExposedColumnNameMappingError);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.BadRequest, statusCode);
        }

        /// <summary>
        /// Verify that UnsupportedClaimValueType maps to 400 BadRequest
        /// </summary>
        [TestMethod]
        public void UnsupportedClaimValueType_Returns400()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.UnsupportedClaimValueType);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.BadRequest, statusCode);
        }

        /// <summary>
        /// Verify that ErrorProcessingEasyAuthHeader maps to 400 BadRequest
        /// </summary>
        [TestMethod]
        public void ErrorProcessingEasyAuthHeader_Returns400()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.ErrorProcessingEasyAuthHeader);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.BadRequest, statusCode);
        }

        #endregion

        #region Not Implemented Tests

        /// <summary>
        /// Verify that NotSupported maps to 501 NotImplemented
        /// </summary>
        [TestMethod]
        public void NotSupported_Returns501()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.NotSupported);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.NotImplemented, statusCode);
        }

        /// <summary>
        /// Verify that GlobalRestEndpointDisabled maps to 501 NotImplemented
        /// </summary>
        [TestMethod]
        public void GlobalRestEndpointDisabled_Returns501()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.GlobalRestEndpointDisabled);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.NotImplemented, statusCode);
        }

        #endregion

        #region Conflict Tests

        /// <summary>
        /// Verify that OpenApiDocumentAlreadyExists maps to 409 Conflict
        /// </summary>
        [TestMethod]
        public void OpenApiDocumentAlreadyExists_Returns409()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.OpenApiDocumentAlreadyExists);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.Conflict, statusCode);
        }

        #endregion

        #region Server Error Tests

        /// <summary>
        /// Verify that ConfigValidationError maps to 500 InternalServerError
        /// </summary>
        [TestMethod]
        public void ConfigValidationError_Returns500()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.ConfigValidationError);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.InternalServerError, statusCode);
        }

        /// <summary>
        /// Verify that ErrorInInitialization maps to 500 InternalServerError
        /// </summary>
        [TestMethod]
        public void ErrorInInitialization_Returns500()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.ErrorInInitialization);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.InternalServerError, statusCode);
        }

        /// <summary>
        /// Verify that DatabaseOperationFailed maps to 500 InternalServerError
        /// </summary>
        [TestMethod]
        public void DatabaseOperationFailed_Returns500()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.InternalServerError, statusCode);
        }

        /// <summary>
        /// Verify that GraphQLMapping maps to 500 InternalServerError
        /// </summary>
        [TestMethod]
        public void GraphQLMapping_Returns500()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.GraphQLMapping);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.InternalServerError, statusCode);
        }

        /// <summary>
        /// Verify that UnexpectedError maps to 500 InternalServerError
        /// </summary>
        [TestMethod]
        public void UnexpectedError_Returns500()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.UnexpectedError);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.InternalServerError, statusCode);
        }

        /// <summary>
        /// Verify that OpenApiDocumentCreationFailure maps to 500 InternalServerError
        /// </summary>
        [TestMethod]
        public void OpenApiDocumentCreationFailure_Returns500()
        {
            // Arrange
            IExecutionResult result = CreateResultWithError(DataApiBuilderException.SubStatusCodes.OpenApiDocumentCreationFailure);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(result);

            // Assert
            Assert.AreEqual(HttpStatusCode.InternalServerError, statusCode);
        }

        #endregion

        #region Edge Case Tests

        /// <summary>
        /// Verify that when there are no errors, the base status code is returned (500)
        /// </summary>
        [TestMethod]
        public void NoErrors_ReturnsBaseStatusCode()
        {
            // Arrange
            Mock<IQueryResult> mockResult = new();
            mockResult.Setup(r => r.Errors).Returns((IReadOnlyList<IError>)null);

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(mockResult.Object);

            // Assert
            Assert.AreEqual(HttpStatusCode.InternalServerError, statusCode);
        }

        /// <summary>
        /// Verify that when errors list is empty, the base status code is returned (500)
        /// </summary>
        [TestMethod]
        public void EmptyErrorsList_ReturnsBaseStatusCode()
        {
            // Arrange
            Mock<IQueryResult> mockResult = new();
            mockResult.Setup(r => r.Errors).Returns(new List<IError>());

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(mockResult.Object);

            // Assert
            Assert.AreEqual(HttpStatusCode.InternalServerError, statusCode);
        }

        /// <summary>
        /// Verify that when error has null code, the base status code is returned (500)
        /// </summary>
        [TestMethod]
        public void NullErrorCode_ReturnsBaseStatusCode()
        {
            // Arrange
            Mock<IError> mockError = new();
            mockError.Setup(e => e.Code).Returns((string)null);

            Mock<IQueryResult> mockResult = new();
            mockResult.Setup(r => r.Errors).Returns(new List<IError> { mockError.Object });

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(mockResult.Object);

            // Assert
            Assert.AreEqual(HttpStatusCode.InternalServerError, statusCode);
        }

        /// <summary>
        /// Verify that when error code is not a valid SubStatusCode, the base status code is returned (500)
        /// </summary>
        [TestMethod]
        public void InvalidErrorCode_ReturnsBaseStatusCode()
        {
            // Arrange
            Mock<IError> mockError = new();
            mockError.Setup(e => e.Code).Returns("InvalidErrorCode");

            Mock<IQueryResult> mockResult = new();
            mockResult.Setup(r => r.Errors).Returns(new List<IError> { mockError.Object });

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(mockResult.Object);

            // Assert
            Assert.AreEqual(HttpStatusCode.InternalServerError, statusCode);
        }

        /// <summary>
        /// Verify that when there are multiple errors, the first DAB exception error code is used
        /// </summary>
        [TestMethod]
        public void MultipleErrors_ReturnsFirstDabExceptionStatusCode()
        {
            // Arrange
            Mock<IError> mockError1 = new();
            mockError1.Setup(e => e.Code).Returns("InvalidCode"); // Not a DAB exception

            Mock<IError> mockError2 = new();
            mockError2.Setup(e => e.Code).Returns(DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString());

            Mock<IError> mockError3 = new();
            mockError3.Setup(e => e.Code).Returns(DataApiBuilderException.SubStatusCodes.BadRequest.ToString());

            Mock<IQueryResult> mockResult = new();
            mockResult.Setup(r => r.Errors).Returns(new List<IError> { mockError1.Object, mockError2.Object, mockError3.Object });

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(mockResult.Object);

            // Assert - Should return status code for first valid DAB exception (EntityNotFound = 404)
            Assert.AreEqual(HttpStatusCode.NotFound, statusCode);
        }

        /// <summary>
        /// Verify that result that is not IQueryResult returns base status code (500)
        /// </summary>
        [TestMethod]
        public void NonQueryResult_ReturnsBaseStatusCode()
        {
            // Arrange
            Mock<IExecutionResult> mockResult = new();

            // Act
            HttpStatusCode statusCode = _serializer.GetStatusCode(mockResult.Object);

            // Assert
            Assert.AreEqual(HttpStatusCode.InternalServerError, statusCode);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a mock IExecutionResult with a single error containing the specified SubStatusCode
        /// </summary>
        private static IExecutionResult CreateResultWithError(DataApiBuilderException.SubStatusCodes subStatusCode)
        {
            Mock<IError> mockError = new();
            mockError.Setup(e => e.Code).Returns(subStatusCode.ToString());

            Mock<IQueryResult> mockResult = new();
            mockResult.Setup(r => r.Errors).Returns(new List<IError> { mockError.Object });

            return mockResult.Object;
        }

        #endregion
    }
}
