// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Net;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Data.SqlClient;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Class to handle database specific logic for exception handling for MsSql.
    /// <seealso cref="https://docs.microsoft.com/en-us/sql/relational-databases/errors-events/database-engine-events-and-errors?view=sql-server-ver16"/>
    /// </summary>
    public class MsSqlDbExceptionParser : DbExceptionParser
    {
        public MsSqlDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider)
        {
            // HashSet of Error codes ('Number') which are to be considered as bad requests.
            BadRequestExceptionCodes.UnionWith(new List<string>
            {
                "157", "158", "169",
                "201", // Procedure or function '%.*ls' expects parameter '%.*ls', which was not supplied.
                "404", "412", "414", "415",
                "489", "513", "515", "544", "545", "547",
                "550", "611", "681", "1060", "4005", "4006",
                "4007", "4403", "4405", "4406", "4417", "4418", "4420",
                "4421", "4423", "4426", "4431", "4432", "4433", "4434",
                "4435", "4436", "4437", "4438", "4439", "4440", "4441",
                "4442", "4443", "4444", "4445", "4446", "4447", "4448",
                "4450", "4451", "4452", "4453", "4454", "4455", "4456",
                "4457", "4933", "4934", "4936", "4988", "8102"
            });

            TransientExceptionCodes.UnionWith(new List<string>
            {
                // Transient error codes compiled from:
                // https://github.com/dotnet/efcore/blob/main/src/EFCore.SqlServer/Storage/Internal/SqlServerTransientExceptionDetector.cs
                "20", "64", "121", "233", "601", "617", "669", "921", "997", "1203", "1204", "1205", "1221", "1807", "3935", "3960",
                "3966", "4060", "4221", "8628", "8645", "8651", "9515", "10053", "10054", "10060", "10922", "10928", "10929", "10936",
                "14355", "17197", "20041", "40197", "40501", "40613", "41301", "41302", "41305", "41325", "41839", "42109", "49918", "49919", "49920",

                // Transient error codes compiled from:
                //  https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlconfigurableretryfactory?view=sqlclient-dotnet-standard-5.0
                "1222", "40143", "40540",

                // Transient error codes compiled from:
                // https://docs.microsoft.com/en-us/azure/azure-sql/database/troubleshoot-common-errors-issues?view=azuresql
                "615", "926",

                // These errors mainly occur when the SQL Server client can't connect to the server.
                // This may happen when the client cannot resolve the name of the server or the name of the server is incorrect.
                "53", "11001"
            });

            ConflictExceptionCodes.UnionWith(new List<string>
            {
                "548", "2627", "22818", "22819", "22820", "22821",
                "22822", "22823", "22824", "22825", "3960", "5062"
            });

            _errorMessages.Add(201, "Invalid request. Missing required request parameters.");
        }

        /// <inheritdoc/>
        public override HttpStatusCode GetHttpStatusCodeForException(DbException e)
        {
            string exceptionNumber = ((SqlException)e).Number.ToString();
            if (BadRequestExceptionCodes.Contains(exceptionNumber))
            {
                return HttpStatusCode.BadRequest;
            }

            if (ConflictExceptionCodes.Contains(exceptionNumber))
            {
                return HttpStatusCode.Conflict;
            }

            return HttpStatusCode.InternalServerError;
        }

        /// <inheritdoc/>
        public override bool IsTransientException(DbException e)
        {
            string errorNumber = ((SqlException)e).Number.ToString();
            return TransientExceptionCodes.Contains(errorNumber);
        }

        /// <inheritdoc/>
        public override DataApiBuilderException.SubStatusCodes GetResultSubStatusCodeForException(DbException e)
        {
            int errorCode = ((SqlException)e).Number;
            // MSSQL 201 - Procedure or function '%.*ls' expects parameter '%.*ls', which was not supplied.
            // Pending revisions of error code classifications, this is a temporary fix to determine substatus code.
            if (errorCode == 201)
            {
                return DataApiBuilderException.SubStatusCodes.DatabaseInputError;
            }

            return DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed;
        }

        /// <summary>
        /// Returns a sanitized error message to be returned to the user.
        /// Development mode: returns specific database message which may contain database object names
        /// and not just DAB overridden entity names or field mappings.
        /// For MSSQL, also looks up any DAB overridden error messages in "_errorMessages" dictionary.
        /// e.g. message may contain: "dbo.CxStoredProcName" instead of "MySpEntityName"
        /// Production mode: returns generic database error message.
        /// </summary>
        /// <param name="e">Exception occurred in the database.</param>
        /// <returns>Error message returned to client.</returns>
        public override string GetMessage(DbException e)
        {
            if (_developerMode)
            {
                return e.Message;
            }

            int errorCode = ((SqlException)e).Number;
            return _errorMessages.ContainsKey(errorCode) ? _errorMessages[errorCode] : GENERIC_DB_EXCEPTION_MESSAGE;
        }
    }
}
