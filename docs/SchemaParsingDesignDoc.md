## Scope
This change supports parsing the schema name provided through the developer friendly runtime config. It is built on top of the work being done to implement the new runtime config into the service. To support this parsing, the minimum changes required will be
* Use the deserialization of the developer friendly runtime config json file
* Store or otherwise reference the `Entities` dictionary in the config
* iterate through each `Entity` and parse the schema name and table name in each entity's source into a `Map<Entity, DatabaseObject>`, where the `DatabaseObject` will hold a `schemaName`, `name`, and `tableDefiition`. This Map will be a part of the `DatabaseSchema` section within the config. Parse using algorithm provded here: `https://msdata.visualstudio.com/Database%20Systems/_git/SqlRestApi?path=/SqlREST/Utils/ParamParser.cs`
* for each Entity in the Map we have created use the `schemaName` associated with each `tableName` as the schema name in place of the hard coded default schema names we currently use, and populate the `tableDefinition` within each `DatabaseObject`
* pass to `PopulateForeignKeyDefinitionAsync` an additional 2 lists, one for the schema names, and another for the table names, ensuring they are in the same ordering, we can create these from the previously generated Map.
* must ensure that the ordering of the above tables and schema names are consistent, which will be ensured by pulling from the `DatabaseObject`

The Classes and Datastructures that are being used now are changing so we must be certain that we are populating the new objects in the right manner so that we can correctly transition to the new config.

## Class Alterations
### Startup.cs
>The 'DataGatewayConfig' currently has the location of the JSON used to populate the ResolverConfig bound in `Startup.cs` when services are configured, or when they are changed. This changes to bind the new developer friendly Runtime configuration. We call into the replacement class for 'GraphQLMetadatProvider' once the databse is set in `Startup.cs`, this happens when `PerformOnConfigChangeAsync` invokes `InitializeAsync()`, which will later call `EnrichDatabaseSchemaWithTableMetadata`, the location where we will need the schema names.

* Binding of the `DatagatewayConfig` is replaced by binding of the `RuntimeConfig`
### RuntimeConfigProvider.cs
>In this class constructor is where we read and deserialize the developer friendly runtime configuration.
    
* Add `RuntimeConfig` to class
* Read the developer friendly JSON
* Deserialize this JSON string into the RuntimeConfig that was added

### SqlRuntimeConfigProvider.cs 
>This class is called into via startup, via the entry point of `InitializeAsync()`, which then invokes `EnrichDatabaseSchemaWithTableMetadata()`. It is in this call where we need the schema names and will do the parsing required to collect them. We will parse the entities from the RuntimeConfig and associated a schema name with every table name provided. These will be stored into a Map<entity, databaseobject> which will allow for easy retrieval of the tabledefinition for any given entity.
* In `EnrichDatabaseSchemaWithTableMetadata()` 
    * _foreach_ `entity` in `RuntimeConfig.Entities`
        * Parse Source: add to Map<Entity, DatabaseObject>
        * Parsing can be done using this logic: `https://msdata.visualstudio.com/Database%20Systems/_git/SqlRestApi?path=/SqlREST/Utils/ParamParser.cs`
    * _sqlMetadataProvider.PopulateTableDefinitionAsync(schemaName, tableName, tableDefinition)
    * form 2 lists, one of schemaName and one of tableName with same ordering
    * invoke `PopulateForeignKeyDefinitionAsync()` with these lists added as arguments
### SqlMetadataProvicer.cs
>In `SqlMetadataProvider` we populate the foreign key definitions, which depends on an array of schema names and an array of table in the same orering. We provide this in the form of 2 lists which can easily be converted to arrays in the related function call.
* Use the lists provided to call into the parameter creation helper function `GetForeignKeyQueryParams()`

## Some (as of yet) Unanswered Questions
* How to change or add binding of the configuration on startup?
    * this change is added in the branch we build off of, so is included
* Replace with or provide the RuntimeConfig components in addition to ResolverConfig components?
    * the runtime config is replacing incrementally and will be used in place for this change
* In place of parsing and storing schema name separately, add a new class to hold schema + tablename or modify current classes to hold schema as well, or create/modify class and add as field?
    * the new class is `DatabaseObject` which holds a `schemaName`, `name`, and `TableDefinition`