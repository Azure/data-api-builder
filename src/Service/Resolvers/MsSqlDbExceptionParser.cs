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
        public MsSqlDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider)
        {
            // HashSet of Error codes ('Number') which are to be considered as bad requests.
            BadRequestErrorCodes = new() {
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
            };
        }

        /// <inheritdoc/>
        public override HttpStatusCode GetHttpSTatusCodeForException(DbException e)
        {
            string errorNumber = ((SqlException)e).Number.ToString();
            return BadRequestErrorCodes.Contains(errorNumber) ? HttpStatusCode.BadRequest : HttpStatusCode.InternalServerError;
        }
    }
}
