## Scope
This change supports parsing the schema name provided through the developer friendly runtime config. It is built on top of the work being done to implement the new runtime config into the service. To support this parsing, the changes we implement are:
* Use the deserialization of the developer friendly runtime config json file
* Store or otherwise reference the `Entities` dictionary in the config
* iterate through each `Entity` and parse the schema name and table name in each entity's source into a `Map<Entity, DatabaseObject>`, where the `DatabaseObject` will hold a `schemaName`, `name`, and `tableDefinition`. This Map will be a part of the `DatabaseSchema` section within the config. Parser class provded here: `https://github.com/Azure/hawaii-gql/blob/dev/aaronburtle/consumeDeveloperConfigWithSchemaParsing/DataGateway.Service/Parsers/EntitySourceNamesParser.cs`
* for each Entity in the Map we have created use the `schemaName` associated with each `tableName` as the schema name in place of the hard coded default schema names we currently use, and populate the `tableDefinition` within each `DatabaseObject`. If no schema name is provided, for PostgreSql we will check for the schema name in the connection string, and then if none exists, or if we have another database type we will use the default schema name for that database type. For MySql no schema should be provided since a `.` will break the naming rules for MySql, and we will instead throw a `DataGateway Exception` in this case.
* pass to `PopulateForeignKeyDefinitionAsync` the collection of database objects in our map, this ensures that each `schema` is associated with the correct database object `name`
* must ensure that the ordering of the above tables and schema names are consistent, which will be ensured by pulling from the `DatabaseObject`

Once we have the schema name correctly parsed and stored in our mapping, we will be able to use the schema name as appropriate in query generation. To facilitate this, the changes that we implement are:
* Save the schema name, and table name individually as part of the `RequestContext`, and we validate that the entity name is valid as part of our request validation.
* Save the schema name, and table individully as part of the `QueryStructure`. We do this by referencing the mapping from entity to database object and then extracting the schema and table name that are mapped to the current entity for our structure.
* Modify the logic in the `QueryBuilder` so that we correctly use the schema as part of the query generation. This needs to take into account when we alias the table name, when we do and do not have a schema available, and ensure that we correctly format the query given the new logic.


## Class Alterations
### Startup.cs
>The 'DataGatewayConfig' currently has the location of the JSON used to populate the ResolverConfig bound in `Startup.cs` when services are configured, or when they are changed. This changes to bind the new developer friendly Runtime configuration. We call into the replacement class for 'GraphQLMetadatProvider', now `SqlMetadataProvider` once the database is set in `Startup.cs`, this happens when `PerformOnConfigChangeAsync()` invokes `InitializeAsync()`, which will later call `GenerateDatabaseObjectForEntities()`, where we will parse the schema name and table name for later consumption.

### RuntimeConfigProvider.cs
>This PR is built on top of work that uses this class to deserialize the new developer friendly `runtime-config.json` into the object mapping that is consumed later by the runtime.

### SqlMetadataProvider.cs 
>This class is called into via startup, via the entry point of `InitializeAsync()`, which then invokes `GenerateDatabaseObjectForEntities()`. It is in this call where we do the parsing of the schema and table names. We will parse the entities from the `RuntimeConfig`. Each of these entities will have a `source` field that can hold the schema and tablename in the format of "schema.table". These names will be stored into a Map<entity, databaseobject>, where `databaseobject` contains a schema name and table (database object) name. This will allow for easy retrieval of the tabledefinition for any given entity.
* In `GenerateDatabaseObjectForEntities()` 
    * _foreach_ `entity` in `RuntimeConfig.Entities`
        * Parse Source: add to Map<Entity, DatabaseObject>
        * Parsing can be done using this class: `https://github.com/Azure/hawaii-gql/blob/dev/aaronburtle/consumeDeveloperConfigWithSchemaParsing/DataGateway.Service/Parsers/EntitySourceNamesParser.cs`
* From `InitializeAsync()` we also invoke `PopulateTableDefinitionAsync(schemaName, tableName, tableDefinition)`, which uses the previously generated mapping of entity to database object to populate the table definitions. Then, from within this call we pass the collection of database objects which constitute the values of our Map<entity, databaseobject> to `PopulateForeignKeyDefinitionAsync`, which uses them to correctly populate the foriegn key info that we need. 

