# Stored Procedure Design Doc

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
<a id='sample-source'></a>
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
- For GraphQL, the stored-procedure will look something like this passed in the body
```
{
    GetBooks(param1:value, param2:value) {}
}
```

> **Spec Interpretation**: 
> - since not explicitly stated in the specification, request body for GET will be ignored altogether.
> - Parameter resolution will go as follows, from highest to lowest priority: request (query string or body) > config defaults > <s> sql defaults </s>
>   - NOTE: sql defaults not so easy to infer for parameters, so we explicitly require all parameters to be provided either in the request or config in this first version.
> - GRAPHQL
> - if the request doesn't contain the parameter values, default values from the config will be picked up.

### Stored Procedure Permissions 

Stored procedures have identical role/action permissions to any other entity. I.e. same familiar format:
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
However, the behavior of **column/field-level permissions** and **database policies** have not yet been designed/defined for procedures; as such, these will be **ignored**.

<a id='suggested-config'></a>
> Why not simplify stored procedure permissions, if POST, PUT, PATCH, DELETE semantically identical?

Justification **against** supporting all CRUD operations:
- `POST` request is the only HTTP method that specifies a client can "submit a command" per [Microsoft API Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/Guidelines.md#741-post)
- Other solutions usually limit to `POST`: https://learn.microsoft.com/rest/api/cosmos-db/execute-a-stored-procedure
    - [PostgREST](https://postgrest.org/en/stable/api.html#stored-procedures) support `POST` and `GET` if marked `IMMUTABLE` or `STABLE`
- Proposed permission:
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
Justification **for** allowing permission configuration for all CRUD operations:
- Davide: the "simplification" would complicate authorization with no real benefit. True in that the authorization logic would need to change conditioned on whether the entity source was a stored procedure.
- Aniruddh: we should leave the responsibility for the developer to properly configure hawaii; it's not our fault if they configure against the API guidelines 

**Conclusion**: we treat stored procedures as any other entity when it comes to CRUD support and role/action AuthZ.
## Implementation Overview

Implementation was segmented into 5 main sections:
### 1.  Support in Config

> ### `Entity.cs`
> - Exposing the `DatabaseObjectSource` object rather than `source` being a simple string.
> - Stored procedures must be specified as "stored-procedure" source type
>     - See [sample source config](#sample-source)
> - Unfortunately, System.Text.Json does not natively support Kebab-casing for converting hyphenated strings ("stored-procedure") to a CamelCase enum (StoredProcedure). As such, Newtonsoft is used for deserialization. If we want to migrate to System.Text.Json, we either need to change the spec and only accept non-hyphenated strings or write our own custom string-to-enum converter, which was out of scope for v1.
> GRAPHQL
> - No change required here.

### 2.  Metadata Validation

> ### `DatabaseObject.cs`
> - Tables and views have metadata that does not apply to stored procedures, most notably Columns, Primary Keys, and Relationships.
> - As such, we create a `StoredProcedureDefinition` class to hold procedure-relevant info - i.e. procedure parameter metadata. 
>     - We also add an `ObjectType` attribute on a `DatabaseObject` for more robust checking of whether it represents a table, view, or stored procedure vs. just null-checking the `TableDefinition` and `StoredProcedureDefinition` attributes. 
>     - The `StoredProcedureDefinition` class houses a Dictionary, `Parameters`, of strings to `ParameterDefinition`s, where the strings are procedure parameter names as defined in sql. 
>     - `ParameterDefinition` class houses all useful parameter metadata, such as its data type in sql, corresponding CLR/.NET data type, whether it has a default value specified in config, and the default value config defines. It also holds parameter type (IN/OUT), but this isn't currently being used.

<br/>

> ### `RuntimeConfigValidator.cs`
> - `ValidateEntitiesDoNotGenerateDuplicateQueries` should skip check for Stored-Procedures.


> ### `SchemaConverter.cs` and `GraphQLSchemaCreator.cs`
> - Generate a GraphQL object type from a SQL table/view/stored-procedure definition, combined with the runtime config entity information

> ### `SqlMetadataProvider.cs`
> - Problem: when we draw metadata from the database schema, we implicitly check if each entity specified in config exists in the database. Path: in `Startup.cs`, the `PerformOnConfigChangeAsync()` method invokes `InitializeAsync()` of the metadata provider bound at runtime, which then invokes `PopulateTableDefinitionForEntities()`. Several steps down the stack `FillSchemaForTableAsync()` is called, which performs a `SELECT * FROM {table_name}` for the given entity and adds the resulting `DataTable` object to the `EntitiesDataSet` class variable. Unfortunately, stored procedure metadata cannot be queried using the same syntax. As such, running the select statement on a stored procedure source name will cause a Sql exception to surface and result in runtime initialization failure.
> - Instead, we introduce the `PopulateStoredProcedureDefinitionForEntities()` method, which iterates over only entites labeled as stored procedures to fill their metadata, and we simply skip over entities labeled as Stored Procedures in the `PopulateTableDefinitionForEntities()` method.
> - We use the ADO.NET `GetSchemaAsync` method to retrieve procedure metadata and parameter metadata from the Procedures and Procedure Parameters collections respectively: https://learn.microsoft.com/dotnet/framework/data/adonet/sql-server-schema-collections. These collections are listed as SQL Server-specific, so this implementation might not be extensible to MySQL and Postgres.
>     - In this case, we should just directly access the ANSI-standard information_schema and retrieve metadata using:
>        - `SELECT * FROM INFORMATION_SCHEMA.ROUTINES WHERE SPECIFIC_NAME = {procedure_name}`
>        - `SELECT * FROM INFORMATION_SCHEMA.PARAMETERS WHERE SPECIFIC_NAME = {procedure_name}`
> - Using metadata, we first verify procedures specified in config are present in the database. Then, we verify all parameters for each procedure specified in config are actually present in the database, otherwise we fail runtime initialization. We also verify that any parameters specified in config match the type retrieved from the parameter metadata. 
>     - TODO: more logic is needed for determining whether a value specified in config can be parsed into the metadata type. For example, providing a string in config might be parse-able into a datetime value, but right now it will be rejected. We should relax this constraint at runtime initialization and potentially fall back to request-time validation.

<br/>

> ### `MsSqlMetadataProvider.cs`, `MySqlMetadataProvider.cs`, & `PostgreSqlMetadataProvider.cs`
> - Added overriden method to map Sql data type returned from metadata into the CLR/.NET type equivalent. Used/necessary for metadata parsing in `FillSchemaForStoredProcedureAsync()` in `SqlMetadataProvider`.
> - Left as TODOs in MySql and Postgres.

### 3.  Request Context + Validation

> ### `RestRequestContext.cs`
> - Since multiple derived classes are implementing/duplicating logic for populating their `FieldValuePairsInBody` dictionary with the Json request body, moved that logic into a method in this class, `PopulateFieldValuePairsInBody`.
> - Added `DispatchExecute(IQueryEngine)` and `DispatchExecute(IMutationEngine)` as virtual methods, as an implementation of the visitor pattern/double dispatch strategy. Helps prevent the sometimes-bad practice of downcasting in calling the overloaded `ExecuteAsync` methods in the query and mutation engines (between `FindRequestContext` and `StoredProcedureRequestContext` in the query engine, for example).

<br/>

> ### `StoredProcedureRequestContext.cs`
> - Since it was agreed not to change the Operation enum, as we are keeping the same general authorization logic, we need a way to conditionally split constructing and building the appropriate query structure in the query and mutation engine. The best way I found to do so was introduce a request context representing a stored procedure request for both Query and Mutation scenarios. 
> - Populates the request body on construction.
> - Contains `PopulateResolvedParameters` method to populate its `ResolvedParameters` dictionary, which houses the final, resolved parameters that will be passed in constructing the sql query. Populates this dictionary with the query string or request body depending on the `OperationType` field. Should only be called after `ParsedQueryString` and/or `FieldValuePairsInBody` are appropriately populated.
> - Overrides `DispatchExecute` methods

<br/>

> ### `RestService.cs`
> - Condition in `ExecuteAsync` based on the database object type whether the entity type requested is a stored procedure.
> - If so, initialize a `StoredProcedureRequestContext`. 
>     - If we have a read operation, parse the query string into `ParsedQueryString`. Note: for stored procedures, ODataFilters do not apply for this iteration. As such, keys like `$filter` and `$select` will be treated as any other parameters. Request body is ignored, as we pass in `null` to constructor.
>     - For all other operations, throw an exception if query string is non-empty, and pass the json request body to the constructor. 
> - Validates with two method additions to `RequestValidator`:
>     - `ValidateStoredProcedureRequest`: throws bad request if primary key route is non-empty
>     - `ValidateStoredProcedureRequestContext`: throws bad request if
>       - there are extraneous parameters in the request
>       - there were missing parameters in the request and no default was found in config
> - Condition to avoid the `ColumnsPermissionsRequirement` AuthZ check, since specification hasn't defined what this would look like yet 

### 4.  Structure + Query Building

> ### `SqlExecuteStructure.cs`
> - Contains all needed info to build an `EXECUTE {stored_proc_name} {parameters}` query
> - Contains a dictionary, `ProcedureParameters`, mapping stored procedure parameter names to engine-generated parameterized values. I.e. keys are the user-defined procedure parameters (@<!-- -->id), and values are @<!-- -->param0, @<!-- -->param1...
> - Constructor populates `ProcedureParameters` dictionary and the base class's `Parameters` dictionary mapping parameterized values to the procedure parameter values. Confusing, I know.
> - Request validation should ensure all parameters in metadata are either in the request or config, and `ProcedureParameters` gets populated by first checking the request and then defaulting to the config for each metadata parameter. On parameterizing, we try to parse each provided parameter as the `SystemType` for each parameter as defined in metadata. Thus, this is actually where we do type checking, rather than in request validation, to avoid code duplication. 

<br/>

> ### `QueryBuilder.cs`
> - It should not generate Both FindAll and FindByPK query for Stored-Procedure.

> ### `MsSqlQueryBuilder.cs`
> - Added the `Build(SqlExecuteStructure)` that builds the query string for execute requests as `EXECUTE {schema_name}.{stored_proc_name} {parameters}`
> - Added `BuildProcedureParameterList` to build the list of parameters from the `ProcedureParameters` dictionary. The result of this method might look like `@id = @param0, @title = @param1`.
>     - Found the naive string concatenation implementation faster than StringBuilder or Linq + string.Join

<br/>

> ### `MySqlQueryBuilder.cs` & `PostgresQueryBuilder.cs`
> - Added method stubs as TODOs for `Build(SqlExecuteStructure)`

### 5.  Query Execution + Result Formatting

### `SqlQueryEngine.cs`
> - Separated `ExecuteAsync(RestRequestContext)` into `ExecuteAsync(FindRequestContext)` and `ExecuteAsync(StoredProcedureRequestContext)`. Seems to be better practice than doing type checking and conditional downcasting.
> - `ExecuteAsync(StoredProcedureRequestContext)` initializes the `SqlExecuteStructure` with the context's resolved parameters, and calls a new `ExecuteAsync(SqlExecuteStructure)` method. Result returned simply as an `OkResponse` instead of using the `FormatFindResult` method. The pagination methods of `FormatFindResult` don't play well with stored procedures at the moment, since it relies on fields from a `TableDefinition`, but pagination can and should be added to a future version.
>     - The response from this method will always be a `200 OK` if there were no database errors. If there was no result set returned, we will return an empty json array. **NOTE: Only the first result set returned by the procedure is considered.**
> - `ExecuteAsync(SqlExecuteStructure)` is needed because **we have no guarantee the result set is JSON**. As such, we can't use the same strategy that `ExecuteAsync(SqlQueryStructure)` does in using `GetJsonStringFromDbReader`. Instead, we do basically identical fetching as the mutation engine does in using `ExtractRowFromDbDataReader`. As such, one could argue that stored procedures could entirely be the responsibility of the mutation engine.

<br/>

### `SqlMutationEngine.cs`

> - Instead of trying to refactor `ExecuteAsync(RestRequestContext)` and `PerformMutationOperation`, which conditionally initializes query structures and builds them based on context `OperationType` - which is not a distinguishing factor of a stored procedure request - I found it easier and clearer to add an overloaded `ExecuteAsync(StoredProcedureRequestContext)`, which will be responsible for all stored procedure mutation requests (POST, PUT, PATCH, DELETE). 
> - In `ExecuteAsync(StoredProcedureRequestContext)`, we initialize the structure and build it the same way as in the query engine. The difference comes in formatting the response. We're leaving it up to the developers to configure their procedures appropriately and to call them with the appropriate HTTP verb. As such, we will return the expected response for each successful request based on its Operation.
>    - Delete request with no database error returns a `204 No Content` response
>        - Even a stored procedure that returns the results of a SELECT statement will return an empty `204 No Content` response.
>    - Insert request returns `201 Created` with **first result set** as json response. If none/empty result set, an empty array is returned. Discussion: prefer to instead return no json at all?
>    - Update/upsert behaves same as insert but with `200 OK` response.

## TODO
1. MySql/Postgres support - changes really should be minimal. Foundation is already laid, just may need minor updates to metadata and then obviously adding `Build` methods in respective query builders. 
2. Ease up type checking of parameters specified in config. Try to parse them instead of just doing direct type equality check in `SqlMetadataProvider`.
3. Iron out whether sorting/filtering/paginating will be supported.
4. Discuss whether column/field level AuthZ and database policies make sense for stored procedures.
5. If possible, check metadata to see if procedure parameters have a default set. This won't be so easy - will need to parse the object definition manually, at least for SQL Server.
