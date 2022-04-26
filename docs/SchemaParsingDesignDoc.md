## Scope
This change supports parsing the schema name provided through the developer friendly runtime config. To support this parsing, the minimum changes required will be
* add deserialization of the developer friendly runtime config json file
* store or otherwise reference the `Entities` dictionary in the config
* iterate through each `Entity` and parse the schema name and table name in each entity's source into a `Map<`tableName, schemaName`>` for later consumption
* for each tuple in `GraphQLResolverConfig.DatabaseSchema.Tables` use the `schemaName` associated with each `tableName` from the previously created `Map<`tableName, schemaName`>` as the schema name in place of the hard coded default schema names we currently use
* pass to `PopulateForeignKeyDefinitionAsync` an additional 2 lists, one for the schema names, and another for the table names, ensuring they are in the same ordering
* ensure that the ordering of the above tables and schema names are consistent, one option is to join on table name, adding the schema to each table via an additional field or other data structure.

Additionally, the `GraphQLResolverConfig.DatabaseSchema` is populated through the `ResolverConfig` and will not be available once we change to the devloper friendly runtime config. This change may want to replace with or populate with the runtime config in place of the resolver config.

## Class Alterations
### Startup.cs
    The 'DataGatewayConfig' currently has the location of the JSON used to populate the ResolverConfig bound in `Startup.cs` when services are configured, or when they are changed. We call into the 'GraphQLMetadatProvider' once the databse is set in `Startup.cs`, this happens when `PerformOnConfigChangeAsync` invokes `graphQLMetadataProvider.InitializeAsync()`, which will later call `EnrichDatabaseSchemaWithTableMetadata`, the location where we will need the schema names.

* Binding of the `DatagatewayConfig` can be matched with or replaced by binding of the `RuntimeConfig`
### GraphQLFileMetadataProvider.cs
    In this class constructor is where we currently read in and deserialize the `ResolverConfig`, by reading from the location bound by the configuration during startup. This is where we will read and deserialize the developer friendly runtime configuration, either by reading via a provided file location via some string we create or through the binding as with the `ResolverConfig`.
    
* Add `RuntimeConfig` to class
* Read the developer friendly JSON locating either through the value bound in startup or from a string
* Deserialize this JSON string into the RuntimeConfig that was added

### SqlGraphQLFileMetadataProvider.cs 
    This class is called into via startup, via the entry point of `InitializeAsync()`, which then invokes `EnrichDatabaseSchemaWithTableMetadata()`. It is in this call where we need the schema names and will do the parsing required to collect them. We will parse the entities from the RuntimeConfig and associated a schema name with every table name provided. Then when we are populating data needed for the runtime we can always lookup the schema name associated with a given tablename.
* In `EnrichDatabaseSchemaWithTableMetadata()` 
    * _foreach_ `entity` in `RuntimeConfig.Entities`
        * Parse Source: add to Map<tableName, schemName>
    * _sqlMetadataProvider.PopulateTableDefinitionAsync(`Map[tableName].`schemaName, tableName, tableDefinition)
    * form 2 lists, one of schemaName and one of tableName with same ordering
    * invoke `PopulateForeignKeyDefinitionAsync()` with these lists added as arguments
### SqlMetadataProvicer.cs
    In `SqlMetadataProvider` we populate the foreign key definitions, which depends on an array of schema names and an array of table in the same orering. We provide this in the form of 2 lists which can easily be converted to arrays in the related function call.
* Use the lists provided to call into the parameter creation helper function `GetForeignKeyQueryParams()`