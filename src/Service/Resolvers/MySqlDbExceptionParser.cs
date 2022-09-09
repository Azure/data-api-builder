using Azure.DataApiBuilder.Service.Configurations;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Class to handle database specific logic for exception handling for MySql.
    /// <seealso cref="https://mariadb.com/kb/en/mariadb-error-codes/"/>
    /// </summary>
    public class MySqlDbExceptionParser : DbExceptionParser
    {
        public MySqlDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider)
        {
            badRequestErrorCodes = new() { "23000" };
        }
    }
}
