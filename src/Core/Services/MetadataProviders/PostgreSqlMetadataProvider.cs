// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using System.Net;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// PostgreSql specific override for SqlMetadataProvider.
    /// All the method definitions from base class are sufficient
    /// this class is only created for symmetricity with MySql
    /// and ease of expanding the generics specific to PostgreSql.
    /// </summary>
    public class PostgreSqlMetadataProvider :
        SqlMetadataProvider<NpgsqlConnection, NpgsqlDataAdapter, NpgsqlCommand>
    {

        public PostgreSqlMetadataProvider(
            RuntimeConfigProvider runtimeConfigProvider,
            IAbstractQueryManagerFactory queryManagerFactory,
            ILogger<ISqlMetadataProvider> logger,
            string dataSourceName,
            bool isValidateOnly = false)
            : base(runtimeConfigProvider, queryManagerFactory, logger, dataSourceName, isValidateOnly)
        {
        }

        /// <summary>
        /// Only used for PostgreSql.
        /// The connection string could contain the schema,
        /// in which case it will be associated with the
        /// property 'SearchPath' in the string builder we create.
        /// If `SearchPath` is null we assign the empty string to the
        /// the out param schemaName, otherwise we assign the
        /// value associated with `SearchPath`.
        /// </summary>
        /// <param name="schemaName">the schema name we save.</param>
        /// <returns>true if non empty schema in connection string, false otherwise.</returns>
        public static bool TryGetSchemaFromConnectionString(string connectionString, out string schemaName)
        {
            NpgsqlConnectionStringBuilder connectionStringBuilder;
            try
            {
                connectionStringBuilder = new(connectionString);
            }
            catch (Exception ex)
            {
                throw new DataApiBuilderException(
                    message: DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE,
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization,
                    innerException: ex);
            }

            schemaName = connectionStringBuilder.SearchPath is null ? string.Empty : connectionStringBuilder.SearchPath;
            return string.IsNullOrEmpty(schemaName) ? false : true;
        }

        public override string GetDefaultSchemaName()
        {
            return "public";
        }
        /// <inheritdoc/>
        protected override Task FillSchemaForStoredProcedureAsync(
            Entity procedureEntity,
            string entityName,
            string schemaName,
            string storedProcedureSourceName,
            StoredProcedureDefinition storedProcedureDefinition)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Takes a string version of a PostgreSql data type and returns its .NET common language runtime (CLR) counterpart
        /// TODO: For PostgreSql stored procedure support, this needs to be implemented.
        /// </summary>
        public override Type SqlToCLRType(string sqlType)
        {
            switch (sqlType.ToLower())
            {
                case "boolean":
                case "bool":
                    return typeof(bool);

                case "smallint":
                    return typeof(short);

                case "integer":
                case "int":
                    return typeof(int);

                case "bigint":
                    return typeof(long);

                case "real":
                    return typeof(float);

                case "double precision":
                    return typeof(double);

                case "numeric":
                case "decimal":
                    return typeof(decimal);

                case "money":
                    return typeof(decimal);

                case "character varying":
                case "varchar":
                case "character":
                case "char":
                case "text":
                    return typeof(string);

                case "bytea":
                    return typeof(byte[]);

                case "date":
                    return typeof(DateTime);

                case "timestamp":
                case "timestamp without time zone":
                    return typeof(DateTime);

                case "timestamp with time zone":
                    return typeof(DateTimeOffset);

                case "time":
                case "time without time zone":
                    return typeof(TimeSpan);

                case "time with time zone":
                    return typeof(DateTimeOffset);

                case "interval":
                    return typeof(TimeSpan);

                case "uuid":
                    return typeof(Guid);

                case "json":
                case "jsonb":
                    return typeof(string);

                case "xml":
                    return typeof(string);

                case "inet":
                    return typeof(System.Net.IPAddress);

                case "cidr":
                    return typeof(ValueTuple<System.Net.IPAddress, int>);

                case "macaddr":
                    return typeof(System.Net.NetworkInformation.PhysicalAddress);

                case "bit":
                case "bit varying":
                    return typeof(BitArray);

                case "point":
                    return typeof((double, double));

                case "line":
                    return typeof(string); // Implement a custom type if needed

                case "lseg":
                    return typeof((double, double)[]);

                case "box":
                    return typeof((double, double)[]);

                case "path":
                    return typeof((double, double)[]);

                case "polygon":
                    return typeof((double, double)[]);

                case "circle":
                    return typeof((double, double, double));

                case "tsvector":
                    return typeof(string); // Implement a custom type if needed

                case "tsquery":
                    return typeof(string); // Implement a custom type if needed

                default:
                    throw new NotSupportedException($"The SQL type '{sqlType}' is not supported.");
            }
        }

    }
}
