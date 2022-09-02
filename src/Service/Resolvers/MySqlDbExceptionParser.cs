using Azure.DataApiBuilder.Service.Configurations;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    public class MySqlDbExceptionParser : DbExceptionParser
    {
        public MySqlDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider)
        {
            badRequestErrorCodes = new() { 23000 };
        }
    }
}
