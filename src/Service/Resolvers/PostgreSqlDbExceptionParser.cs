using Azure.DataApiBuilder.Service.Configurations;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    public class PostgreSqlDbExceptionParser : DbExceptionParser
    {
        public PostgreSqlDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider)
        {
            badRequestErrorCodes = new() { 23502, 23503 };
        }
    }
}
