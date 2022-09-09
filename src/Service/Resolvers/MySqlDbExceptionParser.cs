using System.Collections.Generic;
using System.Data.Common;
using Azure.DataApiBuilder.Service.Configurations;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Class to handle database specific logic for exception handling for MySql.
    /// <seealso cref="https://mariadb.com/kb/en/mariadb-error-codes/"/>
    /// </summary>
    public class MySqlDbExceptionParser : DbExceptionParser
    {
        private HashSet<string> _badRequestSqlStates;
        public MySqlDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider)
        {
            // For all the below conditions, we will get the SqlState as 23000 for MySql.
            // 1.  Can't write; duplicate key in table '%s'
            // 2.  Column '%s' cannot be null
            // 3.  Column '%s' in %s is ambiguous
            // 4.  Duplicate entry '%s' for key %d
            // 5.  Can't write, because of unique constraint, to table '%s'
            // 6.  Cannot add or update a child row: a foreign key constraint fails
            // 7.  Cannot delete or update a parent row: a foreign key constraint fails (%s)
            // 8.  Cannot add or update a child row: a foreign key constraint fails (%s)
            // 9.  Upholding foreign key constraints for table '%s', entry '%s', key %d would lead to a duplicate entry
            // 10. Duplicate entry '%s' for key '%s'
            // 11. Foreign key constraint for table '%s', record '%s' would lead to a duplicate entry in table '%s', key '%s'
            // 12. Foreign key constraint for table '%s', record '%s' would lead to a duplicate entry in a child table
            // 13. Duplicate entry for key '%s'
            // 14. CONSTRAINT %`s failed for %`-.192s.%`-.192s
            _badRequestSqlStates = new() { "23000" };
        }

        /// <inheritdoc/>
        public override bool IsBadRequestException(DbException e)
        {
            return e.SqlState is not null && _badRequestSqlStates.Contains(e.SqlState);
        }
    }
}
