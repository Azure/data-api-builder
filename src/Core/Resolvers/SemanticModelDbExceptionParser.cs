// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Net;
using Azure.DataApiBuilder.Core.Configurations;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Class to handle database specific logic for exception handling for Semantic Model (ADOMD.NET) connections.
    /// AdomdException does not extend DbException, so this parser provides default handling
    /// for any DbException instances that may arise from the underlying connection layer.
    /// </summary>
    public class SemanticModelDbExceptionParser : DbExceptionParser
    {
        public SemanticModelDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider)
        {
        }

        /// <inheritdoc/>
        /// <remarks>
        /// ADOMD errors are generally not transient. Connection-level failures
        /// may be transient, but without a reliable error code to check we
        /// default to non-transient.
        /// </remarks>
        public override bool IsTransientException(DbException e)
        {
            return false;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Most ADOMD errors surface query or connection problems. Without
        /// specific error codes we default to InternalServerError.
        /// </remarks>
        public override HttpStatusCode GetHttpStatusCodeForException(DbException e)
        {
            return HttpStatusCode.InternalServerError;
        }

        /// <summary>
        /// Returns a sanitized error message to be returned to the user.
        /// Development mode: returns the database-provided message.
        /// Production mode: returns a generic error message.
        /// </summary>
        /// <param name="e">Exception occurred in the database.</param>
        /// <returns>Error message returned to client.</returns>
        public override string GetMessage(DbException e)
        {
            return _developerMode ? e.Message : GENERIC_DB_EXCEPTION_MESSAGE;
        }
    }
}
