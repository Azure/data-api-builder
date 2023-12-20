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
                                        message: $"Circular relationship detected between entities: {sourceEntityName} and {targetEntityName}. Cannot support nested insertion.",
                                        statusCode: HttpStatusCode.Forbidden,
                                        subStatusCode: DataApiBuilderException.SubStatusCodes.NotSupported);
                                }

                                // Add an entry to inverseFKPairs to track what all (target, source) pairings are not allowed to
                                // have a relationship in the database in order to support nested insertion.
                                inverseFKPairs.Add(new(foreignKeyDefinitionToTarget.Pair.ReferencedDbTable, foreignKeyDefinitionToTarget.Pair.ReferencingDbTable));

                                // if the referencing and referenced columns count > 0,
                                // we have already gathered this information from the runtime config.
                                if (foreignKeyDefinitionToTarget.ReferencingColumns.Count > 0 && foreignKeyDefinitionToTarget.ReferencedColumns.Count > 0)
                                {
                                    ForeignKeyDefinition combinedFKDefinition = ValidateAndCombineInferredAndProvidedFkDefinitions(foreignKeyDefinitionToTarget, inferredDefinition);
                                    validateForeignKeyDefinitionsToTarget.Add(combinedFKDefinition);
                                }
                                // Only add the referencing/referenced columns if they have not been
                                // specified in the configuration file.
                                else
                                {
                                    validateForeignKeyDefinitionsToTarget.Add(inferredDefinition);
                                }
                            }
                        }

                        relationshipData.TargetEntityToFkDefinitionMap[targetEntityName] = validateForeignKeyDefinitionsToTarget;
                    }
                }
            }
        }

        /// <summary>
        /// This method is called when a foreign key definition has been provided by the user in the config for a (source, target) pair
        /// of entities and the database has also inferred a foreign key definition for the same pair. In such a case:
        /// 1. Ensure that one referencing column from the source entity relates to exactly one referenced column in the target entity.
        /// 2. Ensure that both foreign key definitions relate one referencing column from the source entity to the same referenced column in the target entity.
        /// 3. If there are additional relationships defined in the database, then those should also be honored.
        /// </summary>
        /// <param name="foreignKeyDefinitionToTarget">Foreign key definition defined in the runtime config for (source, target).</param>
        /// <param name="inferredDefinition">Foreign key definition inferred from the database for (source, target).</param>
        /// <returns>Combined foreign key definition for (source, target) containing a logical union of the above two foreign key definitions.</returns>
        /// <exception cref="DataApiBuilderException">Thrown when one referencing column relates to multiple referenced columns.</exception>
        private static ForeignKeyDefinition ValidateAndCombineInferredAndProvidedFkDefinitions(ForeignKeyDefinition foreignKeyDefinitionToTarget, ForeignKeyDefinition inferredDefinition)
        {
            // Dictionary to store the final relations from referencing to referenced columns. This will be initialized with the relationship
            // defined in the config.
            Dictionary<string, string> referencingToReferencedColumns = foreignKeyDefinitionToTarget.ReferencingColumns.Zip(
                foreignKeyDefinitionToTarget.ReferencedColumns, (key, value) => new { Key = key, Value = value }).ToDictionary(item => item.Key, item => item.Value);

            // Traverse through each (referencing, referenced) columns pair inferred by the database.
            for (int idx = 0; idx < inferredDefinition.ReferencingColumns.Count; idx++)
            {
                string referencingColumnName = inferredDefinition.ReferencingColumns[idx];
                if (referencingToReferencedColumns.TryGetValue(referencingColumnName, out string? referencedColumnName)
                    && !referencedColumnName.Equals(inferredDefinition.ReferencedColumns[idx]))
                {
                    // This indicates that the user has provided a custom relationship from a referencing column
                    // but we also inferred a different relationship from the same referencing column in the database.
                    // This is not supported because one column should not reference two different columns in order
                    // for nested insertions to make sense.
                    throw new DataApiBuilderException(
                        message: $"Inferred multiple relationships from same referencing column.",
                        statusCode: HttpStatusCode.Forbidden,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.NotSupported);
                }
                else
                {
                    referencingToReferencedColumns[referencingColumnName] = inferredDefinition.ReferencedColumns[idx];
                }
            }

            return new() {
                Pair = inferredDefinition.Pair,
                ReferencingColumns = referencingToReferencedColumns.Keys.ToList(),
                ReferencedColumns = referencingToReferencedColumns.Values.ToList() };
        }
    }
}
