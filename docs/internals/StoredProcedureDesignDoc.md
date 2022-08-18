# Stored Procedure Implementation Plan

## Design/Spec Summary
### Entity Source Object Config
Currently, the `source` attribute is always a string. With the addition of stored procedure support and the next version of the JSON schema: [here](https://github.com/Azure/project-hawaii/blob/db619b4175719c83d540bc30ef5acc5faa6faa6d/playground/hawaii.draft-02.schema.json), the `source` attribute is now optionally an object with attributes like so:
```
"type": {
    "type": "string",                                            
    "enum": [ "table", "view", "stored-procedure" ],
    "description": "Database object type"     
},
"object": {
    "type": "string",
    "description": "Database object name"
},
"parameters": {
    "type": "object",
    "description": "Dictionary of parameters and their values",
```

Thus a stored procedure entity might look like:
```
"sp_entity_name": {
    source: {
        "type": "stored-procedure",
        "object": "sp_name_in_db_schema",
        "parameters": {
            "param1": "val1",
            "param2": "val2"
        }
    } ...
}
```
parameters can either be fixed as above or passed at runtime through
- query parameters for GET request
- request body for POST, PUT, PATCH, DELETE

### Stored Procedure Permissions 

Stored procedures have identical permissions to any other entity. I.e. same familiar format:
```
"permissions": [
        {
            "role": "anonymous",
            "actions": [ "read" ]
            
        },
        {
            "role": "authenticated",
            "actions": [ "create" ]
        }
]
``` 
<a id='suggested-config'></a>
> Why not simplify stored procedure permissions, if POST, PUT, PATCH, DELETE semantically identical?
- `POST` request is the only HTTP method that specifies a client can "submit a command" per [Microsoft API Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/Guidelines.md#741-post)
```
"permissions": [
        {
            "role": "anonymous",
            "access": false
        }, 
        {
            "role": "authenticated",
            "access": true
        }
]
```
Justification of allowing permission configuration for CRUD operations:
- Davide: the "simplification" would complicate authorization with no real benefit. True in that the authorization logic would need to change conditioned on whether the entity source was a stored procedure.
- Aniruddh: we should leave the responsibility for the developer to properly configure hawaii; it's not our fault if they configure against the API guidelines 
- Keeping the same OperationTypes does let us reuse the AuthZ code and the same result set formatting

## Implementation Plan (REST) - By Class

### SqlMetadataProvider.cs

> When we draw metadata from the database schema, we implicitly check if each entity specified in config exists in the database. Path: in `Startup.cs`, the `PerformOnConfigChangeAsync()` method invokes `InitializeAsync()` of the metadata provider bound at runtime, which then invokes `PopulateTableDefinitionForEntities()`. Several steps down the stack `FillSchemaForTableAsync()` is called, which performs a `SELECT * FROM {table_name}` for the given entity and adds the resulting `DataTable` object to the `EntitiesDataSet` class variable. 
> 
> Problem: Unfortunately, stored procedure metadata cannot be queried using the same syntax. As such, running the select statement on a stored procedure source name will cause a Sql exception to surface and result in runtime initialization failure. 
> 
> Change: Instead, we will have to conditionally check if the entity is labeled a stored procedure in config, and then query its metadata through `SELECT * FROM INFORMATION_SCHEMA.ROUTINES WHERE SPECIFIC_NAME = {source_name_in_config}` to see if the stored procedure exists. To get parameter metadata for validation and such, we can use `SELECT * FROM INFORMATION_SCHEMA.PARAMETERS WHERE SPECIFIC_NAME = {source_name}`
> 
> Note: some might suggest querying `sys.all_objects` instead for MsSql. Postgres and MySql have different system table names, so this would require changes to each derived class, which doesn't nicely fit in with the way all the logic has been put in the base class. `INFORMATION_SCHEMA` is being used because it is supported by all sql variants we currently support.

### Entity.cs 

> Need to expose the rest of the `DatabaseObjectSource`, including source type and parameters, rather than just the source name. Necessary for referencing in metadata generation, then in setting appropriate request context in `RestService.cs`

### SqlExecuteStructure.cs

> **New class addition**
Need a structure to build the `EXECUTE {sp_name} {parameters}` statement with. Many of the fields present in other structures are superfluous. The only one I can think to need is parameters (input and output). Predicates in the future perhaps but not now.  Can either inherit from `BaseSqlQueryStructure` or just be implemented standalone.

### DatabaseObject.cs

> Would be a nice-to-have to have input/output parameters in this class for accessing in the `SqlQueryBuilder` classes and their `build` methods.

### MsSqlQueryBuilder.cs

> Starting with sql server, need to add a `build(SqlExecuteStructure structure)` method to build the `EXECUTE {sp_name} {parameters}` statement. Might be extensible to all 3 sql variants we support, in which case it can be moved to `BaseSqlQueryBuilder.cs`


### RestController.cs
> No changes. Stored procedure request will be semantically the same in terms of calling and returning http requests. Yes, that means if you call an http `DELETE url/sp_entity_name`, you will receive a deleted/no content response regardless of what the stored procedure actually does.

### RestService.cs

> We need to condition in `ExecuteAsync` based on the runtimeConfig and/or _metadataProvider whether the entity type requested is a stored procedure. `OperationType` is maintained and most of the AuthZ flow will be maintained, but for now if the entity is a stored procedure we want to bypass 
> - policy parsing
> - ignore the primary key route parsing and validation
> 
> We also want to set the `RestRequestContext` to `StoredProcedureRequestContext` so that the query and mutation engine build the correct query structure - as of now, the query structures are built solely based on the request context, and refactoring this would be a much bigger change than simply creating a new request context. Also needed to bypass context validation (fx: updates and delete require primary keys, which are irrelevant to sp executes).
> 
> Finally, we dispatch to 
> 1. `SqlQueryEngine` if the entity is a stored procedure and `OperationType` is Find.
> 2. `SqlMutationEngine` if the entity is a sp and `OperationType` is any of Insert, Delete, Update, UpdateIncremental, Upsert, UpsertIncremental

### StoredProcedureRequestContext.cs

> Will inherit from `RestRequestContext`. Needs to at least implement/expose `FieldValuePairsInBody` for accessing parameters in `POST`, `PUT`, `PATCH`, `DELETE` requests. Query parameters will be present in base class anyway for `GET` requests. Conditioned on in `SqlQueryEngine` and `SqlMutationEngine` to build appropriate structure.


### SqlQueryEngine.cs

> In `ExecuteAsync(RestRequestContext context)`, check the type of the context; if stored proc, initialize the `SqlExecuteStructure`, else go ahead with the `SqlQueryStructure`. Then, either refactor the `ExecuteAsync(SqlQueryStructure structure)` to instead take the superclass `BaseSqlQueryStructure` or just create a new `ExecuteAsync(SqlExecuteStructure structure)` method. The latter is clearer but results in more code duplication.

### SqlMutationEngine.cs

> Refactor `ExecuteAsync(RestRequestContext context)` and `PerformMutationOperation()` methods so that we can pass the whole context, not just the `OperationType` to the `PerformMutationOperation` method and conditionally build the `SqlExecuteStructure`. Potentially many other changes will be needed in the `ExecuteAsync` method to handle error conditions depending on the `OperationType` (the result set of executing a stored procedure likely won't follow the format the code is expecting for a delete result, for example, which may throw incorrect errors).

### Misc - not yet on the todo list
- Include error handling config source object in metadata provider, e.g. if a key in the parameters dictionary not present as a parameter on the stored procedure found in the schema
- Parameter resolution between those fixed in config and request context. As of now just gonna prefer the request context.