### RestRequestContext.cs
>This class and its derived classes now need to store the schema name that we have parsed, and so we add in fields to represent the new information that is available. We add in fields for both schema name and table name, while keeping the field for the entity name. We also add to the constructors of the following derived classes fields for schema and table names, and then pass those values along to this base class:
* DeleteRequestContext.cs
* FindRequestContext.cs
* UpsertRequestContext.cs
* InsertRequestContext.cs

### BaseSqlQueryStructure.cs
>This class now holds the schema name for the main table that is to be queried. The constructor takes an entity name that is then used to do lookups in the perviously created mapping and then retrieve the schema and table names. In both this class and in the derived classes we must make sure that new objects that are constructed within these classes, where those new objects now contain a schema themselves, have their constructors invoked with the correct schema information, be it the parsed schema, a null value, or something else. The following classes derive from this class and pass entity name to the base class constructor:
* SqlDeleteQueryStructure.cs
* SqlFindQueryStructure.cs
* SqlInsertQueryStructure.cs
* SqlUpsertQueryStructure.cs
* SqlUpdateQueryStructure.cs

### SqlQueryStructure.cs
>Like the other classes that derive from `BaseSqlQueryStructure` we pass the entity name to the base constructor to correctly set the schema name and table name. In this class however we also must set the `TableAlias` since this class is used by the queries which will not involve a mutation. In those cases we use an alias, and that alias is set in the class constructor. We also must ensure that this table alias is populated in any objects that we create where we will need to use the table alias. We therefore assign a table alias to any `OrderByColumn` that does not already have one. This class is also used for GraphQL queries, and we have a constructor invoked for GraphQL where we will set the schema name not from the mapping that we previously built up but instead by looking up the schema based on the table supplied in the `_typeInfo` field: `SchemaName = sqlMetadataProvider.GetSchemaName(_typeInfo.Table)`

### BaseSqlQueryBuilder.cs
> This is where we actually use the schema information to generate a query. The `BaseSqlQueryBuilder` contains shared logic for building certain parts of the query, including the columns. In these cases we need to differentiate between the different kinds of queries that we will build so that we use the correct format. For example, `MySql` queries will not have a `schema`, `mutations` will not have a `table alias`, and `non-mutations` will have a `table alias`. We therefore have logic to check whether or not these fields exist and then return the format that matches based on which fields are present. Derived from this class we also have builders for specific database types. These builders also have their own logic for using the schema (or not) in the generation of those related queries.

### SqlMutationEngine.cs
>The changes in this class are cosmetic.

### RequestValidator.cs
>We verify that the `route` contains a valid entity here which validates the entity that the request is attempting to query.

### Runtime-Config
>The runtime config is a JSON that is created by the command line input of the developer, using a tool that we provide. This configuration can be read about in more detail here: `https://github.com/Azure/project-hawaii/blob/main/rfcs/configuration-file.md`. There are some specific parts of this JSON to take note of with respect to this PR's changes, which would be the `source` field that exists for each `entity` in the configuration. The source is what holds the schema and database object (table) name, and does so by separating schema from table with a `.`. `MySql` will not use a schema name as this configuration defines, and therefore can not contain a `.` in its source. We throw an exception in this case as this is a `BadRequest`. For `MsSql` and `PostgreSql`, a schema is a collection of database objects, or logical structures of data. In this way, a schema can be thought of and used much like a `namespace`. In `MySql` however, the word "schema" is used to reference what would otherwise be thought of as the Database Structure itself, and so for our purposes does not have what we reference as a `schema`. We therefore have the `empty string` as the default schema for `MySql`, while `MsSql` uses `dbo` and `PostgreSql` uses `public`.

### CosmosQueryEngine.cs
>The method `GetPartitionKeyValue` is using `PartitionKeyPath` to find the partition key value from query input parameters, using recursion. Example of `PartitionKeyPath` is `/character/id`.

### CosmosMutationEngine.cs
>For Cosmos, if the nested object field has the same name with an existing model, it will assume it's that model. We are assuming that it's not possible the child nested model has the same name with any other entity, but different json objects.


## Testing

We change how testing is handled in order to provide coverage for the new schem parsing. We include a custom schema of `foo` for the entity `magazines`. For `MySql` this schema is not included. To have the tests maintain shared code, we create a JSON string that represents the runtime configuration for the testing, and we add a constructor to read this JSON string and create the `RuntimeConfig` objects from this string. We then modify this string based on database type so that we can include different `source` values for `MsSql`, `PostgreSql`, and `MySql`. Additional validation of the runtime config and schemas will be included in future work.

We also unit test the schema parser. The unit testing comes from previous work done on SqlREST, and is ported into our project.
