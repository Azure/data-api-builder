using System.Data.Common;
using System.Net;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.Data.SqlClient;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Class to handle database specific logic for exception handling for MsSql.
    /// <seealso cref="https://docs.microsoft.com/en-us/sql/relational-databases/errors-events/database-engine-events-and-errors?view=sql-server-ver16"/>
    /// </summary>
    public class MsSqlDbExceptionParser : DbExceptionParser
    {
        public MsSqlDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider,
            // HashSet of Error codes ('Number') which are to be considered as bad requests.
            new()
            {
                // A column insert or update conflicts with a rule imposed by a previous CREATE RULE statement. The statement was terminated.
                // The conflict occurred in database '%.*ls', table '%.*ls', column '%.*ls'.
                "513",

                // Cannot insert the value NULL into column '%.*ls', table '%.*ls'; column does not allow nulls. %ls fails.
                "515",

                // Cannot insert explicit value for identity column in table '%.*ls' when IDENTITY_INSERT is set to OFF.
                "544",

                // Explicit value must be specified for identity column in table '%.*ls'
                // either when IDENTITY_INSERT is set to ON or when a replication user is inserting into a NOT FOR REPLICATION identity column.
                "545",

                // The %ls statement conflicted with the %ls constraint "%.*ls".
                // The conflict occurred in database "%.*ls", table "%.*ls"%ls%.*ls%ls.
                "547",

                // The insert failed. It conflicted with an identity range check constraint in database '%.*ls',
                // replicated table '%.*ls'%ls%.*ls%ls.
                "548"
            })
        {
            TransientErrorCodes = new(){
                // Transient error codes compiled from:
                // https://github.com/dotnet/efcore/blob/main/src/EFCore.SqlServer/Storage/Internal/SqlServerTransientExceptionDetector.cs
                "20", "64", "121", "233", "601", "617", "669", "921", "997", "1203", "1204", "1205", "1221", "1807", "3935", "3960",
                "3966", "4060", "4221", "8628", "8645", "8651", "9515", "10053", "10054", "10060", "10922", "10928", "10929", "10936",
                "14355", "17197", "20041", "40197", "40501", "40613", "41301", "41302", "41305", "41325", "41839", "49918", "49919", "49920",

                // Transient error codes compiled from:
                // https://docs.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlconfigurableretryfactory?view=sqlclient-dotnet-standard-4.1
                "1222", "40143", "40540",

                // Transient error codes compiled from:
                // https://docs.microsoft.com/en-us/azure/azure-sql/database/troubleshoot-common-errors-issues?view=azuresql
                "615", "926",

                // These errors mainly occur when the SQL Server client can't connect to the server.
                // This may happen when the client cannot resolve the name of the server or the name of the server is incorrect.
                "53", "11001",

                // Transient error codes compiled from:
                // https://docs.microsoft.com/en-us/sql/relational-databases/errors-events/database-engine-events-and-errors?view=sql-server-ver16
                "18456"
            };
        }

        /// <summary>
        /// Helper method to get the HttpStatusCode for the exception based on the 'Number' of the exception.
        /// </summary>
        /// <param name="e">The exception thrown as a result of execution of the request.</param>
        /// <returns>status code to be returned in the response.</returns>
        public override HttpStatusCode GetHttpStatusCodeForException(DbException e)
        {
            string errorNumber = ((SqlException)e).Number.ToString();
            return BadRequestErrorCodes.Contains(errorNumber) ? HttpStatusCode.BadRequest : HttpStatusCode.InternalServerError;
        }

        /// <inheritdoc/>
        public override bool IsTransientException(DbException e)
        {
            string errorNumber = ((SqlException)e).Number.ToString();
            return TransientErrorCodes!.Contains(errorNumber);
        }
    }
}
