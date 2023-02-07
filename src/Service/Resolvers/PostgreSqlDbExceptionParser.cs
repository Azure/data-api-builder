// **************************************
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// @file: PostgreSqlDbExceptionParser.cs
// **************************************

using System.Collections.Generic;
using System.Data.Common;
using System.Net;
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
            // HashSet of 'SqlState'(s) to be considered as bad requests.
            BadRequestExceptionCodes.UnionWith(new List<string>
            {
                // integrity_constraint_violation, occurs when an insert/update/delete statement violates
                    // a foreign key, primary key, check or unique constraint.
                    "23000",

                    // not_null_violation, occurs when attempting to null out a field in table which is declared as non-nullable.
                    "23502",

                    // foreign_key_violation, occurs when an insertion violates foreign key constraint.
                    "23503",

                    // check_violation,The CHECK constraint ensures that all values in a column satisfy certain conditions.
                    // Check violation occurs when a check constraint fails. e.g. name like '.dab' when name = 'DAB' fails the check.
                    "23514",

                    // exclusion_violation, The EXCLUDE constraint ensures that if any two rows are compared on the specified column(s)
                    // or expression(s) using the specified operator(s), atleast one of those operator comparisons will return false or null.
                    "23P01",

                    // object_not_in_prerequisite_state
                    "55000"
            });

            TransientExceptionCodes.UnionWith(new List<string>
            {
                // insufficient_resources
                "53000",

                // disk_full
                "53100",

                // out_of_memory
                "53200",

                // configuration_limit_exceeded
                "53400",

                // cannot_connect_now
                "57P03",

                // system_error
                "58000",

                // io_error
                "58030",

                // lock_not_available
                "55P03",

                // object_in_use
                "55006",

                // object_not_in_prerequisite_state
                "55000",

                // connection_exception
                "08000",

                // connection_does_not_exist
                "08003",

                // connection_failure
                "08006",

                // sqlserver_rejected_establishment_of_sqlconnection
                "08004",

                // transaction_resolution_unknown
                "08007"
            });

            ConflictExceptionCodes.UnionWith(new List<string>
            {
                // unique_violation, occurs when an insertion on a table tries to insert a duplicate value for a unique field.
                "23505",
            });
        }

        /// <inheritdoc/>
        public override bool IsTransientException(DbException e)
        {
            return e.SqlState is not null && TransientExceptionCodes.Contains(e.SqlState);
        }

        /// <inheritdoc/>
        public override HttpStatusCode GetHttpStatusCodeForException(DbException e)
        {
            if (e.SqlState is null)
            {
                return HttpStatusCode.InternalServerError;
            }

            if (BadRequestExceptionCodes.Contains(e.SqlState))
            {
                return HttpStatusCode.BadRequest;
            }

            if (ConflictExceptionCodes.Contains(e.SqlState))
            {
                return HttpStatusCode.Conflict;
            }

            return HttpStatusCode.InternalServerError;
        }
    }
}
