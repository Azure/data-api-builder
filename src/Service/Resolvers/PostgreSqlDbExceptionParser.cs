using Azure.DataApiBuilder.Service.Configurations;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Class to handle database specific logic for exception handling for PgSql.
    /// <seealso cref="https://www.postgresql.org/docs/current/errcodes-appendix.html"/>
    /// </summary>
    public class PostgreSqlDbExceptionParser : DbExceptionParser
    {
        public PostgreSqlDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider)
        {
            badRequestErrorCodes = new() {
                "23000",    // integrity_constraint_violation
                "23001",    // restrict_violation
                "23502",    // not_null_violation
                "23503",    // foreign_key_violation
                "23505",    // unique_violation
                "23514",    // check_violation
                "23P01"     // exclusion_violation
            };
        }
    }
}
