# About `dab` CLI

The Data API builder CLI (**dab CLI** or `dab`) is a command line tool that streamlines the local development experience for applications using Data API builder.

- Find the source code here: [Cli](../src/Cli)
- Getting started: [Getting started](./getting-started/getting-started-dab-cli.md)

## Key Features of `dab` CLI

- Initialize the configuration file for REST and GraphQL endpoints
- Add new entities
- Update entity details
- Add/update entity relationships
- Configure roles and their permissions
- Configure cross-origin requests (CORS)
- Run the Data API builder engine

## Contributing to the CLI

Your feedback and contributions are key to its success.
[Build from Source](../CONTRIBUTING.md)

## CLI command line verbs and options

### **`init`**
To initialize the runtime config for Microsoft Data API builder runtime engine. It will create a new json file with properties provided as options.

**Syntax:** `dab init [options]`

**Example:** `dab init --config "dab-config.MsSql.json" --database-type mssql --connection-string "Server=tcp:127.0.0.1,1433;User ID=sa;Password=REPLACEME;Connection Timeout=5;"`

#### options:
<pre>
--database-type              :Required. Type of database to connect. Supported values: mssql, cosmosdb_nosql,
                              cosmosdb_postgresql, mysql, postgresql

--connection-string          :(Default: '') Connection details to connect to the database.

--cosmosdb_nosql-database    :Database name for Cosmos DB for NoSql.

--cosmosdb_nosql-container   :Container name for Cosmos DB for NoSql.

--graphql-schema             :GraphQL schema Path.

--set-session-context        :(Default: false) Enable sending data to MsSql using session context.

--host-mode                  :(Default: Production) Specify the Host mode - Development or Production

--cors-origin                :Specify the list of allowed origins.

--auth.provider              :(Default: StaticWebApps) Specify the Identity Provider.

--rest.path                  :(Default: /api) Specify the REST endpoint's default prefix.

--auth.audience              :Identifies the recipients that the JWT is intended for.

--auth.issuer                :Specify the party that issued the jwt token.

-c, --config                 :Path to config file. Defaults to 'dab-config.json'.
</pre>


### **`add`**
To add a new Entity to the config. It will fail if the config doesn't exist.

**Syntax**: `dab add [entity-name] [options]`

**Example:**: `dab add Book -c "dab-config.MsSql.json" --source dbo.books --permissions "anonymous:*"`

#### options:
<pre>
-s, --source              :Name of the source table or container.

--permissions             :Permissions required to access the source table or container.

--relationship            :Specify relationship between two entities.

--cardinality             :Specify cardinality between two entities.

--target.entity           :Another exposed entity to which the source entity relates to.

--linking.object          :Database object that is used to support an M:N relationship.

--linking.source.fields   :Database fields in the linking object to connect to the related item in the source entity.

--linking.target.fields   :Database fields in the linking object to connect to the related item in the target entity.

--relationship.fields     :Specify fields to be used for mapping the entities.

-m, --map                 :Specify mappings between database fields and GraphQL and REST fields. format: --map
                           "backendName1:exposedName1,backendName2:exposedName2,...".
                           
--source.type             :Type of the database object.Must be one of: [table, view, stored-procedure]

--source.params           :Dictionary of parameters and their values for Source object."param1:val1,param2:value2,.."

--source.key-fields       :The field(s) to be used as primary keys.

--rest                    :Route for rest api.

--rest.methods            :HTTP actions to be supported for stored procedure. Specify the actions as a comma separated list. Valid HTTP actions are :
                           [GET, POST, PUT, PATCH, DELETE]

--graphql                 :Type of graphQL.

--graphql.operation       :GraphQL operation to be supported for stored procedure. Valid operations are : [Query, Mutation]

--fields.include          :Fields that are allowed access to permission.

--fields.exclude          :Fields that are excluded from the action lists.

--policy-request          :Specify the rule to be checked before sending any request to the database.

--policy-database         :Specify an OData style filter rule that will be injected in the query sent to the database.

-c, --config              :Path to config file. Defaults to 'dab-config.json'.
</pre>


### **`update`**
To update properties of any Entity present in the config.

**Syntax**: `dab update [entity-name] [options]`

**Example:** `dab update Publisher --permissions "authenticated:*"`

#### options:
<pre>
-s, --source              :Name of the source table or container.

--permissions             :Permissions required to access the source table or container.

--relationship            :Specify relationship between two entities.

--cardinality             :Specify cardinality between two entities.

--target.entity           :Another exposed entity to which the source entity relates to.

--linking.object          :Database object that is used to support an M:N relationship.

--linking.source.fields   :Database fields in the linking object to connect to the related item in the source entity.

--linking.target.fields   :Database fields in the linking object to connect to the related item in the target entity.

--relationship.fields     :Specify fields to be used for mapping the entities.

-m, --map                 :Specify mappings between database fields and GraphQL and REST fields. format: --map
                           "backendName1:exposedName1,backendName2:exposedName2,...".

--source.type             :Type of the database object.Must be one of: [table, view, stored-procedure]

--source.params           :Dictionary of parameters and their values for Source object."param1:val1,param2:value2,.."

--source.key-fields       :The field(s) to be used as primary keys.

--rest                    :Route for rest api.

--rest.methods            :HTTP actions to be supported for stored procedure. Specify the actions as a comma separated list. Valid HTTP actions are :
                           [GET, POST, PUT, PATCH, DELETE]

--graphql                 :Type of graphQL.

--graphql.operation       :GraphQL operation to be supported for stored procedure. Valid operations are : [Query, Mutation]

--fields.include          :Fields that are allowed access to permission.

--fields.exclude          :Fields that are excluded from the action lists.

--policy-request          :Specify the rule to be checked before sending any request to the database.

--policy-database         :Specify an OData style filter rule that will be injected in the query sent to the database.

-c, --config              :Path to config file. Defaults to 'dab-config.json'.
</pre>


### **`start`**
To start the runtime engine for serving rest/graphQL requests.

**Syntax**: `dab start [options]`

**Example**: `dab start`

#### options:
<pre>
--verbose             :Specify logging level as informational.

--LogLevel            :Specify logging level as provided value. example: debug, error, information, etc.

--no-https-redirect   :Disables automatic https redirects.

-c, --config          :Path to config file. Defaults to 'dab-config.json'.
</pre>

**NOTE:** 
1. asdsda
2.  To Know more about different Logging levels, see: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.logging.loglevel?view=dotnet-plat-ext-7.0
