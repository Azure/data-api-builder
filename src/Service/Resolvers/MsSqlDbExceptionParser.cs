using System.Data.Common;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.Data.SqlClient;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    public class MsSqlDbExceptionParser : DbExceptionParser
    {
        public MsSqlDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider)
        {
            badRequestErrorCodes = new(){ 515, 547 };
        }

        /// <inheritdoc/>
        protected override bool IsBadRequestException(DbException e)
        {
            int errorNumber = ((SqlException)e).Number;
            return badRequestErrorCodes!.Contains(errorNumber);
        }
    }
}
