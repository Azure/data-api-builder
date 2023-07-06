// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Net;
using Azure.DataApiBuilder.Core.Configurations;
using MySqlConnector;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Class to handle database specific logic for exception handling for MySql.
    /// <seealso cref="https://dev.mysql.com/doc/mysql-errors/8.0/en/server-error-reference.html"/>
    /// </summary>
    public class MySqlDbExceptionParser : DbExceptionParser
    {
        public MySqlDbExceptionParser(RuntimeConfigProvider configProvider) : base(configProvider)
        {
            // HashSet of 'SqlState'(s) which are to be considered as bad requests.
            BadRequestExceptionCodes.UnionWith(new List<string>
            {
                // ER_BAD_NULL_ERROR : Column '%s' cannot be null
                "1048",
                // 2.  ER_NON_UNIQ_ERROR: Column '%s' in %s is ambiguous
                "1052",
                // ER_DUP_UNIQUE: Can't write, because of unique constraint, to table '%s'
                "1169",
                // ER_NO_REFERENCED_ROW: Cannot add or update a child row: a foreign key constraint fails
                "1216",
                // ER_ROW_IS_REFERENCED: Cannot delete or update a parent row: a foreign key constraint fails
                "1217",
                // ER_ROW_IS_REFERENCED_2: Cannot delete or update a parent row: a foreign key constraint fails (%s)
                "1451",
                // ER_NO_REFERENCED_ROW_2: Cannot add or update a child row: a foreign key constraint fails (%s)
                "1452",
                // ER_FOREIGN_DUPLICATE_KEY: Upholding foreign key constraints for table '%s', entry '%s', key %d would
                // lead to a duplicate entry
                "1557",
                // ER_DUP_ENTRY_WITH_KEY_NAME: Duplicate entry '%s' for key '%s'
                "1586",
                // ER_FOREIGN_DUPLICATE_KEY_WITH_CHILD_INFO: Foreign key constraint for table '%s', record '%s' would
                // lead to a duplicate entry in table '%s', key '%s'
                "1761",
                // ER_FOREIGN_DUPLICATE_KEY_WITHOUT_CHILD_INFO: Foreign key constraint for table '%s', record '%s' would
                // lead to a duplicate entry in a child table
                "1762",
                // ER_DUP_UNKNOWN_IN_INDEX: Duplicate entry for key '%s'
                "1859",
                // 15. ER_CONSTRAINT_FAILED: CONSTRAINT %`s failed for %`-.192s.%`-.192s
                "4025"
            });

            TransientExceptionCodes.UnionWith(new List<string>
            {
                // List compiled from: https://mariadb.com/kb/en/mariadb-error-codes/
                "1020", "1021", "1037", "1038", "1040", "1041", "1150", "1151", "1156", "1157",
                "1158", "1159", "1160", "1161", "1192", "1203", "1205", "1206", "1223"
            });

            ConflictExceptionCodes.UnionWith(new List<string>
            {
                "1022", "1062", "1223", "1586", "1706", "1934"
            });
        }

        /// <inheritdoc/>
        public override bool IsTransientException(DbException e)
        {
            MySqlException ex = (MySqlException)e;
            return TransientExceptionCodes.Contains(ex.Number.ToString());
        }

        /// <inheritdoc/>
        public override HttpStatusCode GetHttpStatusCodeForException(DbException e)
        {
            MySqlException ex = (MySqlException)e;
            string exceptionNumber = ex.Number.ToString();

            if (BadRequestExceptionCodes.Contains(exceptionNumber))
            {
                return HttpStatusCode.BadRequest;
            }

            if (ConflictExceptionCodes.Contains(exceptionNumber))
            {
                return HttpStatusCode.Conflict;
            }

            return HttpStatusCode.InternalServerError;
        }
    }
}
