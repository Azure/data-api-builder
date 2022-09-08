using System.Data.Common;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.Data.SqlClient;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    public class MsSqlDbExceptionParser : DbExceptionParser
    {
        public MsSqlDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider)
        {
            // For details about error codes please refer to:
            // https://docs.microsoft.com/en-us/sql/relational-databases/errors-events/database-engine-events-and-errors?view=sql-server-ver16
            badRequestErrorCodes = new() { "513", "515", "544", "545", "547", "548" };
        }

        /// <inheritdoc/>
        protected override bool IsBadRequestException(DbException e)
        {
            string errorNumber = ((SqlException)e).Number.ToString();
            return badRequestErrorCodes!.Contains(errorNumber);
        }
    }
}
