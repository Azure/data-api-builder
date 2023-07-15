using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Sql;
using HotChocolate.Language;
using Microsoft.Extensions.Logging;

namespace TestApp
{
    internal class Program
    {
        static async Task Main()
        {

            try
            {
                string endpoint = "x6eps4xrq2xudenlfv6naeo3i4-vopwj32ar2jufcftggredq6s3a.msit-datamart.pbidedicated.windows.net";
                string authtoken = string.Empty;
                SqlConnectionStringBuilder builder = new()
                {
                    DataSource = "x6eps4xrq2xudenlfv6naeo3i4-vopwj32ar2jufcftggredq6s3a.msit-datamart.pbidedicated.windows.net",
                };

                SqlConnection conn = new(builder.ToString())
                {
                    AccessToken = authtoken
                };
                await conn.OpenAsync();

                string dbName = await GetDatabaseName(conn);

                SqlCommand command = new("SELECT * from model.authors", conn);

                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    DataTable dt = reader.GetSchemaTable();
                    foreach (DataRow row in dt.Rows)
                    {
                        Console.WriteLine(row.Field<string>("ColumnName"));
                    }

                    while (await reader.ReadAsync())
                    {
                        Console.WriteLine(string.Format("Row {0}", reader[0]));
                    }
                }

                // Generate schema
                string fileContent = await AttachDataSourceAsync(endpoint, dbName, authtoken);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// Get database name when using default database
        /// </summary>
        /// <param name="conn"></param>
        /// <returns></returns>
        private static async Task<string> GetDatabaseName(SqlConnection conn)
        {
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            string dbName = null;
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
            SqlCommand dbNameCommand = new("SELECT DB_NAME() AS dbname", conn);
            using (SqlDataReader reader = await dbNameCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    dbName = (string)reader[0];
                }
            }

#pragma warning disable CS8603 // Possible null reference return.
            return dbName;
#pragma warning restore CS8603 // Possible null reference return.
        }

