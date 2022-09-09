using System.Collections.Generic;
using System.Data.Common;
using Azure.DataApiBuilder.Service.Configurations;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Class to handle database specific logic for exception handling for PgSql.
    /// <seealso cref="https://www.postgresql.org/docs/current/errcodes-appendix.html"/>
    /// </summary>
    public class PostgreSqlDbExceptionParser : DbExceptionParser
    {
        private HashSet<string> _badRequestSqlStates;
        public PostgreSqlDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider)
        {
            _badRequestSqlStates = new() {
                // integrity_constraint_violation, occurs when an insert/update/delete statement violates
                // a foreign key,primary key, check or unique constraint.
                "23000",

                // restrict_violation
                "23001",

                // not_null_violation, occurs when we try to null out a field in table which is declared as non-nullable.
                "23502",

                // foreign_key_violation, occurs when an insertion violates foreign key constraint.
                "23503",

                // unique_violation, occurs when an insertion on a table tries to insert a duplicate value for a unique field.
                "23505",

                // check_violation,The CHECK constraint ensures that all values in a column satisfy certain conditions.
                // Check violation occurs when a check constraint fails. For eg. name like '.dab' when name = 'DAB' fails the check.
                "23514",

                // exclusion_violation, The EXCLUDE constraint ensures that if any two rows are compared on the specified column(s)
                // or expression(s) using the specified operator(s), not all of these comparisons will return TRUE.
                "23P01"
            };
        }

        /// <inheritdoc/>
        public override bool IsBadRequestException(DbException e)
        {
            return e.SqlState is not null && _badRequestSqlStates.Contains(e.SqlState);
        }
    }
}
