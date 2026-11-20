// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;

namespace Azure.DataApiBuilder.Core.Resolvers;

/// <summary>
/// Oracle 19c SQL/JSON queries and single-record PL/SQL mutations.
/// </summary>
public class OracleQueryBuilder : BaseSqlQueryBuilder, IQueryBuilder
{
    internal const string MUTATION_PREFIX = "/* DAB Oracle mutation */";
    internal const string RESULT_PARAMETER = "dab_result";
    internal const string UPDATE_INDICATOR = "___dab_updated___";

    public override string QuoteIdentifier(string ident) => "\"" + ident.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private string Table(DatabaseObject table) => string.IsNullOrEmpty(table.SchemaName)
        ? QuoteIdentifier(table.Name)
        : $"{QuoteIdentifier(table.SchemaName)}.{QuoteIdentifier(table.Name)}";

    protected override string Build(Column column) => string.IsNullOrEmpty(column.TableAlias)
        ? QuoteIdentifier(column.ColumnName)
        : $"{QuoteIdentifier(column.TableAlias)}.{QuoteIdentifier(column.ColumnName)}";

    protected override string Build(SqlJoinStructure join) =>
        $" INNER JOIN {Table(join.DbObject)} {QuoteIdentifier(join.TableAlias)} ON {Build(join.Predicates)}";

    protected override string Build(Predicate? predicate)
    {
        string? leftType = predicate?.Left?.AsColumn()?.DatabaseTypeName;
        string? rightType = predicate?.Right.AsColumn()?.DatabaseTypeName;
        string? lobType = leftType == "NCLOB" || rightType == "NCLOB" ? "NCLOB"
            : leftType == "CLOB" || rightType == "CLOB" ? "CLOB" : null;
        if (lobType is "CLOB" or "NCLOB")
        {
            if (predicate!.Op is PredicateOperation.Equal or PredicateOperation.NotEqual or PredicateOperation.LessThan or
                PredicateOperation.LessThanOrEqual or PredicateOperation.GreaterThan or PredicateOperation.GreaterThanOrEqual)
            {
                return BuildLobComparison(ResolveOperand(predicate.Left), ResolveOperand(predicate.Right), Build(predicate.Op), lobType);
            }

            if (predicate!.Op is PredicateOperation.IN)
            {
                throw UnsupportedLobOperation("IN filtering");
            }
        }

        if (predicate is { Op: PredicateOperation.LIKE or PredicateOperation.NOT_LIKE })
        {
            // Oracle has no implicit LIKE escape character. Render the clause as part
            // of the predicate so it also applies inside parentheses and subqueries.
            string sql = $"{ResolveOperand(predicate.Left)} {Build(predicate.Op)} {ResolveOperand(predicate.Right)} ESCAPE '\\'";
            return predicate.AddParenthesis ? $"({sql})" : sql;
        }

        return base.Build(predicate);
    }

    protected override string Build(KeysetPaginationPredicate? predicate)
    {
        if (predicate is null)
        {
            return string.Empty;
        }

        List<string> alternatives = new();
        for (int i = 0; i < predicate.Columns.Count; i++)
        {
            PaginationColumn column = predicate.Columns[i];
            string name = Build((Column)column);
            // Oracle sorts NULL last in ascending order and first in descending order.
            string comparison = (column.Direction, column.Value is null) switch
            {
                (OrderBy.ASC, true) => "1 = 0",
                (OrderBy.ASC, false) => $"({name} > {column.ParamName} OR {name} IS NULL)",
                (OrderBy.DESC, true) => $"{name} IS NOT NULL",
                _ => $"{name} < {column.ParamName}"
            };
            List<string> terms = predicate.Columns.Take(i).Select(previous => previous.Value is null
                ? $"{Build((Column)previous)} IS NULL"
                : $"{Build((Column)previous)} = {previous.ParamName}").ToList();
            terms.Add(comparison);
            alternatives.Add($"({string.Join(" AND ", terms)})");
        }

        return $"({string.Join(" OR ", alternatives)})";
    }

