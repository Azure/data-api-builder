// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Language;
using Microsoft.Extensions.Logging;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;
using KeyNotFoundException = System.Collections.Generic.KeyNotFoundException;

[assembly: InternalsVisibleTo("Azure.DataApiBuilder.Service.Tests")]
namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// Reads schema information from the database to make it
    /// available for the GraphQL/REST services.
    /// </summary>
    public abstract class SqlMetadataProvider<ConnectionT, DataAdapterT, CommandT> : ISqlMetadataProvider
        where ConnectionT : DbConnection, new()
        where DataAdapterT : DbDataAdapter, new()
        where CommandT : DbCommand, new()
    {
        private ODataParser _oDataParser = new();

        private readonly DatabaseType _databaseType;

        // Represents the linking entities created by DAB to support multiple mutations for entities having an M:N relationship between them.
        protected Dictionary<string, Entity> _linkingEntities = new();

        protected readonly string _dataSourceName;

        // Represents the entities exposed in the runtime config.
        private IReadOnlyDictionary<string, Entity> Entities => new ReadOnlyDictionary<string, Entity>(_runtimeConfigProvider.GetConfig().Entities.Where(x => string.Equals(_runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(x.Key), _dataSourceName, StringComparison.OrdinalIgnoreCase)).ToDictionary(x => x.Key, x => x.Value));

        // Represents the autoentities exposed in the runtime config.
        private IReadOnlyDictionary<string, Autoentity> Autoentities => new ReadOnlyDictionary<string, Autoentity>(_runtimeConfigProvider.GetConfig().Autoentities.Where(x => string.Equals(_runtimeConfigProvider.GetConfig().GetDataSourceNameFromAutoentityName(x.Key), _dataSourceName, StringComparison.OrdinalIgnoreCase)).ToDictionary(x => x.Key, x => x.Value));

        // Dictionary containing mapping of graphQL stored procedure exposed query/mutation name
        // to their corresponding entity names defined in the config.
        public Dictionary<string, string> GraphQLStoredProcedureExposedNameToEntityNameMap { get; set; } = new();

        // Contains all the referencing and referenced columns for each pair
        // of referencing and referenced tables.
        public Dictionary<RelationShipPair, ForeignKeyDefinition>? PairToFkDefinition { get; set; }

        /// <summary>
        /// Maps {entityName, relationshipName} to the ForeignKeyDefinition defined for the relationship.
        /// The ForeignKeyDefinition denotes referencing/referenced fields and whether the referencing/referenced fields
        /// apply to the target or source entity as defined in the relationship in the config file.
        /// </summary>
        public Dictionary<EntityRelationshipKey, ForeignKeyDefinition> RelationshipToFkDefinition { get; set; } = new();

        protected IQueryExecutor QueryExecutor { get; }

        protected const int NUMBER_OF_RESTRICTIONS = 4;

        /// <summary>
        /// Column data types, as reported by the "Columns" schema collection, that the data
        /// provider cannot map to a CLR type. Reading such a column makes
        /// <see cref="DbDataAdapter.FillSchema(DataSet, SchemaType)"/> fail with
        /// "DataReader.GetFieldType(N) returned null", which takes down the whole database object
        /// even when the column itself is never exposed. They are therefore left out of the
        /// projection used for schema discovery.
        /// Empty by default: a provider only lists a type here when it genuinely cannot resolve it.
        /// </summary>
        protected virtual ImmutableHashSet<string> UnsupportedColumnDataTypes => ImmutableHashSet<string>.Empty;

        protected string ConnectionString { get; init; }

        protected IQueryBuilder SqlQueryBuilder { get; init; }

        protected DataSet EntitiesDataSet { get; init; }

        private RuntimeConfigProvider _runtimeConfigProvider;

        private RuntimeConfigValidator _runtimeConfigValidator;

        private Dictionary<string, Dictionary<string, string>> EntityBackingColumnsToExposedNames { get; } = new();

        private Dictionary<string, Dictionary<string, string>> EntityExposedNamesToBackingColumnNames { get; } = new();

        /// <summary>
        /// Caches the "Columns" schema collection per database object for the duration of metadata
        /// initialization, so schema discovery and column definition population share one catalog
        /// round trip instead of querying twice per object. Cleared once initialization completes.
        /// The key is compared with an ordinal comparer: under a case-sensitive collation
        /// <c>dbo.Foo</c> and <c>dbo.foo</c> are distinct objects, and aliasing them would serve one
        /// object's catalog rows for the other. Case-insensitive collations are unaffected, since
        /// they cannot hold both names at once.
        /// </summary>
        private readonly ConcurrentDictionary<string, DataTable> _columnsMetadataCache = new(StringComparer.Ordinal);

        /// <summary>
        /// Columns left out of the schema projection per database object, mapped to the data type
        /// that made them unreadable. Used to explain the omission when a configured primary key
        /// turns out to be one of them. Keyed by object with an ordinal comparer for the reason
        /// above; the inner column names stay case-insensitive, matching how this class resolves
        /// configured field names against the schema.
        /// </summary>
        private readonly ConcurrentDictionary<string, Dictionary<string, string>> _skippedColumnsByObject = new(StringComparer.Ordinal);

        /// <summary>
        /// Catalog facts per database object that the "Columns" schema collection does not report.
        /// Only populated for providers that declare <see cref="UnsupportedColumnDataTypes"/>, since
        /// only those replace "*" with an explicit projection and therefore need them. Keyed by
        /// object with an ordinal comparer, for the reason given on <see cref="_columnsMetadataCache"/>.
        /// </summary>
        private readonly ConcurrentDictionary<string, ObjectCatalogMetadata> _objectCatalogMetadataCache = new(StringComparer.Ordinal);

        /// <summary>
        /// The catalog facts an explicit schema projection needs and the "Columns" schema collection
        /// does not carry: which columns the database hides from "SELECT *", which columns identify
        /// a row, and which are identity columns. Together they replace what the data adapter
        /// reports under CommandBehavior.KeyInfo, which cannot be used once the projection is
        /// narrowed: the adapter appends key columns the projection left out as hidden reader
        /// columns, and an unsupported type among them reintroduces the very failure the narrowing
        /// avoids.
        /// </summary>
        protected sealed class ObjectCatalogMetadata
        {
            /// <summary>
            /// Columns the database omits from "SELECT *" — for SQL Server, the period columns of a
            /// temporal table declared GENERATED ALWAYS ... HIDDEN. Naming them in a projection
            /// would expose columns that are invisible today, so they are subtracted before the
            /// unsupported data types are.
            /// </summary>
            public HashSet<string> HiddenColumns { get; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Identity columns. Carried separately rather than through
            /// <see cref="DataColumn.AutoIncrement"/>, whose setter coerces a DataType it cannot
            /// increment to Int32: SQL Server allows identity on tinyint, numeric and decimal, and
            /// the coercion would report the wrong SystemType, which reaches parameter typing and
            /// the generated API schemas.
            /// </summary>
            public HashSet<string> IdentityColumns { get; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// The columns of the object's own primary key, in key order. Empty when the object has
            /// none.
            /// </summary>
            public List<string> PrimaryKeyColumns { get; } = new();

            /// <summary>
            /// Unique indexes eligible to identify a row when the object has no primary key, in
            /// index order, each holding its key columns in key order. Only indexes whose every key
            /// column is non-nullable qualify.
            /// Preserves what the data adapter does on the unnarrowed path: absent a primary key it
            /// reports such a unique key as <see cref="DataTable.PrimaryKey"/>, and dropping that
            /// would make the narrowed path demand source.key-fields for an object the other path
            /// resolves on its own.
            /// </summary>
            public List<List<string>> UniqueKeyCandidates { get; } = new();
        }

        /// <summary>
        /// Reads <see cref="ObjectCatalogMetadata"/> for a database object. Returns null by default:
        /// a provider only implements this when it declares <see cref="UnsupportedColumnDataTypes"/>.
        /// When it returns null the projection stays "*" and schema discovery behaves exactly as it
        /// did before.
        /// </summary>
        protected virtual Task<ObjectCatalogMetadata?> GetObjectCatalogMetadataAsync(
            string schemaName,
            string tableName)
        {
            return Task.FromResult<ObjectCatalogMetadata?>(null);
        }

        /// <summary>
        /// Returns the columns of a unique key the given projection exposes, in key order, for an
        /// object the catalog holds no index for — a view, whose key the data adapter resolved
        /// through the underlying table. Empty by default, and empty whenever the provider cannot
        /// answer, which leaves the missing-primary-key error to be reported as before.
        /// A key column the projection left out must never be reported as part of a key: a partial
        /// key silently matches more than one row on an update or a delete.
        /// </summary>
        /// <exception cref="DataApiBuilderException">
        /// An implementation may fail here instead of returning empty, when it can tell that no key
        /// of the underlying object is fully exposed and can name what is missing. That is a better
        /// error than the generic missing-primary-key one, which reads as something the user forgot
        /// to configure.
        /// </exception>
        protected virtual Task<List<string>> GetProjectionKeyFromResultSetAsync(string selectStatement)
        {
            return Task.FromResult(new List<string>());
        }

        /// <summary>
        /// Returns <see cref="ObjectCatalogMetadata"/> for a database object, reading the catalog
        /// once per object for the duration of metadata initialization.
        /// </summary>
        protected async Task<ObjectCatalogMetadata?> GetCachedObjectCatalogMetadataAsync(
            string schemaName,
            string tableName)
        {
            string cacheKey = GetObjectCacheKey(schemaName, tableName);

            if (_objectCatalogMetadataCache.TryGetValue(cacheKey, out ObjectCatalogMetadata? cachedMetadata))
            {
                return cachedMetadata;
            }

            ObjectCatalogMetadata? catalogMetadata = await GetObjectCatalogMetadataAsync(schemaName, tableName);

            if (catalogMetadata is not null)
            {
                _objectCatalogMetadataCache[cacheKey] = catalogMetadata;
            }

            return catalogMetadata;
        }

        protected IAbstractQueryManagerFactory QueryManagerFactory { get; init; }

        /// <summary>
        /// Maps an entity name to a DatabaseObject.
        /// </summary>
        public virtual Dictionary<string, DatabaseObject> EntityToDatabaseObject { get; set; } =
            new(StringComparer.InvariantCulture);

        protected readonly ILogger<ISqlMetadataProvider> _logger;

        public readonly bool _isValidateOnly;
        public List<Exception> SqlMetadataExceptions { get; private set; } = new();

        private void HandleOrRecordException(Exception e)
        {
            if (_isValidateOnly)
            {
                SqlMetadataExceptions.Add(e);
            }
            else
            {
                throw e;
            }
        }

        public SqlMetadataProvider(
            RuntimeConfigProvider runtimeConfigProvider,
            RuntimeConfigValidator runtimeConfigValidator,
            IAbstractQueryManagerFactory engineFactory,
            ILogger<ISqlMetadataProvider> logger,
            string dataSourceName,
            bool isValidateOnly = false)
        {
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();
            _runtimeConfigProvider = runtimeConfigProvider;
            _runtimeConfigValidator = runtimeConfigValidator;
            _dataSourceName = dataSourceName;
            _databaseType = runtimeConfig.GetDataSourceFromDataSourceName(dataSourceName).DatabaseType;
            _logger = logger;
            _isValidateOnly = isValidateOnly;
            LogRestPathsForEntities(runtimeConfig, Entities);

            ConnectionString = runtimeConfig.GetDataSourceFromDataSourceName(dataSourceName).ConnectionString;
            EntitiesDataSet = new();
            QueryManagerFactory = engineFactory;
            SqlQueryBuilder = QueryManagerFactory.GetQueryBuilder(_databaseType);
            QueryExecutor = QueryManagerFactory.GetQueryExecutor(_databaseType);
        }

        /// <inheritdoc />
        public ODataParser GetODataParser()
        {
            return _oDataParser;
        }

        /// <inheritdoc />
        public DatabaseType GetDatabaseType()
        {
            return _databaseType;
        }

        /// <summary>
        /// Obtains the underlying query builder.
        /// </summary>
        /// <returns></returns>
        public IQueryBuilder GetQueryBuilder()
        {
            return SqlQueryBuilder;
        }

        /// <inheritdoc />
        public virtual string GetSchemaName(string entityName)
        {
            if (!EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? databaseObject))
            {
                throw new DataApiBuilderException(message: $"Database object for entity '{entityName}' has not been inferred.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
            }

            return databaseObject!.SchemaName;
        }

        /// <summary>
        /// Gets the database name. This method is only relevant for MySql where the terms schema and database are used interchangeably.
        /// </summary>
        public virtual string GetDatabaseName() => string.Empty;

        /// <inheritdoc />
        public string GetDatabaseObjectName(string entityName)
        {
            if (!EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? databaseObject))
            {
                throw new DataApiBuilderException(message: $"Database object for entity '{entityName}' has not been inferred.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
            }

            return databaseObject!.Name;
        }

        /// <inheritdoc />
        public SourceDefinition GetSourceDefinition(string entityName)
        {
            if (!EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? databaseObject))
            {
                throw new DataApiBuilderException(message: $"Database object for entity '{entityName}' has not been inferred.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
            }

            return databaseObject.SourceDefinition;
        }

        /// <inheritdoc />
        public StoredProcedureDefinition GetStoredProcedureDefinition(string entityName)
        {
            if (!EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? databaseObject))
            {
                throw new DataApiBuilderException(message: $"Stored procedure definition for entity '{entityName}' has not been inferred.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
            }

            return ((DatabaseStoredProcedure)databaseObject).StoredProcedureDefinition;
        }

        /// <inheritdoc />
        public bool TryGetExposedColumnName(string entityName, string backingFieldName, [NotNullWhen(true)] out string? name)
        {
            if (!EntityBackingColumnsToExposedNames.TryGetValue(entityName, out Dictionary<string, string>? backingToExposed))
            {
                throw new KeyNotFoundException($"Initialization of metadata incomplete for entity: {entityName}");
            }

            if (backingToExposed.TryGetValue(backingFieldName, out name))
            {
                return true;
            }

            if (Entities.TryGetValue(entityName, out Entity? entityDefinition) && entityDefinition.Fields is not null)
            {
                // Find the field by backing name and use its Alias if present.
                FieldMetadata? matched = entityDefinition
                    .Fields
                    .FirstOrDefault(f => f.Name.Equals(backingFieldName, StringComparison.OrdinalIgnoreCase)
                                        && !string.IsNullOrEmpty(f.Alias));

                if (matched is not null)
                {
                    name = matched.Alias!;
                    return true;
                }
            }

            name = null;
            return false;
        }

        /// <inheritdoc />
        public bool TryGetBackingColumn(string entityName, string field, [NotNullWhen(true)] out string? name)
        {
            Dictionary<string, string>? exposedNamesToBackingColumnsMap;
            if (!EntityExposedNamesToBackingColumnNames.TryGetValue(entityName, out exposedNamesToBackingColumnsMap))
            {
                throw new KeyNotFoundException($"Initialization of metadata incomplete for entity: {entityName}");
            }

            if (exposedNamesToBackingColumnsMap.TryGetValue(field, out name))
            {
                return true;
            }

            if (Entities.TryGetValue(entityName, out Entity? entityDefinition) && entityDefinition.Fields is not null)
            {
                FieldMetadata? matchedField = entityDefinition.Fields.FirstOrDefault(f =>
                        f.Alias != null && f.Alias.Equals(field, StringComparison.OrdinalIgnoreCase));

                if (matchedField is not null)
                {
                    name = matchedField.Name;
                    return true;
                }
            }

            return exposedNamesToBackingColumnsMap.TryGetValue(field, out name);
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, DatabaseObject> GetEntityNamesAndDbObjects()
        {
            return EntityToDatabaseObject;
        }

        /// <inheritdoc />
        public string GetEntityName(string graphQLType)
        {
            if (Entities.ContainsKey(graphQLType))
            {
                return graphQLType;
            }

            foreach ((string entityName, Entity entity) in Entities)
            {
                if (entity.GraphQL.Singular == graphQLType)
                {
                    return entityName;
                }
            }

            throw new DataApiBuilderException(
                "GraphQL type doesn't match any entity name or singular type in the runtime config.",
                HttpStatusCode.BadRequest,
                DataApiBuilderException.SubStatusCodes.BadRequest);
        }

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();

            if (_isValidateOnly)
            {
                // Currently Validate mode only support single datasource,
                // so using the below validation we can check connection once instead of checking for each entity.
                // To enable to check for multiple data-sources just remove this validation and each entity will have its own connection check.
                try
                {
                    await ValidateDatabaseConnection();
                }
                catch (Exception e)
                {
                    HandleOrRecordException(e is DataApiBuilderException dabe ? dabe : new DataApiBuilderException(
                        message: DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE + $" {e.Message}",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization,
                        innerException: e));
                    return;
                }
            }

            if (GetDatabaseType() == DatabaseType.MSSQL)
            {
                await GenerateAutoentitiesIntoEntities(Autoentities);
            }

            // Running these entity validations only in development mode to ensure
            // fast startup of engine in production mode.
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            _runtimeConfigValidator.ValidateEntityAndAutoentityConfigurations(runtimeConfig);

            GenerateDatabaseObjectForEntities();

            try
            {
                await PopulateObjectDefinitionForEntities();
            }
            finally
            {
                ReleaseCatalogMetadataCaches();
            }

            GenerateExposedToBackingColumnMapsForEntities();

            // When IsLateConfigured is true we are in a hosted scenario and do not reveal primary key information.
            if (!_runtimeConfigProvider.IsLateConfigured)
            {
                LogPrimaryKeys();
            }

            GenerateRestPathToEntityMap();
            InitODataParser();

            if (_isValidateOnly)
            {
                RemoveGeneratedAutoentities();
            }

            timer.Stop();
            _logger.LogTrace($"Done inferring Sql database schema in {timer.ElapsedMilliseconds}ms.");
        }

        /// <inheritdoc />
        public void InitializeAsync(
            Dictionary<string, DatabaseObject> entityToDatabaseObject,
            Dictionary<string, string> graphQLStoredProcedureExposedNameToEntityNameMap)
        {
            EntityToDatabaseObject = entityToDatabaseObject ?? EntityToDatabaseObject;
            GraphQLStoredProcedureExposedNameToEntityNameMap = graphQLStoredProcedureExposedNameToEntityNameMap ?? GraphQLStoredProcedureExposedNameToEntityNameMap;
            GenerateExposedToBackingColumnMapsForEntities();
        }

        /// <inheritdoc/>
        public bool TryGetExposedFieldToBackingFieldMap(string entityName, [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? mappings)
        {
            Dictionary<string, string>? entityToColumnMappings;
            mappings = null;
            if (EntityExposedNamesToBackingColumnNames.TryGetValue(entityName, out entityToColumnMappings))
            {
                mappings = entityToColumnMappings;
                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public bool TryGetBackingFieldToExposedFieldMap(string entityName, [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? mappings)
        {
            Dictionary<string, string>? columntoEntityMappings;
            mappings = null;
            if (EntityBackingColumnsToExposedNames.TryGetValue(entityName, out columntoEntityMappings))
            {
                mappings = columntoEntityMappings;
                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public bool TryGetArrayElementSyntaxKind(string entityName, string fieldName, out SyntaxKind fieldKind)
        {
            if (TryGetBackingColumn(entityName, fieldName, out string? columnName))
            {
                SourceDefinition sourceDefinition = GetSourceDefinition(entityName);
                ColumnDefinition column = sourceDefinition.Columns[columnName];

                // If the column is an array type, we need to get the syntax kind from the element system type.
                if (column.IsArrayType && TypeHelper.TryGetSyntaxKindFromSystemType(column.ElementSystemType!, out fieldKind))
                {
                    return true;
                }
            }

            fieldKind = default;
            return false;
        }

        /// <summary>
        /// Log Primary key information. Function only called when not
        /// in a hosted scenario. Log relevant information about Primary keys
        /// including backing and exposed names, type, isNullable, and isAutoGenerated.
        /// </summary>
        private void LogPrimaryKeys()
        {
            ColumnDefinition column;
            foreach ((string entityName, Entity _) in Entities)
            {
                try
                {
                    SourceDefinition sourceDefinition = GetSourceDefinition(entityName);
                    _logger.LogDebug("Logging primary key information for entity: {entityName}.", entityName);
                    foreach (string pK in sourceDefinition.PrimaryKey)
                    {
                        column = sourceDefinition.Columns[pK];
                        if (TryGetExposedColumnName(entityName, pK, out string? exposedPKeyName))
                        {
                            _logger.LogDebug(
                                message: "Primary key column name: {pK}\n" +
                                "      Primary key mapped name: {exposedPKeyName}\n" +
                                "      Type: {column.SystemType.Name}\n" +
                                "      IsNullable: {column.IsNullable}\n" +
                                "      IsAutoGenerated: {column.IsAutoGenerated}",
                                pK,
                                exposedPKeyName,
                                column.SystemType.Name,
                                column.IsNullable,
                                column.IsAutoGenerated);
                        }
                    }
                }
                catch (Exception ex)
                {
                    HandleOrRecordException(new DataApiBuilderException(
                        message: $"Failed to log primary key information for entity: {entityName} due to: {ex.Message}",
                        innerException: ex,
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization));
                }
            }
        }

        /// <summary>
        /// Verify that the stored procedure exists in the database schema, then populate its database object parameters accordingly
        /// </summary>
        protected virtual async Task FillSchemaForStoredProcedureAsync(
            Entity procedureEntity,
            string entityName,
            string schemaName,
            string storedProcedureSourceName,
            StoredProcedureDefinition storedProcedureDefinition)
        {
            using ConnectionT conn = new();
            conn.ConnectionString = ConnectionString;
            DataTable procedureMetadata;
            string[] procedureRestrictions = new string[NUMBER_OF_RESTRICTIONS];

            try
            {
                await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName);
                await conn.OpenAsync();

                // To restrict the parameters for the current stored procedure, specify its name
                procedureRestrictions[0] = conn.Database;
                procedureRestrictions[1] = schemaName;
                procedureRestrictions[2] = storedProcedureSourceName;

                procedureMetadata = await conn.GetSchemaAsync(collectionName: "Procedures", restrictionValues: procedureRestrictions);
            }
            catch (Exception ex)
            {
                string message = $"Cannot obtain Schema for entity {entityName} " +
                            $"with underlying database object source: {schemaName}.{storedProcedureSourceName} " +
                            $"due to: {ex.Message}";

                throw new DataApiBuilderException(
                    message: message,
                    innerException: ex,
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            // Stored procedure does not exist in DB schema
            if (procedureMetadata.Rows.Count == 0)
            {
                throw new DataApiBuilderException(
                    message: $"No stored procedure definition found for the given database object {storedProcedureSourceName}",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            // Each row in the procedureParams DataTable corresponds to a single parameter
            DataTable parameterMetadata = await conn.GetSchemaAsync(collectionName: "ProcedureParameters", restrictionValues: procedureRestrictions);

            // For each row/parameter, add an entry to StoredProcedureDefinition.Parameters dictionary
            foreach (DataRow row in parameterMetadata.Rows)
            {
                // row["DATA_TYPE"] has value type string so a direct cast to System.Type is not supported.
                // See https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/sql-server-data-type-mappings
                string sqlType = (string)row["DATA_TYPE"];
                Type systemType = SqlToCLRType(sqlType);
                ParameterDefinition paramDefinition = new()
                {
                    SystemType = systemType,
                    DbType = TypeHelper.GetDbTypeFromSystemType(systemType)
                };

                // Add to parameters dictionary without the leading @ sign
                storedProcedureDefinition.Parameters.TryAdd(((string)row["PARAMETER_NAME"])[1..], paramDefinition);
            }

            // Loop through parameters specified in config, throw error if not found in schema
            // else set runtime config defined default values.
            // Note: we defer type checking of parameters specified in config until request time
            List<ParameterMetadata>? configParameters = procedureEntity.Source.Parameters;
            if (configParameters is not null)
            {
                foreach (ParameterMetadata paramMeta in configParameters)
                {
                    string configParamKey = paramMeta.Name;
                    if (!storedProcedureDefinition.Parameters.TryGetValue(configParamKey, out ParameterDefinition? parameterDefinition))
                    {
                        HandleOrRecordException(new DataApiBuilderException(
                            message: $"Could not find parameter \"{configParamKey}\" specified in config for procedure \"{schemaName}.{storedProcedureSourceName}\"",
                            statusCode: HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization));
                    }
                    else
                    {
                        // Map all metadata from config
                        parameterDefinition.Description = paramMeta.Description;
                        parameterDefinition.Required = paramMeta.Required;
                        parameterDefinition.Default = paramMeta.Default;
                        parameterDefinition.HasConfigDefault = paramMeta.Default is not null;
                        parameterDefinition.ConfigDefaultValue = paramMeta.Default?.ToString();
                    }
                }
            }

            // Generating exposed stored-procedure query/mutation name and adding to the dictionary mapping it to its entity name.
            GraphQLStoredProcedureExposedNameToEntityNameMap.TryAdd(GenerateStoredProcedureGraphQLFieldName(entityName, procedureEntity), entityName);
        }

        /// <summary>
        /// Takes a string version of a sql data type and returns its .NET common language runtime (CLR) counterpart
        /// </summary>
        public abstract Type SqlToCLRType(string sqlType);

        /// <summary>
        /// Updates a table's SourceDefinition object's metadata with whether any enabled insert/update DML triggers exist for the table.
        /// This method is only called for tables in MsSql.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="schemaName">Name of the schema in which the table is present.</param>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="sourceDefinition">Table definition to update.</param>
        public virtual Task PopulateTriggerMetadataForTable(string entityName, string schemaName, string tableName, SourceDefinition sourceDefinition)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Generates the map used to find a given entity based
        /// on the path that will be used for that entity.
        /// </summary>
        private void GenerateRestPathToEntityMap()
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            string graphQLGlobalPath = runtimeConfig.GraphQLPath;

            foreach ((string entityName, Entity entity) in Entities)
            {
                try
                {
                    string path = GetEntityPath(entity, entityName).TrimStart('/');
                    ValidateEntityAndGraphQLPathUniqueness(path, graphQLGlobalPath);

                    if (!string.IsNullOrEmpty(path))
                    {
                        // add the entity path name to the entity name mapping to the runtime config for multi-db resolution.
                        runtimeConfig.TryAddEntityPathNameToEntityName(path, entityName);
                    }
                }
                catch (Exception e)
                {
                    HandleOrRecordException(e);
                }
            }
        }

        /// <summary>
        /// Validate that an Entity's REST path does not conflict with the developer configured
        /// or the internal default GraphQL path (/graphql).
        /// </summary>
        /// <param name="path">Entity's calculated REST path.</param>
        /// <param name="graphQLGlobalPath">Developer configured GraphQL Path</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public void ValidateEntityAndGraphQLPathUniqueness(string path, string graphQLGlobalPath)
        {
            // Handle case when path does not have forward slash (/) prefix
            // by adding one if not present or ignoring an existing slash.
            // entityName -> /entityName
            // /entityName -> /entityName (no change)
            if (!string.IsNullOrWhiteSpace(path) && path[0] != '/')
            {
                path = '/' + path;
            }

            if (string.Equals(path, graphQLGlobalPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, GraphQLRuntimeOptions.DEFAULT_PATH, StringComparison.OrdinalIgnoreCase))
            {
                HandleOrRecordException(new DataApiBuilderException(
                    message: "Entity's REST path conflicts with GraphQL reserved paths.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
            }
        }

        /// <summary>
        /// Deserialize and return the entity's path.
        /// </summary>
        /// <param name="entity">Entity object to get the path of.</param>
        /// <param name="entityName">name of the entity</param>
        /// <returns>route for the given Entity.</returns>
        private static string GetEntityPath(Entity entity, string entityName)
        {
            // if entity.Rest is null or it's enabled without a custom path, return the entity name
            if (entity.Rest is null || (entity.Rest.Enabled && string.IsNullOrEmpty(entity.Rest.Path)))
            {
                return entityName;
            }

            // for false return empty string so we know not to add in caller
            if (!entity.Rest.Enabled)
            {
                return string.Empty;
            }

            // otherwise return the custom path
            return entity.Rest.Path!;
        }

        /// <summary>
        /// Returns the default schema name. Throws exception here since
        /// each derived class should override this method.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public virtual string GetDefaultSchemaName()
        {
            throw new NotSupportedException($"Cannot get default schema " +
                $"name for database type {_databaseType}");
        }

        /// <summary>
        /// Creates a Database object with the given schema and table names.
        /// </summary>
        protected virtual DatabaseTable GenerateDbTable(string schemaName, string tableName)
        {
            return new(schemaName, tableName);
        }

        /// <summary>
        /// Builds the dictionary of parameters and their values required for the
        /// foreign key query.
        /// </summary>
        /// <param name="schemaNames"></param>
        /// <param name="tableNames"></param>
        /// <returns>The dictionary populated with parameters.</returns>
        protected virtual Dictionary<string, DbConnectionParam>
            GetForeignKeyQueryParams(
                string[] schemaNames,
                string[] tableNames)
        {
            Dictionary<string, DbConnectionParam> parameters = new();
            string[] schemaNameParams =
                BaseSqlQueryBuilder.CreateParams(
                    kindOfParam: BaseSqlQueryBuilder.SCHEMA_NAME_PARAM,
                    schemaNames.Count());
            string[] tableNameParams =
                BaseSqlQueryBuilder.CreateParams(
                    kindOfParam: BaseSqlQueryBuilder.TABLE_NAME_PARAM,
                    tableNames.Count());

            for (int i = 0; i < schemaNames.Count(); ++i)
            {
                parameters.Add(schemaNameParams[i], new(schemaNames[i], DbType.String));
            }

            for (int i = 0; i < tableNames.Count(); ++i)
            {
                parameters.Add(tableNameParams[i], new(tableNames[i], DbType.String));
            }

            return parameters;
        }

        /// <summary>
        /// Create a DatabaseObject for all the exposed entities.
        /// </summary>
        private void GenerateDatabaseObjectForEntities()
        {
            Dictionary<string, DatabaseObject> sourceObjects = new();
            foreach ((string entityName, Entity entity) in Entities)
            {
                PopulateDatabaseObjectForEntity(entity, entityName, sourceObjects);
            }
        }

        /// <summary>
        /// Creates entities for each table that is found, based on the autoentity configuration.
        /// This method is only called for tables in MsSql.
        /// </summary>
        protected virtual Task GenerateAutoentitiesIntoEntities(IReadOnlyDictionary<string, Autoentity>? autoentities)
        {
            throw new NotSupportedException($"{GetType().Name} does not support autoentities yet.");
        }

        /// <summary>
        /// Removes the entities that were generated from the autoentities property.
        /// This should only be done when we only want to validate the entities.
        /// </summary>
        private void RemoveGeneratedAutoentities()
        {
            _runtimeConfigProvider.RemoveGeneratedAutoentitiesFromConfig();
        }

        /// <summary>
        /// Removes whitespace from the generated entity name and capitalizes the character
        /// immediately following each removed whitespace (camelCase join).
        /// For example, "Order Items" becomes "OrderItems" and "dbo_Order Items" becomes "dbo_OrderItems".
        /// </summary>
        /// <param name="name">The entity name to process.</param>
        /// <returns>The entity name with whitespace removed and following characters capitalized.</returns>
        protected static string RemoveWhitespaceAddCamelCase(string name)
        {
            StringBuilder result = new(name.Length);
            bool capitalizeNext = false;

            foreach (char character in name)
            {
                if (char.IsWhiteSpace(character))
                {
                    capitalizeNext = true;
                    continue;
                }

                result.Append(capitalizeNext ? char.ToUpperInvariant(character) : character);
                capitalizeNext = false;
            }

            return result.ToString();
        }

        protected void PopulateDatabaseObjectForEntity(
            Entity entity,
            string entityName,
            Dictionary<string, DatabaseObject> sourceObjects)
        {
            try
            {
                EntitySourceType sourceType = GetEntitySourceType(entityName, entity);
                if (!EntityToDatabaseObject.ContainsKey(entityName))
                {
                    if (entity.Source.Object is null)
                    {
                        throw new DataApiBuilderException(
                            message: $"The entity {entityName} does not have a valid source object.",
                            statusCode: HttpStatusCode.InternalServerError,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                    }

                    // Reuse the same Database object for multiple entities if they share the same source.
                    if (!sourceObjects.TryGetValue(entity.Source.Object, out DatabaseObject? sourceObject))
                    {
                        // parse source name into a tuple of (schemaName, databaseObjectName)
                        (string schemaName, string dbObjectName) = ParseSchemaAndDbTableName(entity.Source.Object)!;

                        // if specified as stored procedure in config,
                        // initialize DatabaseObject as DatabaseStoredProcedure,
                        // else with DatabaseTable (for tables) / DatabaseView (for views).

                        if (sourceType is EntitySourceType.StoredProcedure)
                        {
                            sourceObject = new DatabaseStoredProcedure(schemaName, dbObjectName)
                            {
                                SourceType = sourceType,
                                StoredProcedureDefinition = new()
                            };
                        }
                        else if (sourceType is EntitySourceType.Table)
                        {
                            sourceObject = new DatabaseTable()
                            {
                                SchemaName = schemaName,
                                Name = dbObjectName,
                                SourceType = sourceType,
                                TableDefinition = new()
                            };
                        }
                        else
                        {
                            sourceObject = new DatabaseView(schemaName, dbObjectName)
                            {
                                SchemaName = schemaName,
                                Name = dbObjectName,
                                SourceType = sourceType,
                                ViewDefinition = new()
                            };
                        }

                        sourceObjects.Add(entity.Source.Object, sourceObject);
                    }

                    EntityToDatabaseObject.Add(entityName, sourceObject);

                    if (entity.Relationships is not null && entity.Source.Type is EntitySourceType.Table)
                    {
                        ProcessRelationships(entityName, entity, (DatabaseTable)sourceObject, sourceObjects);
                    }
                }
            }
            catch (Exception e)
            {
                HandleOrRecordException(e);
            }
        }

        /// <summary>
        /// Get the EntitySourceType for the given entity or throw an exception if it is null.
        /// </summary>
        /// <param name="entityName">Name of the entity, used to provide info if an error is raised.</param>
        /// <param name="entity">Entity to get the source type from.</param>
        /// <returns>The non-nullable EntitySourceType.</returns>
        /// <exception cref="DataApiBuilderException">If the EntitySourceType is null raise an exception as it is required for a SQL entity.</exception>
        private static EntitySourceType GetEntitySourceType(string entityName, Entity entity)
        {
            return entity.Source.Type ??
                                throw new DataApiBuilderException(
                                    $"The entity {entityName} does not have a source type. A null source type is only valid if the database type is CosmosDB_NoSQL.",
                                    statusCode: HttpStatusCode.ServiceUnavailable,
                                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
        }

        /// <summary>
        /// Adds a foreign key definition for each of the nested entities
        /// specified in the relationships section of this entity
        /// to gather the referencing and referenced columns from the database at a later stage.
        /// Sets the referencing and referenced tables based on the kind of relationship.
        /// A linking object encountered is used as the referencing table
        /// for the foreign key definition.
        /// When no foreign key is defined in the database for the relationship,
        /// the relationship.source.fields and relationship.target.fields are mandatory.
        /// Initializing a FKDefinition indicates to find the foreign key
        /// between the referencing and referenced tables.
        /// </summary>
        /// <param name="entityName"></param>
        /// <param name="entity"></param>
        /// <param name="databaseTable"></param>
        /// <exception cref="InvalidOperationException"></exception>
        private void ProcessRelationships(
            string entityName,
            Entity entity,
            DatabaseTable databaseTable,
            Dictionary<string, DatabaseObject> sourceObjects)
        {
            SourceDefinition sourceDefinition = GetSourceDefinition(entityName);
            if (!sourceDefinition.SourceEntityRelationshipMap
                .TryGetValue(entityName, out RelationshipMetadata? relationshipData))
            {
                relationshipData = new();
                sourceDefinition.SourceEntityRelationshipMap.Add(entityName, relationshipData);
            }

            string targetSchemaName, targetDbTableName, linkingTableSchema, linkingTableName;
            foreach ((string relationshipName, EntityRelationship relationship) in entity.Relationships!)
            {
                string targetEntityName = relationship.TargetEntity;
                if (!Entities.TryGetValue(targetEntityName, out Entity? targetEntity))
                {
                    throw new InvalidOperationException($"Target Entity {targetEntityName} should be one of the exposed entities.");
                }

                if (targetEntity.Source.Object is null)
                {
                    throw new DataApiBuilderException(
                                message: $"Target entity {entityName} does not have a valid source object.",
                                statusCode: HttpStatusCode.InternalServerError,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                }

                (targetSchemaName, targetDbTableName) = ParseSchemaAndDbTableName(targetEntity.Source.Object)!;
                DatabaseTable targetDbTable = new(targetSchemaName, targetDbTableName);
                // If a linking object is specified,
                // give that higher preference and add two foreign keys for this targetEntity.
                if (relationship.LinkingObject is not null)
                {
                    (linkingTableSchema, linkingTableName) = ParseSchemaAndDbTableName(relationship.LinkingObject)!;
                    DatabaseTable linkingDbTable = new(linkingTableSchema, linkingTableName);
                    AddForeignKeyForTargetEntity(
                        sourceEntityName: entityName,
                        relationshipName: relationshipName,
                        targetEntityName: targetEntityName,
                        referencingDbTable: linkingDbTable,
                        referencedDbTable: databaseTable,
                        referencingColumns: relationship.LinkingSourceFields,
                        referencedColumns: relationship.SourceFields,
                        referencingEntityRole: RelationshipRole.Linking,
                        referencedEntityRole: RelationshipRole.Source,
                        relationshipData: relationshipData);

                    AddForeignKeyForTargetEntity(
                        sourceEntityName: entityName,
                        relationshipName: relationshipName,
                        targetEntityName: targetEntityName,
                        referencingDbTable: linkingDbTable,
                        referencedDbTable: targetDbTable,
                        referencingColumns: relationship.LinkingTargetFields,
                        referencedColumns: relationship.TargetFields,
                        referencingEntityRole: RelationshipRole.Linking,
                        referencedEntityRole: RelationshipRole.Target,
                        relationshipData: relationshipData);

                    RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();

                    // Populating metadata for linking object is only required when multiple create operation is enabled and those database types that support multiple create operation.
                    if (runtimeConfig.IsMultipleCreateOperationEnabled())
                    {
                        // When a linking object is encountered for a database table, we will create a linking entity for the object.
                        // Subsequently, we will also populate the Database object for the linking entity. This is used to infer
                        // metadata about linking object needed to create GQL schema for multiple insertions.
                        if (entity.Source.Type is EntitySourceType.Table)
                        {
                            PopulateMetadataForLinkingObject(
                                entityName: entityName,
                                targetEntityName: targetEntityName,
                                linkingObject: relationship.LinkingObject,
                                sourceObjects: sourceObjects);
                        }
                    }
                }
                else if (relationship.Cardinality == Cardinality.One)
                {
                    // Example: books(Many) - publisher(One)
                    // where books.publisher_id is referencing publisher.id
                    // For Many-One OR One-One Relationships, DAB optimistically
                    // creates two ForeignKeyDefinitions to represent the relationship:
                    //
                    // #1
                    // Referencing Entity | Referenced Entity
                    // -------------------|-------------------
                    // Source Entity      | Target Entity
                    //
                    // #2
                    // Referencing Entity | Referenced Entity
                    // -------------------|-------------------
                    // Target Entity      | Source Entity
                    //
                    // One of the created ForeignKeyDefinitions correctly matches foreign key
                    // metadata in the database and DAB will later identify the correct
                    // ForeignKeyDefinition object when processing database schema metadata.
                    //
                    // When the runtime config doesn't specify how to relate these entities
                    // (via source/target fields), DAB expects to identity that one of
                    // the ForeignKeyDefinition objects will match foreign key metadata in the database.
                    // Create ForeignKeyDefinition #1
                    AddForeignKeyForTargetEntity(
                        sourceEntityName: entityName,
                        relationshipName: relationshipName,
                        targetEntityName,
                        referencingDbTable: databaseTable,
                        referencedDbTable: targetDbTable,
                        referencingColumns: relationship.SourceFields,
                        referencedColumns: relationship.TargetFields,
                        referencingEntityRole: RelationshipRole.Source,
                        referencedEntityRole: RelationshipRole.Target,
                        relationshipData);

                    // Create ForeignKeyDefinition #2
                    // when target and source entities differ (NOT self-referencing)
                    // because one ForeignKeyDefintion is sufficient to represent a self-joining relationship.
                    if (targetEntityName != entityName)
                    {
                        AddForeignKeyForTargetEntity(
                            sourceEntityName: entityName,
                            relationshipName: relationshipName,
                            targetEntityName,
                            referencingDbTable: targetDbTable,
                            referencedDbTable: databaseTable,
                            referencingColumns: relationship.TargetFields,
                            referencedColumns: relationship.SourceFields,
                            referencingEntityRole: RelationshipRole.Target,
                            referencedEntityRole: RelationshipRole.Source,
                            relationshipData);
                    }
                }
                else if (relationship.Cardinality is Cardinality.Many)
                {
                    // Example: publisher(One)-books(Many)
                    // where publisher.id is referenced by books.publisher_id
                    // For Many-Many relationships, DAB creates one
                    // ForeignKeyDefinition to represent the relationship:
                    //
                    // #1
                    // Referencing Entity | Referenced Entity
                    // -------------------|-------------------
                    // Target Entity      | Source Entity
                    AddForeignKeyForTargetEntity(
                        sourceEntityName: entityName,
                        relationshipName: relationshipName,
                        targetEntityName,
                        referencingDbTable: targetDbTable,
                        referencedDbTable: databaseTable,
                        referencingColumns: relationship.TargetFields,
                        referencedColumns: relationship.SourceFields,
                        referencingEntityRole: RelationshipRole.Target,
                        referencedEntityRole: RelationshipRole.Source,
                        relationshipData);
                }
            }
        }

        /// <summary>
        /// Helper method to create a linking entity and a database object for the given linking object (which relates the source and target with an M:N relationship).
        /// The created linking entity and its corresponding database object definition is later used during GraphQL schema generation
        /// to enable multiple mutations.
        /// </summary>
        /// <param name="entityName">Source entity name.</param>
        /// <param name="targetEntityName">Target entity name.</param>
        /// <param name="linkingObject">Linking object</param>
        /// <param name="sourceObjects">Dictionary storing a collection of database objects which have been created.</param>
        protected virtual void PopulateMetadataForLinkingObject(
            string entityName,
            string targetEntityName,
            string linkingObject,
            Dictionary<string, DatabaseObject> sourceObjects)
        {
            return;
        }

        /// <summary>
        /// Helper method that logs the REST paths for all entities and shows if the REST calls are enabled/disabled for any entity.
        /// </summary>
        /// <param name="runtimeConfig"></param>
        /// <param name="entities"></param>
        protected void LogRestPathsForEntities(RuntimeConfig runtimeConfig, IReadOnlyDictionary<string, Entity> entities)
        {
            if (!_isValidateOnly)
            {
                foreach ((string entityName, Entity entityMetatdata) in entities)
                {
                    if (runtimeConfig.IsRestEnabled)
                    {
                        string restPath = entityMetatdata.Rest?.Path ?? entityName;
                        _logger.LogInformation("[{entity}] REST path: {globalRestPath}/{entityRestPath}", entityName, runtimeConfig.RestPath, restPath);
                    }
                    else
                    {
                        _logger.LogInformation(message: "REST calls are disabled for the entity: {entity}", entityName);
                    }
                }
            }
        }

        /// <summary>
        /// Adds a new foreign key definition for the target entity in the relationship metadata.
        /// The last argument "relationshipData" is modified (hydrated with the new foreign key definition)
        /// as a side effect of executing this function.
        /// </summary>
        private static void AddForeignKeyForTargetEntity(
            string sourceEntityName,
            string relationshipName,
            string targetEntityName,
            DatabaseTable referencingDbTable,
            DatabaseTable referencedDbTable,
            string[]? referencingColumns,
            string[]? referencedColumns,
            RelationshipRole referencingEntityRole,
            RelationshipRole referencedEntityRole,
            RelationshipMetadata relationshipData)
        {
            ForeignKeyDefinition foreignKeyDefinition = new()
            {
                SourceEntityName = sourceEntityName,
                RelationshipName = relationshipName,
                ReferencingEntityRole = referencingEntityRole,
                ReferencedEntityRole = referencedEntityRole,
                Pair = new()
                {
                    RelationshipName = relationshipName,
                    ReferencingDbTable = referencingDbTable,
                    ReferencedDbTable = referencedDbTable
                }
            };

            if (referencingColumns is not null)
            {
                foreignKeyDefinition.ReferencingColumns.AddRange(referencingColumns);
            }

            if (referencedColumns is not null)
            {
                foreignKeyDefinition.ReferencedColumns.AddRange(referencedColumns);
            }

            if (relationshipData
                .TargetEntityToFkDefinitionMap.TryGetValue(targetEntityName, out List<ForeignKeyDefinition>? foreignKeys))
            {
                foreignKeys.Add(foreignKeyDefinition);
            }
            else
            {
                relationshipData.TargetEntityToFkDefinitionMap
                    .Add(targetEntityName,
                        new List<ForeignKeyDefinition>() { foreignKeyDefinition });
            }
        }

        /// <summary>
        /// Helper function will parse the schema and database object name
        /// from the provided source string and sort out if a default schema
        /// should be used.
        /// </summary>
        /// <param name="source">source string to parse</param>
        /// <returns>The appropriate schema and db object name as a tuple of strings.</returns>
        /// <exception cref="DataApiBuilderException"></exception>
        public (string, string) ParseSchemaAndDbTableName(string source)
        {
            (string? schemaName, string dbTableName) = EntitySourceNamesParser.ParseSchemaAndTable(source)!;

            // if schemaName is empty we check if the DB type is postgresql
            // and if the schema name was included in the connection string
            // as a value associated with the keyword 'SearchPath'.
            // if the DB type is not postgresql or if the connection string
            // does not include the schema name, we use the default schema name.
            // if schemaName is not empty we must check if Database Type is MySql
            // and in this case we throw an exception since there should be no
            // schema name in this case.
            if (string.IsNullOrEmpty(schemaName))
            {
                // if DatabaseType is not postgresql will short circuit and use default
                if (_databaseType is not DatabaseType.PostgreSQL ||
                    !PostgreSqlMetadataProvider.TryGetSchemaFromConnectionString(
                        connectionString: ConnectionString,
                        out schemaName))
                {
                    schemaName = GetDefaultSchemaName();
                }
            }
            else if (_databaseType is DatabaseType.MySQL)
            {
                throw new DataApiBuilderException(message: $"Invalid database object name: \"{schemaName}.{dbTableName}\"",
                                               statusCode: HttpStatusCode.ServiceUnavailable,
                                               subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            return (schemaName, dbTableName);
        }

        /// <inheritdoc />
        public List<string> GetSchemaGraphQLFieldNamesForEntityName(string entityName)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public string? GetSchemaGraphQLFieldTypeFromFieldName(string graphQLType, string fieldName)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public FieldDefinitionNode? GetSchemaGraphQLFieldFromFieldName(string graphQLType, string fieldName)
            => throw new NotImplementedException();

        public IReadOnlyDictionary<string, Entity> GetLinkingEntities()
        {
            return _linkingEntities;
        }

        /// <summary>
        /// Enrich the entities in the runtime config with the
        /// object definition information needed by the runtime to serve requests.
        /// Populates table definition for entities specified as tables or views
        /// Populates procedure definition for entities specified as stored procedures
        /// </summary>
        private async Task PopulateObjectDefinitionForEntities()
        {
            foreach ((string entityName, Entity entity) in Entities)
            {
                await PopulateObjectDefinitionForEntity(entityName, entity);
            }

            foreach ((string entityName, Entity entity) in _linkingEntities)
            {
                await PopulateObjectDefinitionForEntity(entityName, entity);
            }

            try
            {
                await PopulateForeignKeyDefinitionAsync();
            }
            catch (Exception e)
            {
                HandleOrRecordException(e);
            }
        }

        private async Task PopulateObjectDefinitionForEntity(string entityName, Entity entity)
        {
            try
            {
                EntitySourceType entitySourceType = GetEntitySourceType(entityName, entity);
                if (entitySourceType is EntitySourceType.StoredProcedure)
                {
                    await FillSchemaForStoredProcedureAsync(
                        entity,
                        entityName,
                        GetSchemaName(entityName),
                        GetDatabaseObjectName(entityName),
                        GetStoredProcedureDefinition(entityName));

                    if (GetDatabaseType() == DatabaseType.MSSQL || GetDatabaseType() == DatabaseType.DWSQL)
                    {
                        await PopulateResultSetDefinitionsForStoredProcedureAsync(
                            GetSchemaName(entityName),
                            GetDatabaseObjectName(entityName),
                            GetStoredProcedureDefinition(entityName));
                    }
                }
                else if (entitySourceType is EntitySourceType.Table)
                {
                    List<string> pkFields = new();

                    // Resolve PKs from fields first
                    if (entity.Fields is not null && entity.Fields.Any())
                    {
                        pkFields = entity.Fields
                            .Where(f => f.PrimaryKey)
                            .Select(f => f.Name)
                            .ToList();
                    }

                    // Fallback to key-fields from config
                    if (pkFields.Count == 0 && entity.Source.KeyFields is not null)
                    {
                        pkFields = entity.Source.KeyFields.ToList();
                    }

                    // If still empty, fallback to DB schema PKs
                    if (pkFields.Count == 0)
                    {
                        DataTable dataTable = await GetTableWithSchemaFromDataSetAsync(
                            entityName,
                            GetSchemaName(entityName),
                            GetDatabaseObjectName(entityName));

                        pkFields = dataTable.PrimaryKey.Select(pk => pk.ColumnName).ToList();
                    }

                    // Final safeguard
                    pkFields ??= new List<string>();

                    await PopulateSourceDefinitionAsync(
                        entityName,
                        GetSchemaName(entityName),
                        GetDatabaseObjectName(entityName),
                        GetSourceDefinition(entityName),
                        pkFields);
                }
                else
                {
                    List<string> pkFields = new();

                    // Resolve PKs from fields first
                    if (entity.Fields is not null && entity.Fields.Any())
                    {
                        pkFields = entity.Fields
                            .Where(f => f.PrimaryKey)
                            .Select(f => f.Name)
                            .ToList();
                    }

                    // Fallback to key-fields from config
                    if (pkFields.Count == 0 && entity.Source.KeyFields is not null)
                    {
                        pkFields = entity.Source.KeyFields.ToList();
                    }

                    // If still empty, fallback to DB schema PKs
                    if (pkFields.Count == 0)
                    {
                        DataTable dataTable = await GetTableWithSchemaFromDataSetAsync(
                            entityName,
                            GetSchemaName(entityName),
                            GetDatabaseObjectName(entityName));

                        pkFields = dataTable.PrimaryKey.Select(pk => pk.ColumnName).ToList();
                    }

                    ViewDefinition viewDefinition = (ViewDefinition)GetSourceDefinition(entityName);
                    await PopulateSourceDefinitionAsync(
                        entityName,
                        GetSchemaName(entityName),
                        GetDatabaseObjectName(entityName),
                        viewDefinition,
                        pkFields);
                }
            }
            catch (Exception e)
            {
                HandleOrRecordException(e);
            }
        }

        /// <summary>
        /// Queries DB to get the result fields name and type to
        /// populate the result set definition for entities specified as stored procedures
        /// </summary>
        private async Task PopulateResultSetDefinitionsForStoredProcedureAsync(
            string schemaName,
            string storedProcedureName,
            SourceDefinition sourceDefinition)
        {
            StoredProcedureDefinition storedProcedureDefinition = (StoredProcedureDefinition)sourceDefinition;
            string dbStoredProcedureName = $"{schemaName}.{storedProcedureName}";
            // Generate query to get result set details
            // of the stored procedure.
            string queryForResultSetDetails = SqlQueryBuilder.BuildStoredProcedureResultDetailsQuery(
                dbStoredProcedureName);

            // Execute the query to get columns' details.
            JsonArray? resultArray = await QueryExecutor.ExecuteQueryAsync(
                sqltext: queryForResultSetDetails,
                parameters: null!,
                dataReaderHandler: QueryExecutor.GetJsonArrayAsync,
                dataSourceName: _dataSourceName);

            using JsonDocument sqlResult = JsonDocument.Parse(resultArray!.ToJsonString());

            // Iterate through each row returned by the query which corresponds to
            // one row in the result set.
            foreach (JsonElement element in sqlResult.RootElement.EnumerateArray())
            {
                string resultFieldName = element.GetProperty(BaseSqlQueryBuilder.STOREDPROC_COLUMN_NAME).ToString();
                Type resultFieldType = SqlToCLRType(element.GetProperty(BaseSqlQueryBuilder.STOREDPROC_COLUMN_SYSTEMTYPENAME).ToString());
                bool isResultFieldNullable = element.GetProperty(BaseSqlQueryBuilder.STOREDPROC_COLUMN_ISNULLABLE).GetBoolean();

                // Validate that the stored procedure returns columns with proper names
                // This commonly occurs when using aggregate functions or expressions without aliases
                if (string.IsNullOrWhiteSpace(resultFieldName))
                {
                    throw new DataApiBuilderException(
                        message: $"The stored procedure '{dbStoredProcedureName}' returns a column without a name. " +
                                "This typically happens when using aggregate functions (like MAX, MIN, COUNT) or expressions " +
                                "without providing an alias. Please add column aliases to your SELECT statement. " +
                                "For example: 'SELECT MAX(id) AS MaxId' instead of 'SELECT MAX(id)'.",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }

                // Store the dictionary containing result set field with its type as Columns
                storedProcedureDefinition.Columns.TryAdd(resultFieldName, new(resultFieldType) { IsNullable = isResultFieldNullable });
            }
        }

        /// <summary>
        /// Generate the mappings of exposed names to
        /// backing columns, and of backing columns to
        /// exposed names. Used to generate EDM Model using
        /// the exposed names, and to translate between
        /// exposed name and backing column (or the reverse)
        /// when needed while processing the request.
        /// For now, only do this for tables/views as Stored Procedures do not have a SourceDefinition
        /// In the future, mappings for SPs could be used for parameter renaming.
        /// We also handle logging the primary key information here since this is when we first have
        /// the exposed names suitable for logging.
        /// As part of building the database query, when generating the output columns,
        /// EntityBackingColumnsToExposedNames is looked at.
        /// But, when linking entity details are not populated, the flow will fail
        /// when generating the output columns.
        /// Hence, mappings of exposed names to backing columns
        /// and of backing columns to exposed names
        /// are generated for linking entities as well.
        /// </summary>
        private void GenerateExposedToBackingColumnMapsForEntities()
        {
            foreach ((string entityName, Entity _) in Entities)
            {
                GenerateExposedToBackingColumnMapUtil(entityName);
            }

            foreach ((string entityName, Entity _) in _linkingEntities)
            {
                GenerateExposedToBackingColumnMapUtil(entityName);
            }
        }

        /// <summary>
        /// Helper method to generate the mappings of exposed names to
        /// backing columns, and of backing columns to exposed names.
        /// </summary>
        /// <param name="entityName">Name of the entity</param>
        private void GenerateExposedToBackingColumnMapUtil(string entityName)
        {
            try
            {
                // Build case-insensitive maps per entity.
                Dictionary<string, string> backToExposed = new(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, string> exposedToBack = new(StringComparer.OrdinalIgnoreCase);

                // Pull definitions.
                Entities.TryGetValue(entityName, out Entity? entity);
                SourceDefinition sourceDefinition = GetSourceDefinition(entityName);

                // 1) Prefer new-style fields (backing = f.Name, exposed = f.Alias ?? f.Name)
                if (entity?.Fields is not null)
                {
                    foreach (FieldMetadata f in entity.Fields)
                    {
                        string backing = f.Name;
                        string exposed = string.IsNullOrWhiteSpace(f.Alias) ? backing : f.Alias!;
                        backToExposed[backing] = exposed;
                        exposedToBack[exposed] = backing;
                    }
                }

                // 2) Overlay legacy mappings (backing -> alias) only where we don't already have an alias from fields.
                if (entity?.Mappings is not null)
                {
                    foreach (KeyValuePair<string, string> kvp in entity.Mappings)
                    {
                        string backing = kvp.Key;
                        string exposed = kvp.Value;

                        // If fields already provided an alias for this backing column, keep fields precedence.
                        if (!backToExposed.ContainsKey(backing))
                        {
                            backToExposed[backing] = exposed;
                        }

                        // Always ensure reverse map is coherent (fields still take precedence if the same exposed already exists).
                        if (!exposedToBack.ContainsKey(exposed))
                        {
                            exposedToBack[exposed] = backing;
                        }
                    }
                }

                // 3) Ensure all physical columns are mapped (identity default).
                foreach (string backing in sourceDefinition.Columns.Keys)
                {
                    if (!backToExposed.ContainsKey(backing))
                    {
                        backToExposed[backing] = backing;
                    }

                    string exposed = backToExposed[backing];
                    if (!exposedToBack.ContainsKey(exposed))
                    {
                        exposedToBack[exposed] = backing;
                    }
                }

                // 4) Store maps for runtime
                EntityBackingColumnsToExposedNames[entityName] = backToExposed;
                EntityExposedNamesToBackingColumnNames[entityName] = exposedToBack;
            }
            catch (Exception e)
            {
                HandleOrRecordException(e);
            }
        }

        /// <summary>
        /// Initialize OData parser by building OData model.
        /// The parser will be used for parsing filter clause and order by clause.
        /// </summary>
        private void InitODataParser()
        {
            _oDataParser.BuildModel(this);
        }

        /// <summary>
        /// Fills the table definition with information of all columns and
        /// primary keys.
        /// </summary>
        /// <param name="schemaName">Name of the schema.</param>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="sourceDefinition">Table definition to fill.</param>
        /// <param name="entityName">EntityName included to pass on for error messaging.</param>
        private async Task PopulateSourceDefinitionAsync(
            string entityName,
            string schemaName,
            string tableName,
            SourceDefinition sourceDefinition,
            List<string> pkFields)
        {
            sourceDefinition.PrimaryKey = [.. pkFields];

            if (sourceDefinition.PrimaryKey.Count == 0)
            {
                // When the object's own primary key is unreadable, say so. The message below reads
                // as a configuration mistake, and no configuration can express that key.
                RejectUnreadablePrimaryKey(schemaName, tableName);

                throw new DataApiBuilderException(
                       message: $"Primary key not configured on the given database object {tableName}",
                       statusCode: HttpStatusCode.ServiceUnavailable,
                       subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            Entities.TryGetValue(entityName, out Entity? entity);
            if (GetDatabaseType() is DatabaseType.MSSQL && entity is not null && entity.Source.Type is EntitySourceType.Table)
            {
                await PopulateTriggerMetadataForTable(entityName, schemaName, tableName, sourceDefinition);
            }

            DataTable dataTable = await GetTableWithSchemaFromDataSetAsync(entityName, schemaName, tableName);
            using DataTableReader reader = new(dataTable);
            DataTable schemaTable = reader.GetSchemaTable();
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            foreach (DataRow columnInfoFromAdapter in schemaTable.Rows)
            {
                string columnName = columnInfoFromAdapter["ColumnName"].ToString()!;

                if (runtimeConfig.IsGraphQLEnabled
                    && entity is not null
                    && IsGraphQLReservedName(entity, columnName, graphQLEnabledGlobally: runtimeConfig.IsGraphQLEnabled))
                {
                    throw new DataApiBuilderException(
                       message: $"The column '{columnName}' from '{entityName}' violates GraphQL name restrictions.",
                       statusCode: HttpStatusCode.ServiceUnavailable,
                       subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }

                Type systemType = (Type)columnInfoFromAdapter["DataType"];

                // Detect array types: concrete array types (e.g., int[]) have IsArray=true,
                // while Npgsql reports abstract System.Array for PostgreSQL array columns.
                // byte[] is excluded since it maps to the bytea/ByteArray scalar type.
                bool isArrayType = (systemType.IsArray && systemType != typeof(byte[])) || systemType == typeof(Array);

                ColumnDefinition column = new()
                {
                    IsNullable = (bool)columnInfoFromAdapter["AllowDBNull"],
                    IsAutoGenerated = (bool)columnInfoFromAdapter["IsAutoIncrement"],
                    SystemType = systemType,
                    IsArrayType = isArrayType,
                    ElementSystemType = isArrayType && systemType.IsArray ? systemType.GetElementType() : null,
                    // An auto-increment column is also considered as a read-only column. For other types of read-only columns,
                    // the flag is populated later via PopulateColumnDefinitionsWithReadOnlyFlag() method.
                    // Array columns are also treated as read-only until write support for array types is implemented.
                    IsReadOnly = (bool)columnInfoFromAdapter["IsAutoIncrement"] || isArrayType
                };

                // Tests may try to add the same column simultaneously
                // hence we use TryAdd here.
                // If the addition fails, it is assumed the column definition
                // has already been added and need not error out.
                sourceDefinition.Columns.TryAdd(columnName, column);
            }

            ApplyIdentityColumnsFromCatalog(schemaName, tableName, sourceDefinition);

            RejectPrimaryKeyOnUnsupportedColumn(schemaName, tableName, sourceDefinition);

            RejectConfiguredReferencesToSkippedColumns(entityName, entity, schemaName, tableName);

            DataTable columnsInTable = await GetCachedColumnsAsync(schemaName, tableName);

            PopulateColumnDefinitionWithHasDefaultAndDbType(
                sourceDefinition,
                columnsInTable);

            if (entity is not null && entity.Source.Type is EntitySourceType.Table)
            {
                // For MySql, database name is equivalent to schema name.
                string schemaOrDatabaseName = GetDatabaseType() is DatabaseType.MySQL ? GetDatabaseName() : schemaName;
                await PopulateColumnDefinitionsWithReadOnlyFlag(tableName, schemaOrDatabaseName, sourceDefinition);
            }
        }

        /// <summary>
        /// Helper method to populate the column definitions of each column in a table with the info about
        /// whether the column can be updated or not.
        /// </summary>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="schemaOrDatabaseName">Name of the schema (for MsSql/PgSql)/database (for MySql) of the table.</param>
        /// <param name="sourceDefinition">Table definition.</param>
        private async Task PopulateColumnDefinitionsWithReadOnlyFlag(string tableName, string schemaOrDatabaseName, SourceDefinition sourceDefinition)
        {
            string schemaOrDatabaseParamName = $"{BaseQueryStructure.PARAM_NAME_PREFIX}param0";
            string quotedTableName = SqlQueryBuilder.QuoteTableNameAsDBConnectionParam(tableName);
            string tableParamName = $"{BaseQueryStructure.PARAM_NAME_PREFIX}param1";
            string queryToGetReadOnlyColumns = SqlQueryBuilder.BuildQueryToGetReadOnlyColumns(schemaOrDatabaseParamName, tableParamName);
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { schemaOrDatabaseParamName, new(schemaOrDatabaseName, DbType.String) },
                { tableParamName, new(quotedTableName, DbType.String) }
            };

            List<string>? readOnlyFields = await QueryExecutor.ExecuteQueryAsync(
                sqltext: queryToGetReadOnlyColumns,
                parameters: parameters,
                dataReaderHandler: SummarizeReadOnlyFieldsMetadata,
                dataSourceName: _dataSourceName);

            if (readOnlyFields is not null && readOnlyFields.Count > 0)
            {
                foreach (string readOnlyField in readOnlyFields)
                {
                    if (sourceDefinition.Columns.TryGetValue(readOnlyField, out ColumnDefinition? columnDefinition))
                    {
                        // Mark the column as read-only.
                        columnDefinition.IsReadOnly = true;
                    }
                }
            }
        }

        /// <summary>
        /// Determine whether the provided field of a GraphQL enabled entity meets GraphQL reserved name requirements.
        /// Criteria:
        /// - Is GraphQL enabled globally
        /// - Is GraphQL implicitly enabled e.g. entity.GraphQL is null, or explicitly enabled e.g. entity.GraphQL is true).
        /// - If field has a mapped value (alias), then use the mapped value to evaluate name violation.
        /// - If field does not have an alias/mapped value, then use the provided field name to
        /// check for naming violations.
        /// </summary>
        /// <param name="entity">Entity to check </param>
        /// <param name="databaseColumnName">Name to evaluate against GraphQL naming requirements</param>
        /// <param name="graphQLEnabledGlobally">Whether GraphQL is enabled globally in the runtime configuration.</param>
        /// <exception cref="DataApiBuilderException"/>
        /// <returns>True if no name rules are broken. Otherwise, false</returns>
        public static bool IsGraphQLReservedName(Entity entity, string databaseColumnName, bool graphQLEnabledGlobally)
        {
            if (graphQLEnabledGlobally)
            {
                if (entity.GraphQL is null || (entity.GraphQL.Enabled))
                {
                    if (entity.Mappings is not null
                       && entity.Mappings.TryGetValue(databaseColumnName, out string? fieldAlias)
                       && !string.IsNullOrWhiteSpace(fieldAlias))
                    {
                        databaseColumnName = fieldAlias;
                    }

                    if (entity.Fields is not null)
                    {
                        FieldMetadata? fieldMeta = entity.Fields.FirstOrDefault(f => f.Name == databaseColumnName);
                        if (fieldMeta != null && !string.IsNullOrWhiteSpace(fieldMeta.Alias))
                        {
                            databaseColumnName = fieldMeta.Alias;
                        }
                    }

                    return IsIntrospectionField(databaseColumnName);
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the DataTable from the EntitiesDataSet if already present.
        /// If not present, fills it first and returns the same.
        /// </summary>
        private async Task<DataTable> GetTableWithSchemaFromDataSetAsync(
            string entityName,
            string schemaName,
            string tableName)
        {
            // Because we have an instance of SqlMetadataProvider for each individual database
            // (note: this means each actual database not each database type), we do not
            // need to worry about collisions beyond that schema, hence no database name is needed.
            string tableNameWithSchemaPrefix = GetTableNameWithSchemaPrefix(
                schemaName: schemaName,
                tableName: tableName);

            DataTable? dataTable = EntitiesDataSet.Tables[tableNameWithSchemaPrefix];
            if (dataTable is null)
            {
                try
                {
                    dataTable = await FillSchemaForTableAsync(schemaName, tableName);
                }
                catch (Exception ex) when (ex is not DataApiBuilderException)
                {
                    string message;
                    // Check exception content to ensure proper error message for connection string.
                    // If MySql has a non-empty, invalid connection string, it will have the
                    // MYSQL_INVALID_CONNECTION_STRING_MESSAGE in its message when the connection
                    // string is totally invalid and lacks even the basic format of a valid connection
                    // string (ie: ConnectionString="&#@&^@*&^#$"), or will have a targetsite in
                    // the exception with a name of MYSQL_INVALID_CONNECTION_STRING_OPTIONS in the
                    // case where the connection string follows the correct general form, but does
                    // not have keys with valid names (ie: ConnectionString="foo=bar;baz=qux")
                    if (ex.Message.Contains(MySqlMetadataProvider.MYSQL_INVALID_CONNECTION_STRING_MESSAGE) ||
                       (ex.TargetSite is not null &&
                        string.Equals(ex.TargetSite.Name, MySqlMetadataProvider.MYSQL_INVALID_CONNECTION_STRING_OPTIONS)))
                    {
                        message = DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE +
                            $"Underlying Exception message: {ex.Message}";
                    }
                    else
                    {
                        message = $"Cannot obtain Schema for entity {entityName} " +
                            $"with underlying database object source: {schemaName}.{tableName} " +
                            $"due to: {ex.Message}";
                    }

                    throw new DataApiBuilderException(
                        message,
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization,
                        innerException: ex);
                }
            }

            return dataTable!;
        }

        /// <summary>
        /// This method attempts to open a database connection using the provided connection string.
        /// If the connection fails to open, it catches the exception and throws a DataApiBuilderException.
        /// It is specifically used to validate the connection string provided in the runtime configuration
        /// for single datasource.
        /// </summary>
        private async Task ValidateDatabaseConnection()
        {
            using ConnectionT conn = new();
            conn.ConnectionString = ConnectionString;
            await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName);
            try
            {
                await conn.OpenAsync();
            }
            catch (Exception ex)
            {
                string message = DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE +
                    $" Database connection failed due to: {ex.Message}";
                throw new DataApiBuilderException(
                    message,
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization,
                    innerException: ex);
            }
        }

        /// <summary>
        /// Using a data adapter, obtains the schema of the given table name
        /// and adds the corresponding DataTable to the entities data set.
        /// Columns whose data type the data provider cannot map to a CLR type are left out of the
        /// projection, because the data adapter refuses to build a schema mapping for them and the
        /// whole object would otherwise be unreachable. See <see cref="UnsupportedColumnDataTypes"/>.
        /// </summary>
        private async Task<DataTable> FillSchemaForTableAsync(
            string schemaName,
            string tableName)
        {
            using ConnectionT conn = new();
            // If connection string is set to empty string
            // we throw here to avoid having to sort out
            // complicated db specific exception messages.
            // This is caught and returned as DataApiBuilderException.
            // The runtime config has a public setter so we check
            // here for empty connection string to ensure that
            // it was not set to an invalid state after initialization.
            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                throw new DataApiBuilderException(
                    DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE +
                    " Connection string is null, empty, or whitespace.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            try
            {
                // for non-MySql DB types, this will throw an exception
                // for malformed connection strings
                conn.ConnectionString = ConnectionString;
                await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName);
            }
            catch (Exception ex)
            {
                string message = DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE +
                    $" Underlying Exception message: {ex.Message}";
                throw new DataApiBuilderException(
                    message,
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization,
                    innerException: ex);
            }

            string tableNameWithSchemaPrefix = GetTableNameWithSchemaPrefix(schemaName, tableName);

            // Resolved before the connection below is opened. Reading the catalog uses a connection
            // of its own, and nesting that inside an already open one exhausts a small pool: with
            // "Max Pool Size=1" the inner open waits for a connection the outer scope still holds.
            string projection = await BuildSchemaProjectionAsync(schemaName, tableName);

            bool isProjectionNarrowed = !string.Equals(projection, "*", StringComparison.Ordinal);

            // Resolved before the connection below is opened, for the same reason as the projection:
            // reading the catalog uses a connection of its own, and nesting that inside an already
            // open one exhausts a small pool.
            ObjectCatalogMetadata? catalogMetadata = isProjectionNarrowed
                ? await GetCachedObjectCatalogMetadataAsync(schemaName, tableName)
                : null;

            string selectStatement = $"SELECT {projection} FROM {tableNameWithSchemaPrefix}";

            // An ordinary view has no indexes of its own, so the catalog lookup above finds no key
            // for it. FillSchema resolved one through the view's underlying table under KeyInfo, and
            // describing the projection recovers the same route — without a reader, and without
            // asking for a CLR type. Only reached for an object the catalog could not key, and
            // resolved before the connection is opened for the same pooling reason as above.
            // Kept out of the cached metadata: it depends on this projection, not on the object.
            List<string> describedKey = catalogMetadata is not null
                && catalogMetadata.PrimaryKeyColumns.Count == 0
                && catalogMetadata.UniqueKeyCandidates.Count == 0
                    ? await GetProjectionKeyFromResultSetAsync(selectStatement)
                    : new List<string>();

            await conn.OpenAsync();

            if (isProjectionNarrowed)
            {
                // The projection left columns out, so the data adapter cannot be used here:
                // FillSchema runs with CommandBehavior.KeyInfo, under which the provider performs
                // its own key discovery and appends key columns missing from the SELECT list as
                // hidden reader columns. A column whose CLR type the provider cannot resolve
                // reintroduces "DataReader.GetFieldType(N) returned null" that way even though the
                // projection excluded it — reachable through the object's own primary key, and
                // through any unique index, including when a supported key is configured through
                // source.key-fields. Reading the shape without KeyInfo keeps the projection
                // authoritative; the primary key comes from the catalog instead.
                return await ReadSchemaWithoutKeyInfoAsync(
                    conn,
                    selectStatement,
                    tableNameWithSchemaPrefix,
                    schemaName,
                    tableName,
                    catalogMetadata,
                    describedKey);
            }

            DataAdapterT adapterForTable = new();
            CommandT selectCommand = new()
            {
                Connection = conn,
                CommandText = selectStatement
            };
            adapterForTable.SelectCommand = selectCommand;

            DataTable[] dataTable = adapterForTable.FillSchema(EntitiesDataSet, SchemaType.Source, tableNameWithSchemaPrefix);
            return dataTable[0];
        }

        /// <summary>
        /// Reads the shape of a narrowed projection without CommandBehavior.KeyInfo and registers
        /// the result in <see cref="EntitiesDataSet"/> under the name the data adapter would have
        /// used, so callers find it there on subsequent lookups. The primary key is taken from the
        /// catalog, because the adapter's own key discovery is precisely what has to be avoided.
        /// </summary>
        private async Task<DataTable> ReadSchemaWithoutKeyInfoAsync(
            ConnectionT conn,
            string selectStatement,
            string tableNameWithSchemaPrefix,
            string schemaName,
            string tableName,
            ObjectCatalogMetadata? catalogMetadata,
            List<string> describedKey)
        {
            DataTable dataTable = new(tableNameWithSchemaPrefix);

            using (CommandT selectCommand = new())
            {
                selectCommand.Connection = conn;
                selectCommand.CommandText = selectStatement;

                // SchemaOnly describes the statement without returning rows. Without KeyInfo the
                // reader carries exactly the projected columns and nothing else.
                using DbDataReader reader =
                    await selectCommand.ExecuteReaderAsync(CommandBehavior.SchemaOnly);

                using DataTable? schemaTable = reader.GetSchemaTable();

                if (schemaTable is null)
                {
                    throw new DataApiBuilderException(
                        message: $"The data provider reported no schema for {schemaName}.{tableName}.",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }

                foreach (DataRow columnInfo in schemaTable.Rows)
                {
                    if (columnInfo["ColumnName"] is not string columnName)
                    {
                        continue;
                    }

                    // The provider-reported DataType is carried through unchanged. Auto-increment is
                    // deliberately not set here — see ApplyIdentityColumnsFromCatalog for why
                    // DataColumn.AutoIncrement cannot be the transport for it.
                    DataColumn column = new(columnName, (Type)columnInfo["DataType"])
                    {
                        // Unknown nullability is treated as nullable. A column wrongly marked
                        // non-nullable is reported as required through REST, GraphQL and OpenAPI and
                        // rejects writes the database would accept, so the permissive direction is
                        // the safe one when the provider does not report the flag.
                        AllowDBNull = columnInfo["AllowDBNull"] is not bool allowDbNull || allowDbNull
                    };

                    dataTable.Columns.Add(column);
                }
            }

            if (catalogMetadata is not null)
            {
                DataColumn[]? keyColumns = ResolveKeyColumns(dataTable, catalogMetadata)
                    ?? (describedKey.Count > 0 ? TryResolveColumns(dataTable, describedKey) : null);

                if (keyColumns is not null)
                {
                    dataTable.PrimaryKey = keyColumns;
                }
            }

            EntitiesDataSet.Tables.Add(dataTable);

            return dataTable;
        }

        /// <summary>
        /// Picks the columns to report as <see cref="DataTable.PrimaryKey"/> on the narrowed path.
        /// The object's own primary key wins. A key column the projection left out is not reported,
        /// and the key is not silently replaced by another candidate either:
        /// PopulateSourceDefinitionAsync fails through RejectUnreadablePrimaryKey when that key is
        /// the one in effect, and a key configured through source.key-fields takes precedence over
        /// this one anyway.
        /// Absent a primary key, the first unique index whose every key column is present is used,
        /// which is what the data adapter reports on the unnarrowed path. Returns null when nothing
        /// identifies a row, leaving the caller to report a missing primary key as it does today.
        /// </summary>
        private static DataColumn[]? ResolveKeyColumns(DataTable dataTable, ObjectCatalogMetadata catalogMetadata)
        {
            if (catalogMetadata.PrimaryKeyColumns.Count > 0)
            {
                return TryResolveColumns(dataTable, catalogMetadata.PrimaryKeyColumns);
            }

            foreach (List<string> uniqueKeyCandidate in catalogMetadata.UniqueKeyCandidates)
            {
                DataColumn[]? keyColumns = TryResolveColumns(dataTable, uniqueKeyCandidate);

                if (keyColumns is not null)
                {
                    return keyColumns;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves every named column against the table, in the order given, or returns null when
        /// one of them is absent.
        /// </summary>
        private static DataColumn[]? TryResolveColumns(DataTable dataTable, List<string> columnNames)
        {
            DataColumn[] columns = new DataColumn[columnNames.Count];

            for (int index = 0; index < columnNames.Count; index++)
            {
                if (!dataTable.Columns.Contains(columnNames[index]))
                {
                    return null;
                }

                columns[index] = dataTable.Columns[columnNames[index]]!;
            }

            return columns;
        }

        /// <summary>
        /// Builds the projection used to read the schema of a database object. Returns "*" unless
        /// the object holds columns whose data type this provider cannot map to a CLR type, in
        /// which case those columns are named out of the projection so the rest stays reachable.
        /// The column list comes from the "Columns" schema collection, which reads catalog metadata
        /// only and therefore never has to materialize the offending type.
        /// </summary>
        /// <exception cref="DataApiBuilderException">
        /// Thrown when every column of the object has an unsupported data type. Returning "*" there
        /// would re-issue the projection that cannot be read, hiding the reason behind the
        /// provider's own error.
        /// </exception>
        private async Task<string> BuildSchemaProjectionAsync(string schemaName, string tableName)
        {
            if (UnsupportedColumnDataTypes.Count == 0)
            {
                return "*";
            }

            List<string> readableColumns = new();
            Dictionary<string, string> skippedColumns = new(StringComparer.OrdinalIgnoreCase);

            try
            {
                DataTable columnsInTable = await GetCachedColumnsAsync(schemaName, tableName);

                // Classify from the "Columns" schema collection first, which is read for every
                // object anyway. Only an object that actually holds an unsupported type needs the
                // additional catalog facts below; asking for them up front would add a query per
                // MSSQL and DWSQL object to every startup.
                List<string> supportedColumns = new();

                foreach (DataRow columnInfo in columnsInTable.Rows)
                {
                    if (columnInfo["COLUMN_NAME"] is not string columnName)
                    {
                        continue;
                    }

                    if (columnInfo["DATA_TYPE"] is not string dataType)
                    {
                        // The catalog did not report a usable type name, so this column cannot be
                        // classified. Leave the projection alone rather than guess.
                        return "*";
                    }

                    if (UnsupportedColumnDataTypes.Contains(dataType))
                    {
                        skippedColumns[columnName] = dataType;
                    }
                    else
                    {
                        supportedColumns.Add(columnName);
                    }
                }

                if (skippedColumns.Count == 0)
                {
                    return "*";
                }

                if (supportedColumns.Count == 0)
                {
                    // Falling back to "*" here would re-issue the very projection that fails, so the
                    // caller would see the opaque provider error instead of the reason for it.
                    throw new DataApiBuilderException(
                        message: $"Every column of {schemaName}.{tableName} has a data type that is not supported: "
                            + $"{FormatSkippedColumns(skippedColumns)}. The object cannot be exposed.",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }

                ObjectCatalogMetadata? catalogMetadata =
                    await GetCachedObjectCatalogMetadataAsync(schemaName, tableName);

                if (catalogMetadata is null)
                {
                    // Without the catalog there is no way to tell which columns the database hides
                    // from "SELECT *". Naming columns anyway would add the hidden period columns of
                    // a temporal table to the exposed contract, so leave the projection alone.
                    return "*";
                }

                foreach (string columnName in supportedColumns)
                {
                    if (catalogMetadata.HiddenColumns.Contains(columnName))
                    {
                        // "SELECT *" does not return this column, so naming it would widen the
                        // exposed contract instead of preserving it. It is not "skipped": it was
                        // never part of the object's shape as the engine sees it, and the read-only
                        // classification does not recognize generated-always period columns, so a
                        // PUT would try to null them.
                        continue;
                    }

                    readableColumns.Add(columnName);
                }
            }
            catch (Exception ex) when (ex is not DataApiBuilderException)
            {
                // The column list is a best-effort optimization: without it the read below behaves
                // exactly as it did before, failing loudly if an unsupported type is present.
                _logger.LogDebug(
                    "Unable to enumerate the columns of {schemaName}.{tableName}: {message}",
                    schemaName,
                    tableName,
                    ex.Message);
                return "*";
            }

            if (readableColumns.Count == 0)
            {
                // Every column that is not of an unsupported type is one the database hides from
                // "SELECT *". Naming the hidden ones is not an option, and "*" would re-issue the
                // projection that fails, so nothing about this object can be read.
                throw new DataApiBuilderException(
                    message: $"No column of {schemaName}.{tableName} can be read: "
                        + $"{FormatSkippedColumns(skippedColumns)} have a data type that is not supported, and "
                        + "every remaining column is one the database does not return from a SELECT *. "
                        + "The object cannot be exposed.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            _skippedColumnsByObject[GetObjectCacheKey(schemaName, tableName)] = skippedColumns;

            _logger.LogWarning(
                "Skipping column(s) of {schemaName}.{tableName} whose data type is not supported: {skippedColumns}. "
                + "They are not exposed through REST, GraphQL or MCP.",
                schemaName,
                tableName,
                FormatSkippedColumns(skippedColumns));

            return string.Join(", ", readableColumns.Select(column => SqlQueryBuilder.QuoteIdentifier(column)));
        }

        /// <summary>
        /// Fails initialization when a configured primary key names a column that was left out of
        /// the projection because its data type is not supported. The key would otherwise stay in
        /// <see cref="SourceDefinition.PrimaryKey"/> while being absent from
        /// <see cref="SourceDefinition.Columns"/>, and the inconsistency surfaces much later as a
        /// lookup failure while building queries, the OpenAPI document or the EDM model.
        /// </summary>
        private void RejectPrimaryKeyOnUnsupportedColumn(
            string schemaName,
            string tableName,
            SourceDefinition sourceDefinition)
        {
            if (!_skippedColumnsByObject.TryGetValue(GetObjectCacheKey(schemaName, tableName), out Dictionary<string, string>? skippedColumns))
            {
                return;
            }

            foreach (string primaryKey in sourceDefinition.PrimaryKey)
            {
                if (skippedColumns.TryGetValue(primaryKey, out string? dataType))
                {
                    throw new DataApiBuilderException(
                        message: $"The primary key column {primaryKey} of {schemaName}.{tableName} has the data type "
                            + $"{dataType}, which is not supported. A primary key cannot be omitted from the object "
                            + "metadata, so this object cannot be exposed.",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }
            }
        }

        /// <summary>
        /// Marks identity columns on the narrowed schema discovery path, which builds its columns
        /// itself instead of letting the data adapter populate them.
        /// The flag is not carried through <see cref="DataColumn.AutoIncrement"/>: its setter
        /// coerces a DataType it cannot increment to Int32, and SQL Server allows identity on
        /// tinyint, numeric and decimal, so doing that would report the wrong SystemType and reach
        /// parameter typing and the generated API schemas. It comes from the catalog instead.
        /// No-op for every object whose projection was not narrowed, which is where the adapter
        /// still reports the flag itself.
        /// </summary>
        private void ApplyIdentityColumnsFromCatalog(
            string schemaName,
            string tableName,
            SourceDefinition sourceDefinition)
        {
            if (!_objectCatalogMetadataCache.TryGetValue(
                    GetObjectCacheKey(schemaName, tableName),
                    out ObjectCatalogMetadata? catalogMetadata))
            {
                return;
            }

            foreach (string identityColumn in catalogMetadata.IdentityColumns)
            {
                if (sourceDefinition.Columns.TryGetValue(identityColumn, out ColumnDefinition? columnDefinition))
                {
                    columnDefinition.IsAutoGenerated = true;

                    // Matches how the adapter-reported flag is treated above: an auto-increment
                    // column is also read-only.
                    columnDefinition.IsReadOnly = true;
                }
            }
        }

        /// <summary>
        /// Fails initialization with the reason when the object's own primary key includes a column
        /// left out of the projection because its data type is not supported. Without this the
        /// caller reports a missing primary key, which reads as something the user forgot to
        /// configure even though no configuration can express that key.
        /// </summary>
        private void RejectUnreadablePrimaryKey(string schemaName, string tableName)
        {
            string cacheKey = GetObjectCacheKey(schemaName, tableName);

            if (!_skippedColumnsByObject.TryGetValue(cacheKey, out Dictionary<string, string>? skippedColumns)
                || !_objectCatalogMetadataCache.TryGetValue(cacheKey, out ObjectCatalogMetadata? catalogMetadata))
            {
                return;
            }

            foreach (string primaryKeyColumn in catalogMetadata.PrimaryKeyColumns)
            {
                if (skippedColumns.TryGetValue(primaryKeyColumn, out string? dataType))
                {
                    throw new DataApiBuilderException(
                        message: $"The primary key of {schemaName}.{tableName} includes the column {primaryKeyColumn}, "
                            + $"whose data type {dataType} is not supported. The object cannot be exposed through that "
                            + "key. Configure source.key-fields with a supported column that identifies a row uniquely, "
                            + "if the object has one.",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }
            }
        }

        /// <summary>
        /// Fails initialization when the configuration names a column that was left out of the
        /// projection because its data type is not supported.
        /// Such a name keeps resolving after the column is gone: the exposed and backing column maps
        /// are built from entity fields and mappings without requiring the column to exist in
        /// <see cref="SourceDefinition.Columns"/>, and the authorization resolver accepts explicitly
        /// included field names. The reference then reaches code that indexes
        /// <see cref="SourceDefinition.Columns"/> and fails per request instead of at startup — a
        /// permission-derived default projection selects the column and the read fails serializing
        /// it, a mutation throws while resolving the backing column, and MCP metadata advertises a
        /// field no other surface has. Rejecting here keeps the configuration and the exposed
        /// contract in agreement. Nothing that worked before stops working: an object with such a
        /// column failed discovery outright, so the entity never loaded.
        /// </summary>
        private void RejectConfiguredReferencesToSkippedColumns(
            string entityName,
            Entity? entity,
            string schemaName,
            string tableName)
        {
            if (entity is null
                || !_skippedColumnsByObject.TryGetValue(
                    GetObjectCacheKey(schemaName, tableName),
                    out Dictionary<string, string>? skippedColumnsForObject))
            {
                return;
            }

            // Held in a non-nullable local because the local function below captures it, and the
            // guard above is not something the compiler can carry into that capture.
            Dictionary<string, string> skippedColumns = skippedColumnsForObject;

            // "mappings" and "fields" both key on the backing column name.
            if (entity.Mappings is not null)
            {
                foreach (string backingColumn in entity.Mappings.Keys)
                {
                    RejectReference(backingColumn, "mappings");
                }
            }

            if (entity.Fields is not null)
            {
                foreach (FieldMetadata field in entity.Fields)
                {
                    RejectReference(field.Name, "fields");
                }
            }

            foreach (EntityPermission permission in entity.Permissions)
            {
                foreach (EntityAction action in permission.Actions)
                {
                    // A database policy is parsed per request against the OData model, which is
                    // built from SourceDefinition.Columns. A policy naming a column that is not
                    // there fails every request for that role instead of the configuration being
                    // rejected once, at startup.
                    foreach (string policyField in EnumeratePolicyFieldReferences(action.Policy?.Database))
                    {
                        // Policy identifiers are exposed names, the way the OData model's properties
                        // are, so they are resolved through the configured aliases before being
                        // compared against backing column names. Without that, an alias over a
                        // supported column that happens to carry the name of a skipped one — a
                        // "Location" alias of a text column beside a skipped "Location" spatial
                        // column — would be rejected even though the policy references the
                        // supported field.
                        RejectReference(
                            ResolveBackingColumnName(entity, policyField),
                            $"database policy of role {permission.Role}");
                    }

                    if (action.Fields?.Include is null)
                    {
                        continue;
                    }

                    // An include list names columns that have to be readable. An exclude list naming
                    // one of these columns asks for what already happened, so it is left alone.
                    foreach (string includedField in action.Fields.Include)
                    {
                        RejectReference(includedField, $"permissions of role {permission.Role}");
                    }
                }
            }

            void RejectReference(string configuredName, string configurationSection)
            {
                if (!skippedColumns.TryGetValue(configuredName, out string? dataType))
                {
                    return;
                }

                throw new DataApiBuilderException(
                    message: $"The {configurationSection} of entity {entityName} reference the column {configuredName} "
                        + $"of {schemaName}.{tableName}, whose data type {dataType} is not supported. That column is not "
                        + "part of the exposed contract, so the reference cannot be honored. Remove it from the "
                        + "configuration.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }
        }

        /// <summary>
        /// Yields the field names a database policy references through its "@item." prefix.
        /// Single-quoted literals are skipped, with a doubled quote read as an escaped quote inside
        /// one, so a literal that merely contains the prefix is not mistaken for a reference.
        /// Scanned by hand rather than parsed: the OData model the parser needs does not exist yet
        /// at this point in initialization. The names yielded are exposed names, which the caller
        /// resolves through the configured aliases.
        /// </summary>
        private static IEnumerable<string> EnumeratePolicyFieldReferences(string? databasePolicy)
        {
            const string POLICY_FIELD_PREFIX = "@item.";

            if (string.IsNullOrWhiteSpace(databasePolicy))
            {
                yield break;
            }

            int index = 0;

            while (index < databasePolicy.Length)
            {
                if (databasePolicy[index] == '\'')
                {
                    index++;

                    while (index < databasePolicy.Length)
                    {
                        if (databasePolicy[index] != '\'')
                        {
                            index++;
                            continue;
                        }

                        // A doubled quote is an escaped quote within the literal, not its end.
                        if (index + 1 < databasePolicy.Length && databasePolicy[index + 1] == '\'')
                        {
                            index += 2;
                            continue;
                        }

                        index++;
                        break;
                    }

                    continue;
                }

                if (string.Compare(
                        databasePolicy,
                        index,
                        POLICY_FIELD_PREFIX,
                        0,
                        POLICY_FIELD_PREFIX.Length,
                        StringComparison.OrdinalIgnoreCase) != 0)
                {
                    index++;
                    continue;
                }

                int fieldStart = index + POLICY_FIELD_PREFIX.Length;
                int fieldEnd = fieldStart;

                while (fieldEnd < databasePolicy.Length
                    && (char.IsLetterOrDigit(databasePolicy[fieldEnd]) || databasePolicy[fieldEnd] == '_'))
                {
                    fieldEnd++;
                }

                if (fieldEnd > fieldStart)
                {
                    yield return databasePolicy[fieldStart..fieldEnd];
                }

                index = fieldEnd > fieldStart ? fieldEnd : fieldStart;
            }
        }

        /// <summary>
        /// Resolves an exposed field name to the column it is backed by, using the aliases the
        /// configuration declares through "mappings" and through a field's "alias". An unaliased
        /// name is already the backing name.
        /// </summary>
        private static string ResolveBackingColumnName(Entity entity, string exposedName)
        {
            // "fields" is consulted before "mappings", mirroring the precedence
            // GenerateExposedToBackingColumnMapUtil applies when it builds the map the runtime
            // resolves names through. Reversing it here would reject a configuration the runtime
            // resolves the other way.
            if (entity.Fields is not null)
            {
                foreach (FieldMetadata field in entity.Fields)
                {
                    if (!string.IsNullOrWhiteSpace(field.Alias)
                        && string.Equals(field.Alias, exposedName, StringComparison.OrdinalIgnoreCase))
                    {
                        return field.Name;
                    }
                }
            }

            if (entity.Mappings is not null)
            {
                foreach (KeyValuePair<string, string> mapping in entity.Mappings)
                {
                    if (string.Equals(mapping.Value, exposedName, StringComparison.OrdinalIgnoreCase))
                    {
                        return mapping.Key;
                    }
                }
            }

            return exposedName;
        }

        /// <summary>
        /// Renders skipped columns as "name (type)" pairs for log and error messages.
        /// </summary>
        private static string FormatSkippedColumns(Dictionary<string, string> skippedColumns)
        {
            return string.Join(", ", skippedColumns.Select(entry => $"{entry.Key} ({entry.Value})"));
        }

        /// <summary>
        /// Key used by the per-object metadata caches held during initialization. The schema name is
        /// length-prefixed rather than joined with a dot, because bracketed identifiers may contain
        /// dots: <c>[a.b].[c]</c> and <c>[a].[b.c]</c> are different objects that a "schema.table"
        /// key would collide, letting one reuse the other's catalog rows.
        /// </summary>
        private static string GetObjectCacheKey(string schemaName, string tableName)
        {
            return $"{schemaName.Length}:{schemaName}{tableName}";
        }

        /// <summary>
        /// Releases the catalog metadata gathered during initialization. Nothing reads these caches
        /// once object definitions are populated, and the cached tables are disposable.
        /// </summary>
        private void ReleaseCatalogMetadataCaches()
        {
            foreach (DataTable columnsInTable in _columnsMetadataCache.Values)
            {
                columnsInTable.Dispose();
            }

            _columnsMetadataCache.Clear();
            _skippedColumnsByObject.Clear();
            _objectCatalogMetadataCache.Clear();
        }

        /// <summary>
        /// Returns the "Columns" schema collection for a database object, reading it from the
        /// catalog once per object. Schema discovery and column definition population both need it,
        /// and each <see cref="GetColumnsAsync"/> call opens its own connection.
        /// </summary>
        private async Task<DataTable> GetCachedColumnsAsync(string schemaName, string tableName)
        {
            string cacheKey = GetObjectCacheKey(schemaName, tableName);

            if (_columnsMetadataCache.TryGetValue(cacheKey, out DataTable? cachedColumns))
            {
                return cachedColumns;
            }

            DataTable columnsInTable = await GetColumnsAsync(schemaName, tableName);
            _columnsMetadataCache[cacheKey] = columnsInTable;

            return columnsInTable;
        }

        /// <summary>
        /// Gets the correctly formatted table name with schema as prefix, if one exists.
        /// A schema prefix is simply the correctly formatted and prefixed schema name that
        /// is provided, separated from the table name by a ".". The formatting for both the
        /// schema and table name is based on database type and may or may not include
        /// [] quotes depending how the particular database type handles said format.
        /// </summary>
        /// <param name="schemaName">Name of schema the table belongs within.</param>
        /// <param name="tableName">Name of the table.</param>
        /// <returns>Properly formatted table name with schema prefix if it exists.</returns>
        internal string GetTableNameWithSchemaPrefix(string schemaName, string tableName)
        {
            IQueryBuilder queryBuilder = GetQueryBuilder();
            StringBuilder tablePrefix = new();

            if (!string.IsNullOrEmpty(schemaName))
            {
                // Determine schemaName for prefix.
                schemaName = queryBuilder.QuoteIdentifier(schemaName);
                // Database name is empty we just need the schema name.
                tablePrefix.Append(schemaName);
            }

            string queryPrefix = string.IsNullOrEmpty(tablePrefix.ToString()) ? string.Empty : $"{tablePrefix}.";
            return $"{queryPrefix}{SqlQueryBuilder.QuoteIdentifier(tableName)}";
        }

        /// <summary>
        /// Gets the metadata information of each column of
        /// the given schema.table
        /// </summary>
        /// <returns>A data table where each row corresponds to a
        /// column of the table.</returns>
        protected virtual async Task<DataTable> GetColumnsAsync(
            string schemaName,
            string tableName)
        {
            using ConnectionT conn = new();
            conn.ConnectionString = ConnectionString;
            await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName);
            await conn.OpenAsync();
            // We can specify the Catalog, Schema, Table Name, Column Name to get
            // the specified column(s).
            // Hence, we should create a 4 members array.
            string[] columnRestrictions = new string[NUMBER_OF_RESTRICTIONS];

            // To restrict the columns for the current table, specify the table's name
            // in column restrictions.
            columnRestrictions[0] = conn.Database;
            columnRestrictions[1] = schemaName;
            columnRestrictions[2] = tableName;

            // Each row in the columnsInTable DataTable corresponds to
            // a single column of the table.
            DataTable columnsInTable = await conn.GetSchemaAsync("Columns", columnRestrictions);

            return columnsInTable;
        }

        /// <summary>
        /// Helper method to populate the column definition with HasDefault and DbType properties.
        /// </summary>
        protected virtual void PopulateColumnDefinitionWithHasDefaultAndDbType(
            SourceDefinition sourceDefinition,
            DataTable allColumnsInTable)
        {
            foreach (DataRow columnInfo in allColumnsInTable.Rows)
            {
                string columnName = (string)columnInfo["COLUMN_NAME"];
                bool hasDefault =
                    Type.GetTypeCode(columnInfo["COLUMN_DEFAULT"].GetType()) != TypeCode.DBNull;
                if (sourceDefinition.Columns.TryGetValue(columnName, out ColumnDefinition? columnDefinition))
                {
                    columnDefinition.HasDefault = hasDefault;

                    if (hasDefault)
                    {
                        columnDefinition.DefaultValue = columnInfo["COLUMN_DEFAULT"];
                    }

                    columnDefinition.DbType = TypeHelper.GetDbTypeFromSystemType(columnDefinition.SystemType);
                }
            }
        }

        /// <summary>
        /// Fills the table definition with information of the foreign keys
        /// for all the tables.
        /// </summary>
        private async Task PopulateForeignKeyDefinitionAsync()
        {
            // For each database object, that has a relationship metadata,
            // build the array storing all the schemaNames(for now the defaultSchemaName)
            // and the array for all tableNames
            List<string> schemaNames = new();
            List<string> tableNames = new();
            IEnumerable<SourceDefinition> dbEntitiesToBePopulatedWithFK =
                FindAllEntitiesWhoseForeignKeyIsToBeRetrieved(schemaNames, tableNames);

            // No need to do any further work if there are no FK to be retrieved
            if (!dbEntitiesToBePopulatedWithFK.Any())
            {
                return;
            }

            // Build the query required to get the foreign key information.
            BaseSqlQueryBuilder queryBuilder = (BaseSqlQueryBuilder)GetQueryBuilder();
            string foreignKeyMetadataQuery = queryBuilder.BuildForeignKeyInfoQuery(numberOfParameters: tableNames.Count);

            // Build the parameters dictionary for the foreign key info query
            // consisting of all schema names and table names.
            Dictionary<string, DbConnectionParam> foreignKeyMetadataQueryParameters =
                GetForeignKeyQueryParams(
                    schemaNames.ToArray(),
                    tableNames.ToArray());

            // Saves the <RelationShipPair, ForeignKeyDefinition> objects returned from query execution.
            // RelationShipPair: referencing, referenced tables
            // ForeignKeyDefinition: referecing, referenced columns
            PairToFkDefinition = await QueryExecutor.ExecuteQueryAsync(
                sqltext: foreignKeyMetadataQuery,
                parameters: foreignKeyMetadataQueryParameters,
                dataReaderHandler: SummarizeFkMetadata,
                dataSourceName: _dataSourceName,
                httpContext: null,
                args: null);

            if (PairToFkDefinition is not null)
            {
                FillInferredFkInfo(dbEntitiesToBePopulatedWithFK);
            }

            ValidateAllFkHaveBeenInferred(dbEntitiesToBePopulatedWithFK);
        }

        /// <summary>
        /// Identifies SourceDefinitions of table-backed entities that define relationships in the runtime config.
        /// Helper method to find all the entities whose foreign key information is to be retrieved.
        /// </summary>
        /// <param name="schemaNames">List of names of the schemas to which entities belong.</param>
        /// <param name="tableNames">List of names of the entities(tables)</param>
        /// <returns>A collection of distinct entity names</returns>
        private IEnumerable<SourceDefinition> FindAllEntitiesWhoseForeignKeyIsToBeRetrieved(
            List<string> schemaNames,
            List<string> tableNames)
        {
            Dictionary<string, SourceDefinition> sourceNameToSourceDefinition = new();
            foreach ((string entityName, DatabaseObject dbObject) in EntityToDatabaseObject)
            {
                // Ensure we're only doing this on tables, not stored procedures which have no table definition,
                // not views whose underlying base table's foreign key constraints are taken care of
                // by database itself.
                if (dbObject.SourceType is EntitySourceType.Table)
                {
                    // We only keep track of unique tables identified.
                    if (!sourceNameToSourceDefinition.ContainsKey(dbObject.Name))
                    {
                        SourceDefinition sourceDefinition = GetSourceDefinition(entityName);
                        foreach ((_, RelationshipMetadata relationshipData)
                            in sourceDefinition.SourceEntityRelationshipMap)
                        {
                            IEnumerable<List<ForeignKeyDefinition>> foreignKeysForAllTargetEntities
                                = relationshipData.TargetEntityToFkDefinitionMap.Values;
                            foreach (List<ForeignKeyDefinition> fkDefinitionsForTargetEntity
                                in foreignKeysForAllTargetEntities)
                            {
                                foreach (ForeignKeyDefinition fk in fkDefinitionsForTargetEntity)
                                {
                                    schemaNames.Add(fk.Pair.ReferencingDbTable.SchemaName);
                                    tableNames.Add(fk.Pair.ReferencingDbTable.Name);
                                    sourceNameToSourceDefinition.TryAdd(dbObject.Name, sourceDefinition);
                                }
                            }
                        }
                    }
                }
            }

            return sourceNameToSourceDefinition.Values;
        }

        /// <summary>
        /// Method to validate that the foreign key information is populated
        /// for all the expected entities
        /// </summary>
        /// <param name="dbEntitiesToBePopulatedWithFK">List of database entities
        /// whose definition has to be populated with foreign key information.</param>
        /// <exception cref="NotSupportedException"></exception>
        private void ValidateAllFkHaveBeenInferred(
            IEnumerable<SourceDefinition> dbEntitiesToBePopulatedWithFK)
        {
            foreach (SourceDefinition sourceDefinition in dbEntitiesToBePopulatedWithFK)
            {
                foreach ((string sourceEntityName, RelationshipMetadata relationshipData)
                        in sourceDefinition.SourceEntityRelationshipMap)
                {
                    IEnumerable<List<ForeignKeyDefinition>> foreignKeys = relationshipData.TargetEntityToFkDefinitionMap.Values;
                    // If none of the inferred foreign keys have the referencing columns,
                    // it means metadata is still missing. DAB startup must fail and terminate.
                    bool isAtLeastOneEntityMissingReferencingColumns = foreignKeys.Any(fkList => fkList.Any(fk => fk.ReferencingColumns.Count == 0));
                    if (isAtLeastOneEntityMissingReferencingColumns)
                    {
                        HandleOrRecordException(new NotSupportedException($"Some of relationship information is missing and could not be inferred for {sourceEntityName}."));
                    }
                }
            }
        }

        /// <summary>
        /// Each row in the results of the given data reader represents one column from one foreign key
        /// between an ordered pair of referencing and referenced database objects.
        /// This data reader handler summarizes this foreign key metadata so that
        /// for each referencing and referenced table pair, there is exactly one foreign key definition
        /// containing the list of all referencing columns and referenced columns.
        /// </summary>
        /// <param name="reader">The DbDataReader.</param>
        /// <param name="args">Arguments to this function.</param>
        /// <returns>A dictionary mapping ordered relationship pairs to
        /// foreign key definition between them.</returns>
        private async Task<Dictionary<RelationShipPair, ForeignKeyDefinition>?>
            SummarizeFkMetadata(DbDataReader reader, List<string>? args = null)
        {
            // Extract all the rows in the current Result Set of DbDataReader.
            DbResultSet foreignKeysInfoWithProperties =
                await QueryExecutor.ExtractResultSetFromDbDataReaderAsync(reader);

            Dictionary<RelationShipPair, ForeignKeyDefinition> pairToFkDefinition = new();

            foreach (DbResultSetRow foreignKeyInfoWithProperties in foreignKeysInfoWithProperties.Rows)
            {
                Dictionary<string, object?> foreignKeyInfo = foreignKeyInfoWithProperties.Columns;
                string referencingSchemaName =
                    (string)foreignKeyInfo[$"Referencing{nameof(DatabaseObject.SchemaName)}"]!;
                string referencingTableName = (string)foreignKeyInfo[$"Referencing{nameof(SourceDefinition)}"]!;
                string referencedSchemaName =
                    (string)foreignKeyInfo[$"Referenced{nameof(DatabaseObject.SchemaName)}"]!;
                string referencedTableName = (string)foreignKeyInfo[$"Referenced{nameof(SourceDefinition)}"]!;

                DatabaseTable referencingDbObject = GenerateDbTable(referencingSchemaName, referencingTableName);
                DatabaseTable referencedDbObject = GenerateDbTable(referencedSchemaName, referencedTableName);
                RelationShipPair pair = new(referencingDbObject, referencedDbObject);
                if (!pairToFkDefinition.TryGetValue(pair, out ForeignKeyDefinition? foreignKeyDefinition))
                {
                    foreignKeyDefinition = new()
                    {
                        Pair = pair
                    };
                    pairToFkDefinition.Add(pair, foreignKeyDefinition);
                }

                // Add the referenced and referencing columns to the foreign key definition.
                foreignKeyDefinition.ReferencedColumns.Add(
                    (string)foreignKeyInfo[nameof(ForeignKeyDefinition.ReferencedColumns)]!);
                foreignKeyDefinition.ReferencingColumns.Add(
                    (string)foreignKeyInfo[nameof(ForeignKeyDefinition.ReferencingColumns)]!);
            }

            return pairToFkDefinition;
        }

        /// <summary>
        /// Helper method to get all the read-only fields name in a table by processing the DbDataReader instance
        /// which contains the name of all the fields - one field per DbResult row.
        /// </summary>
        /// <param name="reader">The DbDataReader.</param>
        /// <param name="args">Arguments to this function. This parameter is unused in this method.
        /// This is added so that the method conforms with the Func delegate's signature.</param>
        /// <returns>List of read-only fields present in the table.</returns>
        private async Task<List<string>>
            SummarizeReadOnlyFieldsMetadata(DbDataReader reader, List<string>? args = null)
        {
            // Extract all the rows in the current Result Set of DbDataReader.
            DbResultSet readOnlyFieldRowsWithProperties =
                await QueryExecutor.ExtractResultSetFromDbDataReaderAsync(reader);

            List<string> readOnlyFields = new();

            foreach (DbResultSetRow readOnlyFieldRowWithProperties in readOnlyFieldRowsWithProperties.Rows)
            {
                Dictionary<string, object?> readOnlyFieldInfo = readOnlyFieldRowWithProperties.Columns;
                string fieldName = (string)readOnlyFieldInfo["COLUMN_NAME"]!;
                readOnlyFields.Add(fieldName);
            }

            return readOnlyFields;
        }

        /// <summary>
        /// Hydrates the table definition (SourceDefinition) with database foreign key
        /// metadata that define a relationship's referencing and referenced columns.
        /// </summary>
        /// <param name="dbEntitiesToBePopulatedWithFK">List of database entities
        /// whose definition has to be populated with foreign key information.</param>
        private void FillInferredFkInfo(
            IEnumerable<SourceDefinition> dbEntitiesToBePopulatedWithFK)
        {
            foreach (SourceDefinition sourceDefinition in dbEntitiesToBePopulatedWithFK)
            {
                foreach ((string sourceEntityName, RelationshipMetadata relationshipData)
                       in sourceDefinition.SourceEntityRelationshipMap)
                {
                    // Create ForeignKeyDefinition objects representing the relationships
                    // between the source entity and each of its defined target entities.
                    foreach ((string targetEntityName, List<ForeignKeyDefinition> fKDefinitionsToTarget) in relationshipData.TargetEntityToFkDefinitionMap)
                    {
                        // fkDefinitionsToTarget is a List that is hydrated differently depending
                        // on the source of the relationship metadata:
                        // 1. Database FK constraints:
                        //      - One ForeignKeyDefinition with the db schema specified Referencing and Referenced tables.
                        // 2. Config Defined:
                        //      - Two ForeignKeyDefinition objects:
                        //        1.  Referencing table: Source entity, Referenced table: Target entity
                        //        2.  Referencing table: Target entity, Referenced table: Source entity
                        List<ForeignKeyDefinition> validatedFKDefinitionsToTarget = GetValidatedFKs(fKDefinitionsToTarget);
                        relationshipData.TargetEntityToFkDefinitionMap[targetEntityName] = validatedFKDefinitionsToTarget;
                    }
                }
            }
        }

        /// <summary>
        /// Loops over all the foreign key definitions defined for the target entity in the source entity's definition
        /// and adds to the set of validated FK definitions:
        /// 1. All the FK definitions which actually map to a foreign key constraint defined in the database.
        ///    - When the source/target fields are also provided in the config, they override the database resolved FK constraint.
        /// 2. FK definitions for custom relationships defined by the user in the configuration file where no FK constraint exists between
        /// the pair of (source, target) entities.
        /// </summary>
        /// <param name="fKDefinitionsToTarget">List of FK definitions defined in the runtime config from source to target.</param>
        /// <returns>List of validated FK definitions from source to target.</returns>
        private List<ForeignKeyDefinition> GetValidatedFKs(List<ForeignKeyDefinition> fKDefinitionsToTarget)
        {
            List<ForeignKeyDefinition> validatedFKDefinitionsToTarget = new();
            foreach (ForeignKeyDefinition configResolvedFkDefinition in fKDefinitionsToTarget)
            {
                // Check whether DAB found a match between 'configResolvedFkDefinition' and 'databaseResolvedFKDefinition's {referencing -> referenced} entity pair.
                if (PairToFkDefinition is not null &&
                    PairToFkDefinition.TryGetValue(configResolvedFkDefinition.Pair, out ForeignKeyDefinition? databaseResolvedFkDefinition))
                {
                    if (DoesConfiguredRelationshipOverrideDatabaseFkConstraint(configResolvedFkDefinition))
                    {
                        validatedFKDefinitionsToTarget.Add(configResolvedFkDefinition);

                        // Save additional metadata for use when processing requests on self-joined/referencing entities.
                        if (IsSelfJoiningRelationship(configResolvedFkDefinition))
                        {
                            EntityRelationshipKey entityToFkDefKey = new(
                                entityName: configResolvedFkDefinition.SourceEntityName,
                                relationshipName: configResolvedFkDefinition.RelationshipName);
                            RelationshipToFkDefinition.TryAdd(entityToFkDefKey, configResolvedFkDefinition);
                        }
                    }
                    else
                    {
                        // When the configured relationship doesn't override the database FK constraint,
                        // DAB can consolidate the referenced and referencing columns from the database FK definition
                        // into the configResolvedFkDefinition object.
                        configResolvedFkDefinition.ReferencedColumns = databaseResolvedFkDefinition.ReferencedColumns;
                        configResolvedFkDefinition.ReferencingColumns = databaseResolvedFkDefinition.ReferencingColumns;
                        validatedFKDefinitionsToTarget.Add(configResolvedFkDefinition);

                        // Save additional metadata for use when processing requests on self-joined/referencing entities.
                        // Since the configResolvedFkDefinition has additional metadata populated, DAB supplements that
                        // object by using the inferred FK definition's referenced/referencing columns.
                        if (IsSelfJoiningRelationship(databaseResolvedFkDefinition))
                        {
                            EntityRelationshipKey entityToFkDefKey = new(
                                entityName: configResolvedFkDefinition.SourceEntityName,
                                relationshipName: configResolvedFkDefinition.RelationshipName);
                            RelationshipToFkDefinition.TryAdd(entityToFkDefKey, configResolvedFkDefinition);
                        }
                    }
                }
                else
                {
                    // A database foreign key doesn't exist that matches configResolvedFkDefinition's referencing and referenced
                    // tables. This section now checks whether DAB resolved a database foreign key definition
                    // matching the inverse order of the referencing/referenced tables.
                    // A match indicates that a FK constraint exists between the source and target entities and
                    // DAB can skip adding the optimstically created configResolvedFkDefinition
                    // to the list of validated foreign key definitions.
                    //
                    // A database FK constraint may exist between the inverse order of referencing/referenced tables
                    // in configResolvedFkDefinition when the relationship has a right cardinality of 1.
                    // DAB optimistically created ForeignKeyDefinition objects denoting relationships between:
                    // both source->target and target->source to the entity's SourceDefinition
                    // because during relationship preprocessing, DAB doesn't know if the relationship is an N:1 a 1:1 relationship.
                    // So here, we need to remove the "wrong" FK definition for:
                    // 1. N:1 relationships,
                    // 2. 1:1 relationships where an FK constraint exists only from source->target or target->source but not both.
                    //
                    // E.g. For a relationship between Book->Publisher entities with cardinality configured to 1 (many to one),
                    // DAB added two Foreign key definitions to Book's source definition:
                    // 1. Book->Publisher [Referencing: Book, Referenced: Publisher] ** this is the correct foreign key definition
                    // 2. Publisher->Book [Referencing: Publisher, Referenced: Book]
                    // This is because DAB pre-processes runtime config relationships prior to processing database FK definitions.
                    // Consequently, because Book->Publisher is an N:1 relationship, DAB optimistically generated ForeignKeyDefinition
                    // objects for both source->target and target->source entities because DAB doesn't yet have db metadata
                    // to confirm which combination of optimistically generated ForeignKeyDefinition objects matched
                    // the database FK relationship metadata.
                    //
                    // At this point in the code, DAB now has the database resolved FK metadata and can determine whether
                    // 1. configResolvedFkDefinition matches a database fk definition -> isn't added to the list of
                    //    validated FK definitions because it's already added.
                    // 2. configResolvedFkDefinition doesn't match a database fk definition -> added to the list of
                    //    validated FK definitions because it's not already added.
                    bool doesFkExistInDatabase = VerifyForeignKeyExistsInDB(
                        databaseTableA: configResolvedFkDefinition.Pair.ReferencingDbTable,
                        databaseTableB: configResolvedFkDefinition.Pair.ReferencedDbTable);

                    if (!doesFkExistInDatabase)
                    {
                        validatedFKDefinitionsToTarget.Add(configResolvedFkDefinition);

                        // The following operation generates FK metadata for use when processing requests on self-joined/referencing entities.
                        if (IsSelfJoiningRelationship(configResolvedFkDefinition))
                        {
                            EntityRelationshipKey key = new(entityName: configResolvedFkDefinition.SourceEntityName, configResolvedFkDefinition.RelationshipName);
                            RelationshipToFkDefinition.TryAdd(key, configResolvedFkDefinition);
                        }
                    }
                }
            }

            return validatedFKDefinitionsToTarget;
        }

        /// <summary>
        /// Returns whether the supplied foreign key definition denotes a self-joining relationship
        /// by checking whether the backing tables are the same.
        /// </summary>
        /// <param name="fkDefinition">ForeignKeyDefinition representing a relationship.</param>
        /// <returns>true when the ForeignKeyDefinition represents a self-joining relationship</returns>
        private static bool IsSelfJoiningRelationship(ForeignKeyDefinition fkDefinition)
        {
            return fkDefinition.Pair.ReferencedDbTable.FullName.Equals(fkDefinition.Pair.ReferencingDbTable.FullName);
        }

        /// <summary>
        /// When a relationship is defined in the runtime config, the user may define
        /// source and target fields. By doing so, the user overrides the
        /// foreign key constraint defined in the database.
        /// </summary>
        /// <param name="configResolvedFkDefinition">FkDefinition resolved from the runtime config.</param>
        /// <returns>True when the passed in foreign key definition defines referencing/referenced columns.</returns>
        private static bool DoesConfiguredRelationshipOverrideDatabaseFkConstraint(ForeignKeyDefinition configResolvedFkDefinition)
        {
            return configResolvedFkDefinition.ReferencingColumns.Count > 0 && configResolvedFkDefinition.ReferencedColumns.Count > 0;
        }

        /// <summary>
        /// Returns whether DAB has resolved a foreign key from the database
        /// linking databaseTableA and databaseTableB.
        /// A database foreign key definition explicitly denotes the referencing table and the referenced table.
        /// This function creates two RelationShipPair objects, interchanging which datatable is referencing
        /// and which table is referenced, so that DAB can definitevly identify whether a database foreign key exists.
        /// - When DAB pre-processes relationships in the config, DAB creates two foreign key definition objects
        /// because the config doesn't tell DAB which table is referencing vs referenced. This function is called when
        /// DAB is determining which of the two FK definitions to keep.
        /// </summary>
        public bool VerifyForeignKeyExistsInDB(
            DatabaseTable databaseTableA,
            DatabaseTable databaseTableB)
        {
            if (PairToFkDefinition is null)
            {
                return false;
            }

            RelationShipPair pairAB = new(
                referencingDbObject: databaseTableA,
                referencedDbObject: databaseTableB);

            RelationShipPair pairBA = new(
                referencingDbObject: databaseTableB,
                referencedDbObject: databaseTableA);

            return (PairToFkDefinition.ContainsKey(pairAB) || PairToFkDefinition.ContainsKey(pairBA));
        }

        /// <summary>
        /// Retrieving the partition key path, for cosmosdb_nosql only
        /// </summary>
        public string? GetPartitionKeyPath(string database, string container)
            => throw new NotImplementedException();

        /// <summary>
        /// Setting the partition key path, for cosmosdb_nosql only
        /// </summary>
        public void SetPartitionKeyPath(string database, string container, string partitionKeyPath)
            => throw new NotImplementedException();

        public bool IsDevelopmentMode()
        {
            return _runtimeConfigProvider.GetConfig().IsDevelopmentMode();
        }

        /// <inheritdoc/>
        public bool TryGetFKDefinition(
            string sourceEntityName,
            string targetEntityName,
            string referencingEntityName,
            string referencedEntityName,
            [NotNullWhen(true)] out ForeignKeyDefinition? foreignKeyDefinition,
            bool isMToNRelationship = false)
        {
            if (GetEntityNamesAndDbObjects().TryGetValue(sourceEntityName, out DatabaseObject? sourceDbObject) &&
                GetEntityNamesAndDbObjects().TryGetValue(referencingEntityName, out DatabaseObject? referencingDbObject) &&
                GetEntityNamesAndDbObjects().TryGetValue(referencedEntityName, out DatabaseObject? referencedDbObject))
            {
                DatabaseTable referencingDbTable = (DatabaseTable)referencingDbObject;
                DatabaseTable referencedDbTable = (DatabaseTable)referencedDbObject;
                SourceDefinition sourceDefinition = sourceDbObject.SourceDefinition;
                RelationShipPair referencingReferencedPair;
                List<ForeignKeyDefinition> fKDefinitions = sourceDefinition.SourceEntityRelationshipMap[sourceEntityName].TargetEntityToFkDefinitionMap[targetEntityName];

                // At this point, we are sure that a valid foreign key definition would exist from the referencing entity
                // to the referenced entity because we validate it during the startup that the Foreign key information
                // has been inferred for all the relationships.
                if (isMToNRelationship)
                {

                    foreignKeyDefinition = fKDefinitions.FirstOrDefault(
                                                            fk => string.Equals(referencedDbTable.FullName, fk.Pair.ReferencedDbTable.FullName, StringComparison.OrdinalIgnoreCase)
                                                            && fk.ReferencingColumns.Count > 0
                                                            && fk.ReferencedColumns.Count > 0)!;
                }
                else
                {
                    referencingReferencedPair = new(referencingDbTable, referencedDbTable);
                    foreignKeyDefinition = fKDefinitions.FirstOrDefault(
                                                            fk => fk.Pair.Equals(referencingReferencedPair) &&
                                                            fk.ReferencingColumns.Count > 0
                                                            && fk.ReferencedColumns.Count > 0)!;
                }

                return true;
            }

            foreignKeyDefinition = null;
            return false;
        }
    }
}

