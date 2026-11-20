// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Transactions;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the exception SqlMutationEngine throws when a mutation's transaction fails.
    /// </summary>
    [TestClass]
    public class SqlMutationEngineTransactionUnitTests
    {
        /// <summary>
        /// The transaction exception, and the database error it carries, are kept as the inner exception
        /// while the status code, sub-status code and message returned to the client are unchanged.
        /// </summary>
        [TestMethod]
        public void TransactionFailure_PreservesInnerException()
        {
            Exception databaseError = new InvalidOperationException("Snapshot isolation transaction aborted due to update conflict.");
            TransactionAbortedException transactionException = new("The transaction has aborted.", databaseError);

            DataApiBuilderException exception = SqlMutationEngine.CreateTransactionFailedException(transactionException);

            Assert.AreEqual("An unexpected error occurred during the transaction execution", exception.Message);
            Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.UnexpectedError, exception.SubStatusCode);
            Assert.AreSame(transactionException, exception.InnerException);
            Assert.AreSame(databaseError, exception.InnerException!.InnerException);
        }

        /// <summary>
        /// Each failure gets its own exception instance, so concurrent requests never share or mutate the same exception object.
        /// </summary>
        [TestMethod]
        public void TransactionFailure_CreatesNewInstancePerFailure()
        {
            DataApiBuilderException first = SqlMutationEngine.CreateTransactionFailedException(new TransactionAbortedException());
            DataApiBuilderException second = SqlMutationEngine.CreateTransactionFailedException(new TransactionInDoubtException());

            Assert.AreNotSame(first, second);
            Assert.IsInstanceOfType(first.InnerException, typeof(TransactionAbortedException));
            Assert.IsInstanceOfType(second.InnerException, typeof(TransactionInDoubtException));
        }
    }
}