    public override string Build(BaseSqlQueryStructure structure) =>
        $"SELECT 1 FROM {Table(structure.DatabaseObject)} {QuoteIdentifier(structure.SourceAlias)}{Build(structure.Joins)} " +
        $"WHERE {JoinPredicateStrings(structure.GetDbPolicyForOperation(EntityActionOperation.Read), Build(structure.Predicates))}";

    public string Build(SqlQueryStructure structure)
        => WithBinaryConversion(structure, BuildQuery(structure));

    private string BuildQuery(SqlQueryStructure structure)
    {
        ValidateLobOrderingAndGrouping(structure);
        string order = structure.OrderByColumns.Count > 0 ? Build(structure.OrderByColumns) : "NULL";
        return BuildReadQuery(structure, order, $" FETCH FIRST {structure.Limit()} ROWS ONLY");
    }

    /// <summary>
    /// Builds an aggregation query with ordering and optional offset pagination inside
    /// the JSON aggregation. Grouping columns break ties between equal aggregate values.
    /// </summary>
    public string BuildAggregation(SqlQueryStructure structure, bool descending, int? offset = null, long? limit = null)
    {
        ValidateLobOrderingAndGrouping(structure);
        List<string> order = new();
        if (structure.GroupByMetadata.Fields.Count > 0)
        {
            string direction = descending ? "DESC" : "ASC";
            order.AddRange(structure.GroupByMetadata.Aggregations.Select(a => $"{Build(a.Column)} {direction}"));
            order.AddRange(structure.GroupByMetadata.Fields.Values.Select(c => $"{Build(c)} ASC"));
        }

        string pagination = $" FETCH FIRST {structure.Limit()} ROWS ONLY";
        if (limit.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit.Value);
            ArgumentOutOfRangeException.ThrowIfNegative(offset ?? 0);
            string offsetParameter = BaseQueryStructure.GetEncodedParamName(structure.Counter.Next());
            structure.Parameters.Add(offsetParameter, new DbConnectionParam(offset ?? 0));
            string limitParameter = BaseQueryStructure.GetEncodedParamName(structure.Counter.Next());
            structure.Parameters.Add(limitParameter, new DbConnectionParam(limit.Value));
            pagination = $" OFFSET {offsetParameter} ROWS FETCH NEXT {limitParameter} ROWS ONLY";
        }

