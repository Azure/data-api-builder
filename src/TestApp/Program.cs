using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using HotChocolate.Language;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.Extensions.Logging;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Sql;
using System.Net;
using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;

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
                string fileContent = await AttachDataSourceAsync(endpoint, authtoken);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    

    /// <summary>
    /// Attach a datasource async
    /// </summary>
    /// <param name="endpoint">artifact metadata</param>
    /// <param name="originalAadToken">original aad token</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>Result of the task.</returns>
    private async static Task<string> AttachDataSourceAsync(string endpoint, string originalAadToken)
    {
        try
        {
            SqlConnectionStringBuilder connectionString = new()
            {
                DataSource = endpoint,
            };
            using SqlConnection connection = new(connectionString.ToString());
            connection.AccessToken = originalAadToken;
            await connection.OpenAsync();

            // Get all user defined tables as entities we want to expose.
            List<string> tableNames = GetTableNames(connection);

            // TODOD
            DataSource dataSource = new(DatabaseType.MSSQL, connectionString.ToString(), new Dictionary<string, System.Text.Json.JsonElement>());

            Dictionary<string, Entity> entityKeyValuePairs = new();

            // Create an entity object for each of them.
            foreach (string tableName in tableNames)
            {
                EntitySource entitySource = new($"[model].{tableName}", EntitySourceType.View, null, null);
                Entity entity = new(entitySource, new EntityGraphQLOptions("none", "none"), new EntityRestOptions(new SupportedHttpVerb[0]), new EntityPermission[0], null, null);
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

            // Generate the three documents that make up a graphql schema -> root with types, querynode, mutation node.
            (DocumentNode root, Dictionary<string, InputObjectTypeDefinitionNode> inputObjectTypes) = GenerateSqlGraphQLObjects(metadataProvider, entities);
            DocumentNode queryNode = QueryBuilder.Build(root, DatabaseType.MSSQL, entities, inputObjectTypes);
            DocumentNode mutationNode = MutationBuilder.Build(root, DatabaseType.MSSQL, entities);
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
    private static (DocumentNode, Dictionary<string, InputObjectTypeDefinitionNode>) GenerateSqlGraphQLObjects(ISqlMetadataProvider provider, RuntimeEntities entities)
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
