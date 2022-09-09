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
            // For all the below conditions, we will get the SqlState as 23000 for MySql.
            // 1.  ER_DUP_KEY : Can't write; duplicate key in table '%s'
            // 2.  ER_BAD_NULL_ERROR : Column '%s' cannot be null
            // 3.  ER_NON_UNIQ_ERROR: Column '%s' in %s is ambiguous
            // 4.  ER_DUP_ENTRY: Duplicate entry '%s' for key %d
            // 5.  ER_DUP_UNIQUE: Can't write, because of unique constraint, to table '%s'
            // 6.  ER_NO_REFERENCED_ROW: Cannot add or update a child row: a foreign key constraint fails
            // 7.  ER_ROW_IS_REFERENCED: Cannot delete or update a parent row: a foreign key constraint fails
            // 8.  ER_ROW_IS_REFERENCED_2: Cannot delete or update a parent row: a foreign key constraint fails (%s)
            // 9.  ER_NO_REFERENCED_ROW_2: Cannot add or update a child row: a foreign key constraint fails (%s)
            // 10. ER_FOREIGN_DUPLICATE_KEY: Upholding foreign key constraints for table '%s', entry '%s', key %d would
            // lead to a duplicate entry
            // 11. ER_DUP_ENTRY_WITH_KEY_NAME: Duplicate entry '%s' for key '%s'
            // 12. ER_FOREIGN_DUPLICATE_KEY_WITH_CHILD_INFO: Foreign key constraint for table '%s', record '%s' would
            // lead to a duplicate entry in table '%s', key '%s'
            // 13. ER_FOREIGN_DUPLICATE_KEY_WITHOUT_CHILD_INFO: Foreign key constraint for table '%s', record '%s' would
            // lead to a duplicate entry in a child table
            // 14. ER_DUP_UNKNOWN_IN_INDEX: Duplicate entry for key '%s'
            // 15. ER_CONSTRAINT_FAILED: CONSTRAINT %`s failed for %`-.192s.%`-.192s

            // HashSet of 'SqlState'(s) which are to be considered as bad requests.
            badRequestErrorCodes = new() { "23000" };
        }
    }
}
