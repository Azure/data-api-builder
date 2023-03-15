# About `dab` CLI

The Data API builder CLI (**dab CLI** or `dab`) is a command line tool that streamlines the local development experience for applications using Data API builder. 

## Key Features of `dab` CLI

- Initialize the configuration file for REST and GraphQL endpoints
- Add new entities
- Update entity details
- Add/update entity relationships
- Configure roles and their permissions
- Configure cross-origin requests (CORS)
- Run the Data API builder engine

## CLI command line 

DAB CLI comes with an integrated help system. To get a list of what commands are available, use the `--help` option on the `dab` command.

```shell
dab --help
```

To get help on a specific command, use the `--help` option. For example, to learn more about the `init` command:

```shell
dab init --help
```

## CLI command line verbs and options

### **`init`**
Initializes the runtime configuration for the Data API builder runtime engine. It will create a new JSON file with the properties provided as options. 

**Syntax:** `dab init [options]`

**Example:** `dab init --config "dab-config.MsSql.json" --database-type mssql --connection-string "Server=tcp:127.0.0.1,1433;User ID=sa;Password=REPLACEME;Connection Timeout=5;"`

| Options | Required    | Default Value    | Description |
| :---   | :--- | :--- | :--- |
| **--database-type** | true   | -   | Type of database to connect. Supported values: mssql, cosmosdb_nosql, cosmosdb_postgresql, mysql, postgresql   |
| **--connection-string** | false   | ""   | Connection details to connect to the database.   |
| **--cosmosdb_nosql-database** | true when databaseType=cosmosdb_nosql   | -   | Database name for Cosmos DB for NoSql.   |
| **--cosmosdb_nosql-container** | false   | -   | Container name for Cosmos DB for NoSql.   |
| **--graphql-schema** | true when databaseType=cosmosdb_nosql   | -   | GraphQL schema Path   |
| **--set-session-context** | false   | false   | Enable sending data to MsSql using session context.   |
| **--host-mode** | false   | production   | Specify the Host mode - development or production   |
| **--cors-origin** | false   | ""   | Specify the list of allowed origins.   |
| **--auth.provider** | false   | StaticWebApps   | Specify the Identity Provider.   |
| **--rest.path** | false   | /api   | Specify the REST endpoint's default prefix.   |
| **--auth.audience** | false   | -   | Identifies the recipients that the JWT is intended for.   |
| **--auth.issuer** | false   | -   | Specify the party that issued the JWT token.   |
| **-c, --config** | false   | dab-config.json   | Path to config file.   |
 

### **`add`**
Add new database entity to the configuration file. Make sure you already have a configuration file before executing this command, otherwise it will return an error.

**Syntax**: `dab add [entity-name] [options]`

**Example:**: `dab add Book -c "dab-config.MsSql.json" --source dbo.books --permissions "anonymous:*"`

| Options | Required    | Default Value    | Description |
| :---   | :--- | :--- | :--- |
| **-s, --source** | true   | -   | Name of the source table or container.   |
| **--permissions** | true   | -   | Permissions required to access the source table or container. Format "[role]:[actions]"   |
| **--source.type** | false   | table   | Type of the database object.Must be one of: [table, view, stored-procedure]   |
| **--source.params** | false   | -   | Dictionary of parameters and their values for Source object."param1:val1,param2:value2,.." for Stored-Procedures.   |
| **--source.key-fields** | false   | -   | The field(s) to be used as primary keys for tables and views only. Comma separated values. Example `--source.key-fields "id,name,type"`  |
| **--rest** | false   | case sensitive entity name.  | Route for REST API. Example:<br/> `--rest: false` -> Disables REST API  calls for this entity.<br/> `--rest: true` -> Entity name becomes the rest path.<br/> `--rest: "customPathName"` -> Provided customPathName becomes the REST path.|
| **--rest.methods** | false   | post   | HTTP actions to be supported for stored procedure. Specify the actions as a comma separated list. Valid HTTP actions are :[get, post, put, patch, delete]   |
| **--graphql** | false   | case sensitive entity name  | Entity type exposed for GraphQL. Example:<br/> `--graphql: false` -> disales graphql calls for this entity.<br/> `--graphql: true` -> Exposes the entity for GraphQL with default names. The singular form of the entity name will be considered for the query and mutation names.<br/> `--graphql: "customQueryName"` -> Lets the user customize the singular and plural name for queries and mutations. |
| **--graphql.operation** | false   | mutation   | GraphQL operation to be supported for stored procedure. Valid operations are : [query, mutation]  |
| **--fields.include** | false   | -   | Fields that are allowed access to permission.  |
| **--fields.exclude** | false   | -   | Fields that are excluded from the action lists.   |
| **--policy-database** | false   | -   | Specify an OData style filter rule that will be injected in the query sent to the database.  |
| **-c, --config** | false   | dab-config.json   | Path to config file.   |


### **`update`**
Update the properties of any database entity in the configuration file.

**Syntax**: `dab update [entity-name] [options]`

**Example:** `dab update Publisher --permissions "authenticated:*"`

**NOTE:** `dab update` supports all the options that are supported by `dab add`. Additionally, it also supports the below listed options.

| Options | Required    | Default Value    | Description |
| :---   | :--- | :--- | :--- |
| **--relationship** | false   | -   | Specify relationship between two entities. Provide the name of the relationship.   |
| **--cardinality** | true when `--relationship` option is used   | -   | Specify cardinality between two entities. Could be one or many.   |
| **--target.entity** | true when `--relationship` option is used   | -   | Another exposed entity to which the source entity relates to.  |
| **--linking.object** | false   | -   | Database object that is used to support an M:N relationship.   |
| **--linking.source.fields** | false   | -   | Database fields in the linking object to connect to the related item in the source entity. Comma separated fields.   |
| **--linking.target.fields** | false   | -   | Database fields in the linking object to connect to the related item in the target entity. Comma separated fields.  |
| **--relationship.fields** | false   | -   | Specify fields to be used for mapping the entities. Example: `--relationship.fields "id:book_id"`. Here `id` represents column from sourceEntity, while `book_id` from targetEntity. Foreign keys are required between the underlying sources if not specified.  |
| **-m, --map** | false   | -   | Specify mappings between database fields and GraphQL and REST fields. format: --map "backendName1:exposedName1,backendName2:exposedName2,...".   |

### **`start`**
Start the runtime engine with the provided configuration file for serving REST and GraphQL requests.

**Syntax**: `dab start [options]`

**Example**: `dab start`

| Options | Required    | Default Value    | Description |
| :---   | :--- | :--- | :--- |
| **--verbose** | false   | -   | Specify logging level as informational.   |
| **--LogLevel** | false   | Debug when hostMode=development, else Error when HostMode=Production   | Specify logging level as provided value. example: debug, error, information, etc.   |
| **--no-https-redirect** | false   | false   | Disables automatic https redirects.   |
| **-c, --config** | false   | dab-config.json   | Path to config file.   |

**NOTE:** 
1. One cannot have both verbose and LogLevel.
2. To know more about different logging levels, see: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.logging.loglevel?view=dotnet-plat-ext-6.0