        /// <summary>
        /// Attach a datasource async
        /// </summary>
        /// <param name="endpoint">artifact metadata</param>
        /// <param name="dbName">Database name</param>
        /// <param name="originalAadToken">original aad token</param>
        /// <param name="ct">cancellation token</param>
        /// <returns>Result of the task.</returns>
        private async static Task<string> AttachDataSourceAsync(string endpoint, string dbName, string originalAadToken)
    {
        try
        {
            SqlConnectionStringBuilder connectionString = new()
            {
                DataSource = endpoint,
                InitialCatalog = dbName,
            };
            using SqlConnection connection = new(connectionString.ToString());
            connection.AccessToken = originalAadToken;
            await connection.OpenAsync();

            // Get all user defined tables as entities we want to expose.
            List<string> tableNames = GetTableNames(connection);

            // TODOD
            DataSource dataSource = new(DatabaseType.MSSQL, connectionString.ToString(), new Dictionary<string, System.Text.Json.JsonElement>());

            Dictionary<string, Entity> entityKeyValuePairs = new();

            EntityAction[] entityActions = new EntityAction[]
            {
                new EntityAction(Action: EntityActionOperation.All, Fields: null, Policy: new EntityActionPolicy())                
            };
            EntityPermission[] entityPermissions = new EntityPermission[]
            {
                new EntityPermission("anonymous", entityActions),
            };

            // Create an entity object for each of them.
            foreach (string tableName in tableNames)
            {
#nullable enable
                string[]? keyFields = null;
#nullable disable

                if (tableName == "authors" || tableName == "books")
                {
                    keyFields = new[] { "id" };
                }
                else if (tableName == "books_authors")
                {
                    keyFields = new[] { "author_id", "book_id" };
                }

                EntitySource entitySource = new($"[model].{tableName}", EntitySourceType.View, null, KeyFields: keyFields);
                Entity entity = new(entitySource, new EntityGraphQLOptions(tableName, "none"), new EntityRestOptions(new SupportedHttpVerb[0]), entityPermissions, null, null);
                entityKeyValuePairs[tableName] = entity;
            }

            string schema = "https://github.com/Azure/data-api-builder/releases/download/v0.6.13/dab.draft.schema.json";

            // Necessary config classes needed for setup.
            RuntimeEntities entities = new(entityKeyValuePairs);
            RestRuntimeOptions restRuntimeOptions = new(false);
            GraphQLRuntimeOptions graphQLRuntimeOptions = new();
            HostOptions hostOptions = new(null, null);
            RuntimeOptions options = new(restRuntimeOptions, graphQLRuntimeOptions, hostOptions);
            RuntimeConfig config = new(schema, dataSource, options, entities);

            string runTimeConfigJson = config.ToJson();
            RuntimeConfigProvider runtimeConfigProvider = new(config);
            _ = runtimeConfigProvider.Initialize(runTimeConfigJson, schema: null, originalAadToken);
            ILoggerFactory loggerFactory = GetLoggerFactoryForLogLevel(LogLevel.Debug);

            // Setup MetadataProvider to get all database metadata.
            MsSqlDbExceptionParser parser = new(runtimeConfigProvider);
            ILogger<IQueryExecutor> qe = loggerFactory.CreateLogger<IQueryExecutor>();
            ILogger<ISqlMetadataProvider> mp = loggerFactory.CreateLogger<ISqlMetadataProvider>();
            IQueryExecutor executor = new MsSqlQueryExecutor(runtimeConfigProvider, parser, qe, null);
            IQueryBuilder builder = new MsSqlQueryBuilder();
            MsSqlMetadataProvider metadataProvider = new(runtimeConfigProvider, executor, builder, mp);
            await metadataProvider.InitializeAsync();
            AuthorizationResolver authorizationResolver = new AuthorizationResolver(runtimeConfigProvider, metadataProvider);

            // Generate the three documents that make up a graphql schema -> root with types, querynode, mutation node.
            (DocumentNode root, Dictionary<string, InputObjectTypeDefinitionNode> inputObjectTypes) = GenerateSqlGraphQLObjects(metadataProvider, authorizationResolver, entities);
            DocumentNode queryNode = QueryBuilder.Build(root, DatabaseType.MSSQL, entities, inputObjectTypes, authorizationResolver.EntityPermissionsMap, metadataProvider.EntityToDatabaseObject);
            DocumentNode mutationNode = MutationBuilder.Build(root, DatabaseType.MSSQL, entities, authorizationResolver.EntityPermissionsMap, metadataProvider.EntityToDatabaseObject);
            DocumentNode finalmerge = root.WithDefinitions(root.Definitions.Concat(queryNode.Definitions.Concat(mutationNode.Definitions)).ToList());

            // write schema document node to one lake.
            return finalmerge.ToString();
        }
        catch (Exception e)
        {
            Console.WriteLine($"SqlDatabaseArtifactStoreHandler: CreateAsync: caught exception {e.Message}");
            throw;
        }
    }

    private static List<string> GetTableNames(SqlConnection connection)
    {
        List<string> tables = new();

        DataTable schema = connection.GetSchema("Tables");

        foreach (DataRow row in schema.Rows)
        {
            string tableSchema = (string)row[1];
            string tableName = (string)row[2];
            string tableType = (string)row[3];

            // Tables in datamart are set as views and identified by tableSchema = model
            if (tableType == "BASE TABLE" || tableSchema == "model")
            {
                tables.Add(tableName);
            }
        }

        return tables;
    }

