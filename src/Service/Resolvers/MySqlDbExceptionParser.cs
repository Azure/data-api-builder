using Azure.DataApiBuilder.Service.Configurations;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    public class MySqlDbExceptionParser : DbExceptionParser
    {
        public MySqlDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider)
        {
            // For details about error codes please refer to: https://mariadb.com/kb/en/mariadb-error-codes/
            badRequestErrorCodes = new() { "23000" };
        }
    }
}
