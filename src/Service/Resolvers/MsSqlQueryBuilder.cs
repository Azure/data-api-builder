// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using Azure.DataApiBuilder.Service.Models;
using Microsoft.Data.SqlClient;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Class for building MsSql queries.
    /// </summary>
    public class MsSqlQueryBuilder : BaseSqlQueryBuilder, IQueryBuilder
    {
        private const string FOR_JSON_SUFFIX = " FOR JSON PATH, INCLUDE_NULL_VALUES";
        private const string WITHOUT_ARRAY_WRAPPER_SUFFIX = "WITHOUT_ARRAY_WRAPPER";

        private static DbCommandBuilder _builder = new SqlCommandBuilder();

        /// <inheritdoc />
        public override string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
        }

        /// <inheritdoc />
        public string Build(SqlQueryStructure structure)
        {
            string dataIdent = QuoteIdentifier(SqlQueryStructure.DATA_IDENT);
            string fromSql = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                             $"AS {QuoteIdentifier($"{structure.SourceAlias}")}{Build(structure.Joins)}";

            fromSql += string.Join(
                    "",
                    structure.JoinQueries.Select(
                        x => $" OUTER APPLY ({Build(x.Value)}) AS {QuoteIdentifier(x.Key)}({dataIdent})"));

            string predicates = JoinPredicateStrings(
                                    structure.DbPolicyPredicatesForOperation[Config.Operation.Read],
                                    structure.FilterPredicates,
                                    Build(structure.Predicates),
                                    Build(structure.PaginationMetadata.PaginationPredicate));

            string query = $"SELECT TOP {structure.Limit()} {WrappedColumns(structure)}"
                + $" FROM {fromSql}"
                + $" WHERE {predicates}"
                + $" ORDER BY {Build(structure.OrderByColumns)}";

            query += FOR_JSON_SUFFIX;
            if (!structure.IsListQuery)
            {
                query += "," + WITHOUT_ARRAY_WRAPPER_SUFFIX;
            }

            return query;
        }

        /// <inheritdoc />
        public string Build(SqlInsertStructure structure)
        {
            string predicates = JoinPredicateStrings(structure.DbPolicyPredicatesForOperation[Config.Operation.Create]);
            string insertColumns = Build(structure.InsertColumns);
            string insertIntoStatementPrefix = $"INSERT INTO {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} ({insertColumns}) " +
                $"OUTPUT {MakeOutputColumns(structure.OutputColumns, OutputQualifier.Inserted)} ";
            string values = string.IsNullOrEmpty(structure.DbPolicyPredicatesForOperation[Config.Operation.Create]) ?
                $"VALUES ({string.Join(", ", structure.Values)});" : $"SELECT {insertColumns} FROM (VALUES({string.Join(", ", structure.Values)})) T({insertColumns}) WHERE {predicates};";
            StringBuilder insertQuery = new(insertIntoStatementPrefix);
            return insertQuery.Append(values).ToString();
        }

        /// <inheritdoc />
        public string Build(SqlUpdateStructure structure)
        {
            string predicates = JoinPredicateStrings(
                                   structure.DbPolicyPredicatesForOperation[Config.Operation.Update],
                                   Build(structure.Predicates));

            return $"UPDATE {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                    $"SET {Build(structure.UpdateOperations, ", ")} " +
                    $"OUTPUT {MakeOutputColumns(structure.OutputColumns, OutputQualifier.Inserted)} " +
                    $"WHERE {predicates};";
        }

        /// <inheritdoc />
        public string Build(SqlDeleteStructure structure)
        {
            string predicates = JoinPredicateStrings(
                       structure.DbPolicyPredicatesForOperation[Config.Operation.Delete],
                       Build(structure.Predicates));

            return $"DELETE FROM {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                    $"WHERE {predicates} ";
        }

        /// <inheritdoc />
        public string Build(SqlExecuteStructure structure)
        {
            return $"EXECUTE {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                $"{BuildProcedureParameterList(structure.ProcedureParameters)}";
        }

        /// <summary>
        /// Avoid redundant check, wrap the sequence in a transaction,
        /// and protect the first table access with appropriate locking.
        /// </summary>
        /// <param name="structure"></param>
        /// <returns></returns>
        public string Build(SqlUpsertQueryStructure structure)
        {
            string updatePredicates = JoinPredicateStrings(Build(structure.Predicates), structure.DbPolicyPredicatesForOperation[Config.Operation.Update]);
            string insertPredicates = JoinPredicateStrings(structure.DbPolicyPredicatesForOperation[Config.Operation.Create]);
            if (structure.IsFallbackToUpdate)
            {
                return $"UPDATE {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                    $"SET {Build(structure.UpdateOperations, ", ")} " +
                    $"OUTPUT {MakeOutputColumns(structure.OutputColumns, OutputQualifier.Inserted)} " +
                    $"WHERE {updatePredicates};";
            }
            else
            {
                string updateQuery = $"SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;BEGIN TRANSACTION; UPDATE {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                    $"WITH(UPDLOCK) SET {Build(structure.UpdateOperations, ", ")} " +
                    $"OUTPUT {MakeOutputColumns(structure.OutputColumns, OutputQualifier.Inserted)} " +
                    $"WHERE {updatePredicates} ";
                StringBuilder upsertQuery = new(updateQuery);
                string insertQuery;
                string insertColumns = Build(structure.InsertColumns);
                if (insertPredicates.Equals(BASE_PREDICATE))
                {
                    insertQuery = $"IF @@ROWCOUNT = 0 " +
                        $"BEGIN; " +
                        $"INSERT INTO {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} ({insertColumns}) " +
                        $"OUTPUT {MakeOutputColumns(structure.OutputColumns, OutputQualifier.Inserted)} " +
                        $"VALUES ({string.Join(", ", structure.Values)}) " +
                        $"END; COMMIT TRANSACTION";
                }
                else
                {
                    insertQuery = $"IF @@ROWCOUNT = 0 " +
                        $"BEGIN; " +
                        $"INSERT INTO {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} ({insertColumns}) " +
                        $"OUTPUT {MakeOutputColumns(structure.OutputColumns, OutputQualifier.Inserted)} " +
                        $"SELECT {insertColumns} " +
                        $"FROM (VALUES({string.Join(", ", structure.Values)})) T({insertColumns}) " +
                        $"WHERE {insertPredicates} " +
                        $"END; COMMIT TRANSACTION";
                }

                return upsertQuery.Append(insertQuery).ToString();
            }
        }

        /// <summary>
        /// Labels with which columns can be marked in the OUTPUT clause
        /// </summary>
        private enum OutputQualifier { Inserted, Deleted };

        /// <summary>
        /// Adds qualifiers (inserted or deleted) to output columns in OUTPUT clause
        /// and joins them with commas. e.g. for outputcolumns [C1, C2, C3] and output
        /// qualifier Inserted return
        /// Inserted.ColumnName1 AS {Label1}, Inserted.ColumnName2 AS {Label2},
        /// Inserted.ColumnName3 AS {Label3}
        /// </summary>
        private string MakeOutputColumns(List<LabelledColumn> columns, OutputQualifier outputQualifier)
        {
            return string.Join(", ", columns.Select(c => Build(c, outputQualifier)));
        }

        /// <summary>
        /// Build a labelled column as a column and attach
        /// ... AS {Label} to it
        /// </summary>
        private string Build(LabelledColumn column, OutputQualifier outputQualifier)
        {
            return $"{outputQualifier}.{QuoteIdentifier(column.ColumnName)} AS {QuoteIdentifier(column.Label)}";
        }

        /// <summary>
        /// Add a JSON_QUERY wrapper on the column
        /// </summary>
        private string WrapSubqueryColumn(LabelledColumn column, SqlQueryStructure subquery)
        {
            string builtColumn = Build(column as Column);
            if (subquery.IsListQuery)
            {
                return $"JSON_QUERY (COALESCE({builtColumn}, '[]'))";
            }

            return $"JSON_QUERY ({builtColumn})";
        }

        /// <summary>
        /// Build columns and wrap columns which represent join subqueries
        /// </summary>
        private string WrappedColumns(SqlQueryStructure structure)
        {
            return string.Join(", ",
                structure.Columns.Select(
                    c => structure.IsSubqueryColumn(c) ?
                        WrapSubqueryColumn(c, structure.JoinQueries[c.TableAlias!]) + $" AS {QuoteIdentifier(c.Label)}" :
                        Build(c)
            ));
        }

        /// <summary>
        /// Builds the parameter list for the stored procedure execute call
        /// paramKeys are the user-generated procedure parameter names
        /// paramValues are the auto-generated, parameterized values (@param0, @param1..)
        /// </summary>
        private static string BuildProcedureParameterList(Dictionary<string, object> procedureParameters)
        {
            StringBuilder sb = new();
            foreach ((string paramKey, object paramValue) in procedureParameters)
            {
                sb.Append($"@{paramKey} = {paramValue}, ");
            }

            string parameterList = sb.ToString();
            // If at least one parameter added, remove trailing comma and space, else return empty string
            return parameterList.Length > 0 ? parameterList[..^2] : parameterList;
        }

        /// <summary>
        /// Builds the query to fetch result set details of stored-procedure.
        /// result_field_name is the name of the result column.
        /// result_type contains the sql type, i.e char,int,varchar. Using TYPE_NAME method
        /// allows us to get the type without size constraints. example: TYPE_NAME for both
        /// varchar(100) and varchar(max) would be varchar.
        /// is_nullable is a boolean value to know if the result column is nullable or not.
        /// </summary>
        public string BuildStoredProcedureResultDetailsQuery(string databaseObjectName)
        {
            string query = "SELECT " +
                            "name as result_field_name, TYPE_NAME(system_type_id) as result_type, is_nullable " +
                            "FROM " +
                            "sys.dm_exec_describe_first_result_set_for_object (" +
                            $"OBJECT_ID('{databaseObjectName}'), 0) " +
                            "WHERE is_hidden is not NULL AND is_hidden = 0";
            return query;
        }
    }
}