    /// <summary>
    /// Generates the ObjectTypeDefinitionNodes and InputObjectTypeDefinitionNodes as part of GraphQL Schema generation
    /// with the provided entities listed in the runtime configuration.
    /// </summary>
    /// <param name="provider">SqlMetaDataProvider</param>
    /// <param name="entities">Key/Value Collection {entityName -> Entity object}</param>
    /// <returns>Root GraphQLSchema DocumentNode and inputNodes to be processed by downstream schema generation helpers.</returns>
    /// <exception cref="DataApiBuilderException">Exception in case of internal server error.</exception>
    private static (DocumentNode, Dictionary<string, InputObjectTypeDefinitionNode>) GenerateSqlGraphQLObjects(ISqlMetadataProvider provider, IAuthorizationResolver authorizationResolver, RuntimeEntities entities)
    {
        Dictionary<string, ObjectTypeDefinitionNode> objectTypes = new();
        Dictionary<string, InputObjectTypeDefinitionNode> inputObjects = new();

        // First pass - build up the object and input types for all the entities
        foreach ((string entityName, Entity entity) in entities)
        {
            // Skip creating the GraphQL object for the current entity due to configuration
            // explicitly excluding the entity from the GraphQL endpoint.
            if (!entity.GraphQL.Enabled)
            {
                continue;
            }

            if (provider.GetEntityNamesAndDbObjects().TryGetValue(entityName, out Azure.DataApiBuilder.Config.DatabasePrimitives.DatabaseObject databaseObject))
            {
                // Collection of role names allowed to access entity, to be added to the authorize directive
                // of the objectTypeDefinitionNode.
                // TODO: Add Role based filters for fields and entities.
                IEnumerable<string> rolesAllowedForEntity = new List<string>();
                Dictionary<string, IEnumerable<string>> rolesAllowedForFields = new();

                SourceDefinition sourceDefinition = provider.GetSourceDefinition(entityName);
                bool isStoredProcedure = entity.Source.Type is EntitySourceType.StoredProcedure;
                foreach (string column in sourceDefinition.Columns.Keys)
                {
                   //rolesAllowedForFields.TryAdd(key: column, value: new string[] { "anonymous" });
                    EntityActionOperation operation = isStoredProcedure ? EntityActionOperation.Execute : EntityActionOperation.Read;
                    IEnumerable<string> roles = authorizationResolver.GetRolesForField(entityName, field: column, operation: operation);
                    if (!rolesAllowedForFields.TryAdd(key: column, value: roles))
                    {
                        throw new DataApiBuilderException(
                            message: "Column already processed for building ObjectTypeDefinition authorization definition.",
                            statusCode: System.Net.HttpStatusCode.InternalServerError,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization
                            );
                    }
                }
                    
                ObjectTypeDefinitionNode node = SchemaConverter.FromDatabaseObject(
                    entityName,
                    databaseObject,
                    entity,
                    entities,
                    rolesAllowedForEntity,
                    rolesAllowedForFields);

                if (databaseObject.SourceType != EntitySourceType.StoredProcedure)
                {
                    InputTypeBuilder.GenerateInputTypesForObjectType(node, inputObjects);
                }

                objectTypes.Add(entityName, node);
            }
            else
            {
                throw new DataApiBuilderException(
                    message: $"Database Object definition for {entityName} has not been inferred.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }
        }

        List<string> keys = new(objectTypes.Keys);
        foreach (string key in keys)
        {
            objectTypes[key] = QueryBuilder.AddQueryArgumentsForRelationships(objectTypes[key], inputObjects);
        }

        List<IDefinitionNode> nodes = new(objectTypes.Values);
        return (new DocumentNode(nodes.Concat(inputObjects.Values).ToImmutableList()), inputObjects);
    }

    /// <summary>
    /// Creates a LoggerFactory and add filter with the given LogLevel.
    /// </summary>
    /// <param name="logLevel">minimum log level.</param>
    private static ILoggerFactory GetLoggerFactoryForLogLevel(LogLevel logLevel)
    {
        return LoggerFactory
            .Create(builder =>
            {
                // Category defines the namespace we will log from,
                // including all sub-domains. ie: "Azure" includes
                // "Azure.DataApiBuilder.Service"
                builder.AddFilter(category: "Microsoft", logLevel);
                builder.AddFilter(category: "Azure", logLevel);
                builder.AddFilter(category: "Default", logLevel);
            });
    }
}
}
