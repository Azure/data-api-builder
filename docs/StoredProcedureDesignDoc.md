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

## Implementation Plan (REST)
1. Add field(s) to the `DatabaseObject` config class allowing for stored procedure identification on metadata parsing
2. Add logic in `SqlMetadataProvider` to populate each stored procedure `DatabaseObject` with appropriate required parameters and such.
    - Also include error handling config source object here, e.g. if a key in the parameters dictionary not present as a parameter on the stored procedure found in the schema 

### Closely Following Davide's Spec
3. After request is routed from `RestController` through `HandleOperation`, we condition in `ExecuteAsync` of `RestService` based on the runtimeConfig and/or _metadataProvider whether the entity type requested is a stored procedure.
   - OperationType (find, insert, update, delete, etc.) is being maintained based on Davide's spec, however each OperationType uses its own respective RequestContext. Since stored procedure is semantically identical for `POST`, `PUT`, `PATCH`, `DELETE`, there should be a new `StoredProcedureRequestContext` that includes parsing of the request body (as that's where parameters will be passed for these methods). 
      - To see why we can't use original associated RequestContexts, consider a `DELETE` call to a stored procedure - `DeleteRequestContext` does not parse the body, since normally the body is ignored on `DELETE` requests.
4.  Conditionally ignore the primary key route parsing and validation if database object type is stored procedure.
5.  Only perform AuthZ for checking if role and action are appropriate for this stored procedure
    - Do not try to process DB policy - predicates not allowed for stored procedures
6. Now that we have a request and request context, need to do SQL translation. How?
    - First, we need to add a new query structure, i.e. `SqlExecuteQueryStructure` that may inherit from `BaseSqlQueryStructure`. This structure will contain all fields necessary to build an `EXEC` command
    - Next, we need to add a `Build(SqlExecuteQueryStructure structure)` method to the respective `SqlQueryBuilder` classes that will actually perform the translation
    - Now we need to have an engine/controller initialize the `SqlExecuteQueryStructure` and build it! How?
        1. Route to `SqlQueryEngine` or `SqlMutationEngine` as the OperationType dictates, i.e. `GET` stored procedure requests would route to `SqlQueryEngine` and all else route to `SqlMutationEngine`. Then, each class would condition based on the RequestContext - if it happens to be a `StoredProcedureRequestContext`, create, build, and execute the `SqlExecuteQueryStructure` according to some resolution of the RequestContext and runtimeConfig parameters.
        2. Have a separate `SqlExecutionEngine` that is dedicated to only execute commands. This would involve less code but may be semantically confusing
7. After returning up through the stack trace, we have to return the result set to the user somehow..
   - Does it make sense for a user to be able to send a `DELETE` request to a stored procedure that actually just gets some rows and returns them? What result do we display? Probably should just be the result returned by SQL to avoid confusion?

### Another Route?
AuthZ simplification discussed above and/or limiting request to only `POST` for stored procedures. What would this involve?
- Might still make semantic sense to give stored procedures their own request context for clarity, but it would be identical to `InsertRequestContext`
- Decide on how config would change - just conditionally ignore all CRUD action permissions except create on stored procedures? Or have something like [above](#suggested-config)
- What result gets propagated to client?

### Misc Questions
- should separate OperationType should be defined or just continue to condition on database object type
    - keeping same OperationType aligns more with Davide's spec in that AuthZ mapped actions wouldn't need to change
    - adding a type such as sp_execute allows for more intuitive separation of logic in `RestService` but doesn't fit with HTTP verb logic
