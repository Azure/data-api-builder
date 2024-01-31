// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Service.Exceptions;
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
            IAbstractQueryManagerFactory queryManagerFactory,
            ILogger<ISqlMetadataProvider> logger,
            string dataSourceName)
            : base(runtimeConfigProvider, queryManagerFactory, logger, dataSourceName)
        {
        }

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
        public override async Task PopulateTriggerMetadataForTable(string entityName, string schemaName, string tableName, SourceDefinition sourceDefinition)
        {
            string enumerateEnabledTriggers = SqlQueryBuilder.BuildFetchEnabledTriggersQuery();
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { $"{BaseQueryStructure.PARAM_NAME_PREFIX}param0", new(schemaName, DbType.String) },
                { $"{BaseQueryStructure.PARAM_NAME_PREFIX}param1", new(tableName, DbType.String) }
            };

            JsonArray? resultArray = await QueryExecutor.ExecuteQueryAsync(
                sqltext: enumerateEnabledTriggers,
                parameters: parameters,
                dataReaderHandler: QueryExecutor.GetJsonArrayAsync);
            using JsonDocument sqlResult = JsonDocument.Parse(resultArray!.ToJsonString());

            foreach (JsonElement element in sqlResult.RootElement.EnumerateArray())
            {
                string type_desc = element.GetProperty("type_desc").ToString();
                if ("UPDATE".Equals(type_desc))
                {
                    sourceDefinition.IsUpdateDMLTriggerEnabled = true;
                    _logger.LogInformation($"An update trigger is enabled for the entity: {entityName}");
                }

                if ("INSERT".Equals(type_desc))
                {
                    sourceDefinition.IsInsertDMLTriggerEnabled = true;
                    _logger.LogInformation($"An insert trigger is enabled for the entity: {entityName}");
                }
            }
        }

        /// <inheritdoc/>
        protected override void FillInferredFkInfo(
            IEnumerable<SourceDefinition> dbEntitiesToBePopulatedWithFK)
        {
            // For each table definition that has to be populated with the inferred
            // foreign key information.
            foreach (SourceDefinition sourceDefinition in dbEntitiesToBePopulatedWithFK)
            {
                // For each source entities, which maps to this table definition
                // and has a relationship metadata to be filled.
                foreach ((string sourceEntityName, RelationshipMetadata relationshipData)
                       in sourceDefinition.SourceEntityRelationshipMap)
                {
                    // Enumerate all the foreign keys required for all the target entities
                    // that this source is related to.
                    foreach ((string targetEntityName, List<ForeignKeyDefinition> foreignKeyDefinitionsToTarget) in relationshipData.TargetEntityToFkDefinitionMap)
                    {
                        List<ForeignKeyDefinition> validateForeignKeyDefinitionsToTarget = new();
                        HashSet<RelationShipPair> inverseFKPairs = new();
                        // For each foreign key between this pair of source and target entities
                        // which needs the referencing columns,
                        // find the fk inferred for this pair the backend and
                        // equate the referencing columns and referenced columns.
                        foreach (ForeignKeyDefinition foreignKeyDefinitionToTarget in foreignKeyDefinitionsToTarget)
                        {
                            // Add the referencing and referenced columns for this foreign key definition for the target.
                            if (PairToFkDefinition is not null &&
                                PairToFkDefinition.TryGetValue(foreignKeyDefinitionToTarget.Pair, out ForeignKeyDefinition? inferredDefinition))
                            {
                                if (inverseFKPairs.Contains(foreignKeyDefinitionToTarget.Pair))
                                {
                                    // This means that there are 2 relationships defined in the database:
                                    // 1. From source to target
                                    // 2. From target to source
                                    // It is not possible to determine the direction of relationship in such a case, so we throw an exception.
                                    throw new DataApiBuilderException(
                                        message: $"Circular relationship detected between source entity: {sourceEntityName} and target entity: {targetEntityName}. Cannot support nested insertion.",
                                        statusCode: HttpStatusCode.Conflict,
                                        subStatusCode: DataApiBuilderException.SubStatusCodes.NotSupported);
                                }

                                // Add an entry to inverseFKPairs to track what all (target, source) pairings are not allowed to
                                // have a relationship in the database in order to support nested insertion.
                                inverseFKPairs.Add(new(foreignKeyDefinitionToTarget.Pair.ReferencedDbTable, foreignKeyDefinitionToTarget.Pair.ReferencingDbTable));

                                // if the referencing and referenced columns count > 0,
                                // we have already gathered this information from the runtime config.
                                if (foreignKeyDefinitionToTarget.ReferencingColumns.Count > 0 && foreignKeyDefinitionToTarget.ReferencedColumns.Count > 0)
                                {
                                    if (!AreFKDefinitionsEqual(foreignKeyDefinitionToTarget, inferredDefinition))
                                    {
                                        throw new DataApiBuilderException(
                                            message: $"The relationship defined between source entity: {sourceEntityName} and target entity: {targetEntityName} conflicts" +
                                            $" with the relationship defined in the database.",
                                            statusCode: HttpStatusCode.Conflict,
                                            subStatusCode: DataApiBuilderException.SubStatusCodes.NotSupported);
                                    }
                                    else
                                    {
                                        validateForeignKeyDefinitionsToTarget.Add(foreignKeyDefinitionToTarget);
                                    }
                                }
                                // Only add the referencing/referenced columns if they have not been
                                // specified in the configuration file.
                                else
                                {
                                    validateForeignKeyDefinitionsToTarget.Add(inferredDefinition);
                                }
                            }
                            else
                            {
                                validateForeignKeyDefinitionsToTarget.Add(foreignKeyDefinitionToTarget);
                            }
                        }

                        relationshipData.TargetEntityToFkDefinitionMap[targetEntityName] = validateForeignKeyDefinitionsToTarget;
                    }
                }
            }
        }

        /// <summary>
        /// Helper method to compare two foreign key definitions for equality on the basis of the referencing -> referenced column mappings present in them.
        /// The equality ensures that both the foreign key definitions have:
        /// 1. Same set of referencing and referenced tables,
        /// 2. Same number of referencing/referenced columns,
        /// 3. Same mappings from referencing -> referenced column.
        /// </summary>
        /// <param name="fkDefinition1">First foreign key definition.</param>
        /// <param name="fkDefinition2">Second foreign key definition.</param>
        /// <returns>true if all the above mentioned conditions are met, else false.</returns>
        private static bool AreFKDefinitionsEqual(ForeignKeyDefinition fkDefinition1, ForeignKeyDefinition fkDefinition2)
        {
            if (!fkDefinition1.Pair.Equals(fkDefinition2.Pair) || fkDefinition1.ReferencingColumns.Count != fkDefinition2.ReferencingColumns.Count)
            {
                return false;
            }

            Dictionary<string, string> referencingToReferencedColumns = fkDefinition1.ReferencingColumns.Zip(
                fkDefinition1.ReferencedColumns, (key, value) => new { Key = key, Value = value }).ToDictionary(item => item.Key, item => item.Value);

            // Traverse through each (referencing, referenced) columns pair in the second foreign key definition.
            for (int idx = 0; idx < fkDefinition2.ReferencingColumns.Count; idx++)
            {
                string referencingColumnName = fkDefinition2.ReferencingColumns[idx];
                if (!referencingToReferencedColumns.TryGetValue(referencingColumnName, out string? referencedColumnName)
                    || !referencedColumnName.Equals(fkDefinition2.ReferencedColumns[idx]))
                {
                    // This indicates that either there is no mapping defined for referencingColumnName in the second foreign key definition
                    // or the referencing -> referenced column mapping in the second foreign key definition do not match the mapping in the first foreign key definition.
                    // In both the cases, it is implied that the two foreign key definitions do not match.
                    return false;
                }
            }

            return true;
        }
    }
}