        return WithBinaryConversion(structure, BuildReadQuery(structure, order.Count > 0 ? string.Join(", ", order) : "NULL", pagination));
    }

    private string BuildReadQuery(SqlQueryStructure structure, string order, string pagination)
    {
        List<string> members = new();
        foreach (LabelledColumn column in structure.Columns)
        {
            string value;
            if (column.ColumnName == SqlQueryStructure.DATA_IDENT && column.TableAlias is not null && structure.JoinQueries.TryGetValue(column.TableAlias, out SqlQueryStructure? nested))
            {
                value = $"({BuildQuery(nested)}) FORMAT JSON";
            }
            else
            {
                value = Build((Column)column);
                if (structure.GetColumnSystemType(column.ColumnName) == typeof(byte[]))
                {
                    value = $"dab_base64({value})";
                }
            }

            members.Add($"'{EscapeLiteral(column.Label)}' VALUE {value}");
        }

        members.AddRange(structure.GroupByMetadata.Aggregations.Select(a =>
            $"'{EscapeLiteral(a.Column.OperationAlias)}' VALUE {Build(a.Column)}"));

        string predicates = JoinPredicateStrings(
            structure.GetDbPolicyForOperation(EntityActionOperation.Read),
            structure.FilterPredicates,
            Build(structure.Predicates),
            Build(structure.PaginationMetadata.PaginationPredicate));
        string data = QuoteIdentifier(SqlQueryStructure.DATA_IDENT);
        string query = $"SELECT JSON_OBJECT({string.Join(", ", members)} NULL ON NULL RETURNING CLOB) AS {data}, " +
            $"ROW_NUMBER() OVER (ORDER BY {order}) AS dab_order " +
            $"FROM {Table(structure.DatabaseObject)} {QuoteIdentifier(structure.SourceAlias)}{Build(structure.Joins)} " +
            $"WHERE {predicates}{BuildGroupBy(structure)}{BuildHaving(structure)}" +
            $" ORDER BY {order}{pagination}";

        return structure.IsListQuery
            ? $"SELECT COALESCE(JSON_ARRAYAGG({data} FORMAT JSON ORDER BY dab_order RETURNING CLOB), JSON_ARRAY(RETURNING CLOB)) AS {data} FROM ({query})"
            : $"SELECT {data} FROM ({query})";
    }

    internal static string BuildLobComparison(string left, string right, string operation, string type)
    {
        // COMPARE promises zero/nonzero, not a lexical ordering for non-equal LOBs.
        if (operation is not ("=" or "!=" or "<>"))
        {
            throw UnsupportedLobOperation("relational comparisons");
        }

        string convert = type == "NCLOB" ? "TO_NCLOB" : "TO_CLOB";
        return $"(DBMS_LOB.COMPARE({convert}({left}), {convert}({right})) {operation} 0)";
    }

    private static void ValidateLobOrderingAndGrouping(SqlQueryStructure structure)
    {
        foreach (Column column in structure.OrderByColumns.Cast<Column>().Concat(structure.GroupByMetadata.Fields.Values))
        {
            if (structure.GetUnderlyingSourceDefinition().Columns.TryGetValue(column.ColumnName, out ColumnDefinition? definition) &&
                definition.DatabaseTypeName is "CLOB" or "NCLOB")
            {
                throw UnsupportedLobOperation("sorting or grouping");
            }
        }
    }

    private static DataApiBuilderException UnsupportedLobOperation(string operation) => new(
        $"Oracle does not support {operation} on CLOB or NCLOB columns. Use a VARCHAR2 projection with explicit application-defined semantics instead.",
        HttpStatusCode.BadRequest, DataApiBuilderException.SubStatusCodes.BadRequest);

    private static bool HasBinaryColumns(SqlQueryStructure structure) =>
        structure.Columns.Any(column => column.ColumnName != SqlQueryStructure.DATA_IDENT &&
            structure.GetColumnSystemType(column.ColumnName) == typeof(byte[])) ||
        structure.JoinQueries.Values.Any(HasBinaryColumns);

    private static string WithBinaryConversion(SqlQueryStructure structure, string sql) =>
        HasBinaryColumns(structure) ? BINARY_CONVERSION_FUNCTIONS + sql : sql;

    // Statement-local functions require no schema objects or CREATE FUNCTION privilege.
    // Chunks are divisible by three, so padding occurs only at the end of the BLOB.
    // Keep each encoded chunk below PL/SQL's 32767-byte RAW/VARCHAR2 limit.
    private const string BINARY_CONVERSION_FUNCTIONS = """
        WITH
        FUNCTION dab_base64(value BLOB) RETURN CLOB IS
            result CLOB;
            position PLS_INTEGER := 1;
            chunk VARCHAR2(32767);
        BEGIN
            IF value IS NULL THEN RETURN NULL; END IF;
            DBMS_LOB.CREATETEMPORARY(result, TRUE, DBMS_LOB.CALL);
            WHILE position <= DBMS_LOB.GETLENGTH(value) LOOP
                chunk := UTL_RAW.CAST_TO_VARCHAR2(UTL_ENCODE.BASE64_ENCODE(DBMS_LOB.SUBSTR(value, 12000, position)));
                chunk := REPLACE(REPLACE(chunk, CHR(13)), CHR(10));
                DBMS_LOB.WRITEAPPEND(result, LENGTH(chunk), chunk);
                position := position + 12000;
            END LOOP;
            RETURN result;
        EXCEPTION WHEN OTHERS THEN
            IF DBMS_LOB.ISTEMPORARY(result) = 1 THEN DBMS_LOB.FREETEMPORARY(result); END IF;
            RAISE;
        END;
        FUNCTION dab_base64(value RAW) RETURN CLOB IS
        BEGIN
            RETURN dab_base64(TO_BLOB(value));
        END;

        """;

    public string Build(SqlInsertStructure structure)
    {
        RejectCreatePolicy(structure);
        return WrapMutation(structure.DatabaseObject, structure.OutputColumns,
            Insert(structure.DatabaseObject, structure.InsertColumns, structure.Values, structure.OutputColumns), false);
    }

    public string Build(SqlUpdateStructure structure)
    {
        string predicates = JoinPredicateStrings(structure.GetDbPolicyForOperation(EntityActionOperation.Update), Build(structure.Predicates));
        string statement = $"UPDATE {Table(structure.DatabaseObject)} SET {Build(structure.UpdateOperations, ", ")} WHERE {predicates}" +
            Returning(structure.OutputColumns) + "; dab_affected := SQL%ROWCOUNT;";
        return WrapMutation(structure.DatabaseObject, structure.OutputColumns, statement, false);
    }

    public string Build(SqlDeleteStructure structure) =>
        $"DELETE FROM {Table(structure.DatabaseObject)} WHERE " +
        JoinPredicateStrings(structure.GetDbPolicyForOperation(EntityActionOperation.Delete), Build(structure.Predicates));

    public string Build(SqlUpsertQueryStructure structure)
    {
        if (!structure.IsFallbackToUpdate)
        {
            RejectCreatePolicy(structure);
        }

        string table = Table(structure.DatabaseObject);
        string keys = Build(structure.Predicates);
        string predicates = JoinPredicateStrings(keys, structure.GetDbPolicyForOperation(EntityActionOperation.Update));
        string update = $"UPDATE {table} SET {Build(structure.UpdateOperations, ", ")} WHERE {predicates}" +
            Returning(structure.OutputColumns) + "; dab_affected := SQL%ROWCOUNT; dab_updated := 1;";
        string count = $"SELECT COUNT(*) INTO dab_exists FROM {table} WHERE {keys};";
        string policyFailure = $"RAISE_APPLICATION_ERROR(-{OracleDbExceptionParser.POLICY_FAILURE}, 'Database policy failure.');";
        string missing = $"RAISE_APPLICATION_ERROR(-{OracleDbExceptionParser.ROW_NOT_FOUND}, 'Record not found.');";
        string statement = update + $" IF dab_affected = 0 THEN {count} IF dab_exists > 0 THEN {policyFailure} ELSE ";
        if (structure.IsFallbackToUpdate)
        {
            statement += missing;
        }
        else
        {
            // A competing insert is serialized by Oracle's unique index. Only retry UPDATE when
            // that conflict belongs to the requested PK, never when a different unique key collides.
            statement += "BEGIN " + Insert(structure.DatabaseObject, structure.InsertColumns, structure.Values, structure.OutputColumns) +
                $" dab_updated := 0; EXCEPTION WHEN DUP_VAL_ON_INDEX THEN {count} IF dab_exists = 0 THEN RAISE; END IF; " +
                update + $" IF dab_affected = 0 THEN {policyFailure} END IF; END;";
        }

        statement += " END IF; END IF;";
        return WrapMutation(structure.DatabaseObject, structure.OutputColumns, statement, true);
    }

    private static void RejectCreatePolicy(BaseSqlQueryStructure structure)
    {
        if (!string.IsNullOrEmpty(structure.GetDbPolicyForOperation(EntityActionOperation.Create)))
        {
            throw new DataApiBuilderException("Oracle does not support database policies for operations that can create rows.",
                HttpStatusCode.BadRequest, DataApiBuilderException.SubStatusCodes.BadRequest);
        }
    }

    private string Insert(DatabaseObject table, List<string> columns, List<string> values, List<LabelledColumn> output)
    {
        // Oracle 19c has no DEFAULT VALUES form. A single DEFAULT assignment also works for identity columns.
        string insertColumns = columns.Count > 0 ? Build(columns) : QuoteIdentifier(output[0].ColumnName);
        string insertValues = values.Count > 0 ? string.Join(", ", values) : "DEFAULT";
        return $"INSERT INTO {Table(table)} ({insertColumns}) VALUES ({insertValues}){Returning(output)}; dab_affected := SQL%ROWCOUNT;";
    }

    private string Returning(List<LabelledColumn> output) =>
        $" RETURNING {string.Join(", ", output.Select(c => QuoteIdentifier(c.ColumnName)))} INTO " +
        string.Join(", ", output.Select((_, i) => $"dab_value{i}"));

    private string WrapMutation(DatabaseObject table, List<LabelledColumn> output, string statement, bool upsert)
    {
        string declarations = string.Join(" ", output.Select((c, i) => $"dab_value{i} {Table(table)}.{QuoteIdentifier(c.ColumnName)}%TYPE;"));
        string selections = string.Join(", ", output.Select((c, i) => $"dab_value{i} AS {QuoteIdentifier(c.Label)}"));
        if (upsert)
        {
            selections += $", dab_updated AS {QuoteIdentifier(UPDATE_INDICATOR)}";
        }

        return $"{MUTATION_PREFIX} DECLARE {declarations} dab_affected PLS_INTEGER := 0; dab_exists PLS_INTEGER; dab_updated PLS_INTEGER := 0; " +
            $"BEGIN {statement} OPEN :{RESULT_PARAMETER} FOR SELECT {selections} FROM DUAL WHERE dab_affected > 0; END;";
    }

    public override string BuildForeignKeyInfoQuery(int numberOfParameters)
    {
        string schemas = string.Join(", ", CreateParams(SCHEMA_NAME_PARAM, numberOfParameters).Select(p => ":" + p));
        string tables = string.Join(", ", CreateParams(TABLE_NAME_PARAM, numberOfParameters).Select(p => ":" + p));
        return $"""
            SELECT fk.CONSTRAINT_NAME AS "ForeignKeyDefinition",
                   child.OWNER AS "ReferencingSchemaName", child.TABLE_NAME AS "ReferencingSourceDefinition",
                   child.COLUMN_NAME AS "ReferencingColumns", parent.OWNER AS "ReferencedSchemaName",
                   parent.TABLE_NAME AS "ReferencedSourceDefinition", parent.COLUMN_NAME AS "ReferencedColumns"
            FROM ALL_CONSTRAINTS fk
            JOIN ALL_CONS_COLUMNS child ON child.OWNER = fk.OWNER AND child.CONSTRAINT_NAME = fk.CONSTRAINT_NAME
            JOIN ALL_CONS_COLUMNS parent ON parent.OWNER = fk.R_OWNER AND parent.CONSTRAINT_NAME = fk.R_CONSTRAINT_NAME
                AND parent.POSITION = child.POSITION
            WHERE fk.CONSTRAINT_TYPE = 'R' AND child.OWNER IN ({schemas}) AND child.TABLE_NAME IN ({tables})
            ORDER BY fk.OWNER, fk.CONSTRAINT_NAME, child.POSITION
            """;
    }

    public string BuildQueryToGetReadOnlyColumns(string schemaOrDatabaseParamName, string tableParamName) =>
        $"SELECT COLUMN_NAME FROM ALL_TAB_COLS WHERE OWNER = {schemaOrDatabaseParamName} AND TABLE_NAME = {tableParamName} AND VIRTUAL_COLUMN = 'YES'";

    public string QuoteTableNameAsDBConnectionParam(string param) => param;

    public string Build(SqlExecuteStructure structure) => throw new NotSupportedException("Oracle stored procedure entities are not supported.");

    public string BuildStoredProcedureResultDetailsQuery(string databaseObjectName) => throw new NotSupportedException("Oracle stored procedure entities are not supported.");

    private static string EscapeLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
