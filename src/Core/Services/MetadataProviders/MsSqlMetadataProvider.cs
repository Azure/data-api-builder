// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// MsSQL specific override for SqlMetadataProvider.
    /// All the method definitions from base class are sufficient
    /// this class is only created for symmetricity with MySql
    /// and ease of expanding the generics specific to MsSql.
    /// </summary>
    public class MsSqlMetadataProvider :
        SqlMetadataProvider<SqlConnection, SqlDataAdapter, SqlCommand>
    {
        public MsSqlMetadataProvider(
            RuntimeConfigProvider runtimeConfigProvider,
            IQueryExecutor queryExecutor,
            IQueryBuilder sqlQueryBuilder,
            ILogger<ISqlMetadataProvider> logger)
            : base(runtimeConfigProvider, queryExecutor, sqlQueryBuilder, logger)
        {
        }

        public static readonly HashSet<string> DateTimeTypes = new() { "date", "smalldatetime", "datetime", "datetime2", "datetimeoffset" };

        public override string GetDefaultSchemaName()
        {
            return "dbo";
        }

        /// <summary>
        /// Takes a string version of an SQL Server data type (also applies to Azure SQL DB)
        /// and returns its .NET common language runtime (CLR) counterpart
        /// As per https://docs.microsoft.com/dotnet/framework/data/adonet/sql-server-data-type-mappings
        /// </summary>
        public override Type SqlToCLRType(string sqlType)
        {
            return TypeHelper.GetSystemTypeFromSqlDbType(sqlType);
        }

        /// <inheritdoc/>
        public override DbType? SqlDbTypeToDbType(string sqlDbTypeName)
        {
            if (Enum.TryParse(sqlDbTypeName, ignoreCase: true, out SqlDbType sqlDbType))
            {
                // For MsSql, all the date time types i.e. date, smalldatetime, datetime, datetime2 map to System.DateTime system type.
                // Hence we cannot directly determine the DbType from the system type.
                // However, to make sure that the database correctly interprets these datatypes, it is necessary to correctly
                // populate the DbTypes.
                return TypeHelper.GetDbTypeFromSqlDbType(sqlDbType);
            }

            // This code should never be hit because every sqlDbTypeName must have a corresponding sqlDbType.
            return null;
        }
    }
}
