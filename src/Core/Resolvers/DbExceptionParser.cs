// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Net;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using static Azure.DataApiBuilder.Service.Exceptions.DataApiBuilderException;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    ///<summary>
    /// Parses some db exceptions and converts them to useful exceptions that can be reported
    /// to the user
    ///</summary>
    public abstract class DbExceptionParser
    {
        public const string GENERIC_DB_EXCEPTION_MESSAGE = "While processing your request the database ran into an error.";
        private readonly bool _developerMode;
        protected HashSet<string> BadRequestExceptionCodes;

        /*A transient error, also known as a transient fault, has an underlying cause that soon resolves itself.
         * An occasional cause of transient errors can be reconfiguration events. Most of these reconfiguration
         * events finish in less than 60 seconds. During this reconfiguration time span, we might have issues with
         * connecting to your database in SQL Database.*/
        protected HashSet<string> TransientExceptionCodes;
        protected HashSet<string> ConflictExceptionCodes;
        internal Dictionary<int, string> _errorMessages;

        public DbExceptionParser(RuntimeConfigProvider configProvider)
        {
            _developerMode = configProvider.GetConfig().IsDevelopmentMode();
            BadRequestExceptionCodes = new();
            TransientExceptionCodes = new();
            ConflictExceptionCodes = new();
            _errorMessages = new();
        }

        /// <summary>
        /// Helper method to parse the exception occurred as a result of executing the request against database
        /// and return our custom exception (with the error message depending on the host mode of operation). 
        /// </summary>
        /// <param name="e">Exception occurred in the database.</param>
        /// <returns>Custom exception to be returned to the user.</returns>
        public Exception Parse(DbException e)
        {
            return new DataApiBuilderException(
                message: _developerMode ? e.Message : GetMessage(e),
                statusCode: GetHttpStatusCodeForException(e),
                subStatusCode: GetResultSubStatusCodeForException(e),
                innerException: e
            );
        }

        /// <summary>
        /// Helper method to determine whether an exception thrown by database is to be considered as transient.
        /// Each of the databases has their own way of classifying an exception as transient and hence the method will
        /// be overridden in each of the subclasses.
        /// </summary>
        /// <param name="e">Exception to be classified as transient/non-transient.</param>
        /// <returns></returns>
        public abstract bool IsTransientException(DbException e);

        /// <summary>
        /// Helper method to get the HttpStatusCode for the exception based on the SqlState of the exception.
        /// </summary>
        /// <param name="e">The exception thrown as a result of execution of the request.</param>
        /// <returns>status code to be returned in the response.</returns>
        public abstract HttpStatusCode GetHttpStatusCodeForException(DbException e);

        /// <summary>
        /// Gets a specific substatus code which describes the cause of the error in more detail.
        /// </summary>
        /// <param name="e">The exception thrown as a result of execution of the request.</param>
        /// <returns>status code to be returned in the response.</returns>
        public virtual SubStatusCodes GetResultSubStatusCodeForException(DbException e)
        {
            return SubStatusCodes.DatabaseOperationFailed;
        }

        /// <summary>
        /// Gets the user-friendly message to be returned to the user in case of an exception.
        /// </summary>
        /// <param name="e">The exception thrown as a result of execution of the request.</param>
        /// <returns>Response message.</returns>
        public virtual string GetMessage(DbException e)
        {
            return GENERIC_DB_EXCEPTION_MESSAGE;
        }
    }
}
