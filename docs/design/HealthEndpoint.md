# Design Document: DAB Health Endpoint
## Objective:
The objective of this task is to enhance the "Health Endpoint" for DAB. Currently we only show whether the DAB Engine is healthy or not with a small description of the version.\
However, the objective under this task item is to enhance the health endpoint to support detailed description of each enabled entity by performing health checks on them based on different kinds of parameters that would be defined below.

## Need for DAB Health Check Endpoint
Azure App Service & Azure Kubernetes Service (AKS) support health probes to monitor the health of your application. If a service fails health checks, Azure can automatically restart it or redirect traffic to healthy instances.\
Similarly, we need Health Endpoint for Data API builder because if it fails health checks in a way a customer deems past a threshold, they have the option to recycle the container or send an alert to direct engineers.

## Current Setup
There is no official industry standard for the health endpoint. /health or variations like /_health are common by convention.\
Currently, the DAB application uses a builtin class in ASP.NET Core i.e. `Microsoft.Extensions.Diagnostics.HealthChecks` library for generating DAB Health check report. We use `HealthReportResponseWrite` class that describes the `HealthReport` object including `HealthReportEntry` dictionary. The object `DabHealthCheck` includes a description of version (Major.Minor.Patch) and app name (dab_oss_Major.Minor.Patch). Finally it generates a `HealthCheckResult` such as the following one.
```
{
    "status": "Healthy",
    "version": "Major.Minor.Patch",
    "appName": "dab_oss_Major.Minor.Patch"
}
```

This is generated in the `Startup.cs` file when mapping the base URL `(/)` to the health check endpoint to get the high level result for DAB Engine Health using this command `ResponseWriter = app.ApplicationServices.GetRequiredService<HealthReportResponseWriter>().WriteResponse`.

| **Term**           | **Description**                                         |
|--------------------|---------------------------------------------------------|
| **Health Endpoint** | The URL (e.g., `/`) exposed as JSON.             |
| **Check**           | A specific diagnostic test (e.g., database, API).      |
| **Status**          | The result of a check.                                 |
| **Status.Healthy**  | The system is functioning correctly.                   |
| **Status.Unhealthy**| The system has a critical failure or issue.            |

## Proposed Change
We want to create a detailed version of the `Health endpoint for DAB Engine` with information regarding all REST and GraphQL endpoints and their behavior. Here we would update the configuration schema to include some parameters which would be required from the user to perform all checks on that data-source/entity (described below) to validate if they are healthy.
We want the user to provide a check and threshold value for each for each entity for which health is enabled and DAB engine would carry out this check. If the result is under the threshold, the DAB Engine would be considered `healthy` for that endpoint, else `unhealthy`. 

**Case 1: Datasource**\
Here we would carry out standard queries based on the datasource type and execute them under the given threshold. If the engine gets the result below the specified threshold, the DB is considered healthy else unhealthy.

> Example: If we have a SQL Server, we would execute the standard query given below in the document for SQL Server. If the elapsed time for this query is under the given threshold, the data source would be considered healthy.


**Case 2: Entity**\
In case of entities we would ask the user to provide a number in the config as the "first" parameter which would specify the number of items to return in the query (Default is 100). We would use this number to form REST and GraphQL queries and hit our DAB endpoints to check their results.

> Example: In case for entity `UserTable` we say that we need to run a health query with first = 5 under 100ms threshold. While running the DAB Engine we would execute the REST endpoint for `UserTable` and fetch the fist 5 rows and similar for GraphQL and check if the threshold is under 100ms. If so, it would be consider healthy entity.

## Implementation Details
### High-Level Schema
Health check responses follow a common convention rather than a strict standard. The typical pattern involves a "checks" property for individual components' statuses (e.g., database, memory), with each status rolling up to an overall "status" at the top level.
```
{
  "status": "Healthy",
  "checks": {
    "check-name": { "status": "Healthy" },
    "check-name": { "status": "Healthy" }
  }
}
```

### Configuration Updates
We need to update the dab config file to include details of the health check for different configuration parameters like runtime, data source, entities. 

#### `runtime.health` Configuration

The runtime configuration would include details like `cache-ttl-sec` in case we need to cache the response of health checks, the `max-dop` value which specifies the degree of parallelism i.e. how many queries that DAB should run at once to get health results and `roles` i.e. which role is allowed to view the health information of DAB.

| **Property**   | **Data Type** | **Required** | **Default** | **Description**                                                                                      |
|----------------|---------------|--------------|-------------|------------------------------------------------------------------------------------------------------|
| `enabled`     | Boolean       | No           | `false`      | Enables or disables the comprehensive health checks for DAB Engine. In case of disabled; we show the previous format of health check.                                             |
| `cache-ttl-sec`   | Integer       | No           | `5`         | Time-to-live (in seconds) for caching health check results. If this value is not specified, caching would not be enabled. Currently, caching is not implemented hence, value is considered to be null.                                        |
| `max-dop`     | Integer       | No           | `1`         | Maximum Degree of Parallelism for running health checks.                                             |
| `roles`       | Array of strings         | Yes           | NA         | Roles allowed to access the health endpoint (e.g., `anonymous`, `authenticated`).                   |

#### `data-source.health` Configuration

The database type in the data source health config determine the threshold of ms it should come under to qualify as a healthy data source for DAB. We get the database type from the runtime parameters to get the query to run on the specific DB Type.

 > TODO: Handle Health Endpoint for multiple data-source configs in the upcoming enhancements

| **Property**      | **Data Type** | **Required** | **Default** | **Description**                                                                                      |
|-------------------|---------------|--------------|-------------|------------------------------------------------------------------------------------------------------|
| `enabled`         | Boolean       | No           | `true`      | Enables or disables health checks for the data source.                                              |
| `name`         | Array of String        | No (Yes in case of multiple data source)           | Database Type      | Identifier for the data source; useful when multiple data sources exist.  
| `threshold-ms`    | Integer       | No           | `10000`     | Threshold in milliseconds for the query response time before the check is considered degraded.      |

#### `<entity-name>.health` Configuration

The Entity config parameters contain information about the `first` which defines the number of first rows to be returned when executing a SELECT query on the data source entity and under what threshold ms should the response be received for it to qualify as a healthy entity.
| **Property**     | **Data Type** | **Required** | **Default** | **Description**                                                                                      |
|------------------|---------------|--------------|-------------|------------------------------------------------------------------------------------------------------|
| `enabled`        | Boolean       | No           | `false`      | Enables or disables health checks for the specific entity.                                          |
| `first`          | Integer       | No           | `1`         | Number of records to query during the health check. Value is used to create the SELECT query to fetch records for REST and GraphQL endpoints.                                                 |
| `threshold-ms`   | Integer       | No           | `10000`     | Threshold in milliseconds for the query response time before the check is considered degraded.      |

#### Example
```
{
  "runtime" : {
    ...
    "health" : {
      "enabled": true, (default: false)
      "cache-ttl-sec": 5, (optional; default: null) // Default value would be updated to 5sec once caching is enabled
      "max-dop": 5, (optional; default: 1)
      "roles": ["anonymous", "authenticated"] (required)
    }
  }
}
{
  "data-source" : {
    ...
    "health" : {
      "name": ["mssql"], (optional; default: Database Type) // not required as mostly configs have just one
      "enabled": true, (default: false)
      "threshold-ms": 100 (optional; default: 10000)
    }
  }
}
{
  "<entity-name>": {
      "health": {
        "enabled": true, (default: true)
        "first": 1 (optional; default: 1),
        "threshold-ms": 100 (optional; default: 10000)
      },
      ...
    },
  }
}
```
The idea of using this updated configuration is to allow the developer to influence how the health checks work against the datasource/entity. This would provide them with a more detailed process for checking if DAB engine is healthy and would give them an enhanced user experience. 

## Code Details
In case `runtime.health.enabled` is false/null then we show the user the original health endpoint format at both `'/'` and `'/health'`.\
In case `runtime.health.enabled` is true, then the comprehensive health report is displayed based on the rules and check below.

### Roles
We focus on two aspects in terms of roles for health report. 
+ First is to check the roles present in the `runtime.health.roles` array in the config. We check if the incoming user has access to the comprehensive health check report i.e. they are in this array of allowed roles. For Health report of DB, we only need to check the first condition. 
+ For Health report on entities (rest and graphql), we focus on the second configuration where we check if this incoming role is allowed to perform the `read` query on the DB. For this we focus on the entity section, where the incoming role should be added with `read` permissions. If so, we perform the health check for the entity and report according to results. Else, we show `status: Unhealthy` and `exception: Health could not be check for this entity as it does not have permissions to perform read query`.

> NOTE that BASIC health does NOT include configuration.

### DataSource Health Check
For each database we have two parameters in the health check config parameters. (Enabled and threshold)\
For each database type we execute a standard query to get the results for that DB and check if the time elapsed is under the threshold.
The standard queries that would be run on each Database Type are the following
+ Postgres: SELECT 1 LIMIT 1;
+ MySQL: SELECT 1 LIMIT 1;
+ Cosmos DB: SELECT TOP 1 1 FROM c
+ MS SQL: SELECT TOP 1 1

### Entity Health Check

In the config, for each entity we have three health parameters (Enabled, First and Threshold) 

+ Enabled: This would define whether we need to run the comprehensive health check for this role
+ First: This would be used to create the query for endpoints to fetch first 'x' records for the DB.
+ Threshold: This is used to check the time elapsed to run the above query.

To form the query we run a for loop against the entities in the config file. For each entity, we add two parameters in the checks array of the output. One for REST and second for GraphQL, these calls are differentiated using the Tags array in the output report.\

While running the health checks for entities, we need the BASE URL on which DAB is running which we use to run all rest and graphql queries on our engine. 
> Note: Current implementation we consider `http://localhost:5000` as the base URL. Need to track this, in case different. Everywhere in the document, using `baseUrl` in place of `http://localhost:5000`.

#### Rest Query
We take the base URL which is and append the REST path in the suffix. After this we get baseUrl/api. (Default Rest Path: /api)\
For each entity we have rest as a parameter in the config block as well in case the path of entity is different from its name. We build the entity REST path using `<entity-name>.rest.path ?? entityName`. This means if the path is given use that, else the entity name is the path to append to suffix.
Further our call becomes ` baseUrl/api/usertable`. (Assuming the entity name is UserTable)

Finally we add the query parameters which is the first 'x' values to get the query as `baseUrl/api/usertable?$first=5`. This query is then executed to get the elapsed time and we get whether the REST endpoint of UserTable healthy or not.

#### GraphQL Query

To execute this query we first need to get the schema of that particular entity. Hence for each entity we run the `introspection query` to get the schema which gives the field names which are then used to query the GraphQL endpoint to check the health.
For this we run a **POST** query against the base URL **baseUrl/graphql/** with a Request BODY.\
The CURL Command of that query is 
```
curl --request POST \
  --url baseUrl/graphql \
  --header 'Content-Type: application/json' \
  --header 'User-Agent: insomnia/10.3.0' \
  --data '{
  "operationName": "IntrospectionQuery",
  "query": "query IntrospectionQuery {\n  __schema {\n    queryType {\n      name\n    }\n    mutationType {\n      name\n    }\n    subscriptionType {\n      name\n    }\n    types {\n      ...FullType\n    }\n    directives {\n      name\n      description\n      isRepeatable\n      args {\n        ...InputValue\n      }\n      locations\n    }\n  }\n}\n\nfragment FullType on __Type {\n  kind\n  name\n  description\n  specifiedByURL\n  oneOf\n  fields(includeDeprecated: true) {\n    name\n    description\n    args {\n      ...InputValue\n    }\n    type {\n      ...TypeRef\n    }\n    isDeprecated\n    deprecationReason\n  }\n  inputFields {\n    ...InputValue\n  }\n  interfaces {\n    ...TypeRef\n  }\n  enumValues(includeDeprecated: true) {\n    name\n    description\n    isDeprecated\n    deprecationReason\n  }\n  possibleTypes {\n    ...TypeRef\n  }\n}\n\nfragment InputValue on __InputValue {\n  name\n  description\n  type {\n    ...TypeRef\n  }\n  defaultValue\n}\n\nfragment TypeRef on __Type {\n  kind\n  name\n  ofType {\n    kind\n    name\n    ofType {\n      kind\n      name\n      ofType {\n        kind\n        name\n        ofType {\n          kind\n          name\n          ofType {\n            kind\n            name\n          }\n        }\n      }\n    }\n  }\n}"
}'
```
The above introspection query is formatted at `src/media/introspection-query.graphql`.

**Introspection Query Filter**

This gives us the GraphQL Schema which is deserialized to get the column names.\
Now we need to create a `columnNames` array which contains the list of column names which can be used to create the graphQL query.\
In the output of the above CURL command we get `Data.Schema.Types` array which contains these column names. We loop through this array and match the `type.kind` value with `OBJECT` and `type.name` value with the name of the entity i.e. `UserTable`. (Note: Only one entry satisfies this check)
After identifying that specific entity `type` that satisfies the above conditions, we get the object that contains the entity column names. However an entity can have primitive column types or Object column type which are nested further. These object nested types cannot be used to create the graphql query and hence we need to filter out the primitive data types. To resolve this we loop on `type.fields` array and match it with two conditions. If either of the conditions match, we add it to the column names array.\
For each field we get the `field.type` object 
+ Condition 1: If the `field.type.kind` is `SCALAR` then this is a primitive column name.
+ Condition 2: If the `field.type.kind` is `NON_NULL` then check if `field.type.ofType.kind` is `SCALAR`. 

We add all those column names which satisfies either of these two conditions to the top `columnNames` array and finally use this to create the graphQL query.

**GraphQL Query**\
After getting column names for the entity we create the graphQL query payload 
`query = $"{{{entityName.ToLowerInvariant()}(first: {First}) {{items {{ {string.Join(" ", columnNames)} }}}}}}"
`. We execute a **POST** query against the GraphQL base URL.\
CURL Command
```
curl --request POST \
  --url baseUrl/graphql \
  --header 'Content-Type: application/json' \
  --header 'User-Agent: insomnia/10.3.0' \
  --data '{
	"query": "{UserTable(first: 4) {items { content id }}}"
}'
```
The time it took to execute the above query is the time elapsed for checking the graphQL health.


## Output Sample
After executing the above queries for DB and entities, we create the below sample for comprehensive Health Check Report.
```
{
  "status": "Unhealthy/Healthy",
  "version": "1.2.10",
  "app-name": "dab_oss_1.2.10",
  "dab-configuration": {
    "http": true,
    "https": true,
    "rest": true,
    "graphql": true,
    "telemetry": true,
    "caching": true,
    "mode": "development"
  },
  "checks": {
    {
      "name": "database-name",
      "status": "Healthy",
      "tags": ["data-source", "mssql"],
      "data": {
          "responseTimeMs": 10,
          "maxAllowedResponseTimeMs": 10
      }
    },
    {
      "name": "<entity-name>",
      "status": "Healthy",
      "tags": ["endpoint", "rest"],
      "data": {
          "responseTimeMs": 10,
          "maxAllowedResponseTimeMs": 10
      }
    },
    {
      "name": "<entity-name>",
      "status": "Healthy",
      "tags": ["endpoint", "graphql"]
      "data": {
          "responseTimeMs": 20,
          "maxAllowedResponseTimeMs": 50
      }
    },
    {
      "name": "<entity-name>",
      "status": "Unhealthy",
      "tags": ["endpoint", "graphql"]
      "exception": "{exception-message-here}",
      "data": {
          "responseTimeMs": 20,
          "maxAllowedResponseTimeMs": 10
      }
    },
  }
}
```

## Test Scenarios

Health Check result scenarios for different cases of Health check and GraphQL and Rest entities enabled or disabled.

> '_' means enabled/disabled (doesn't matter)

* Roles (Global Health and Entity Health are both enabled)
  * Runtime Health check parameter, `runtime.health.roles` contains "UserRole". However entity permissions doesn't have read permissions on this entity
    * Health for this entity is displayed with `status: Unhealthy` and `exception: Health could not be check for this entity as health check call does not have permissions to perform read query`. 
  
**Cases where Global or Entity health and REST and GraphQL for that Entity is enabled or disabled**
* Global Health Enabled
  * Global GraphQL Enabled
    * Entity health ENABLED and Entity GraphQL ENABLED : Health is shown for this particular entity with `status: Healthy`.
    * Entity health DISABLED and Entity GraphQL ENABLED : GraphQL Health check `omitted` for this entity
    * Entity health _ and Entity GraphQL DISABLED : GraphQL Health check shows `status: Unhealthy` and `exception: Health could not be check for this entity as it is disabled in config`. 
  
  * Global GraphQL Disabled
    * Entity health _ and Entity GraphQL _ : GraphQL Health checks are `omitted` from Health Report.
    
  * Global REST Enabled
    * Entity health ENABLED and Entity REST ENABLED : Health is shown for this particular entity with `status: Healthy`.
    * Entity health DISABLED and Entity REST ENABLED : REST Health check `omitted` for this entity
    * Entity health _ and Entity REST DISABLED : REST Health shows check `status: Unhealthy` and `exception: Health could not be check for this entity as it is disabled in config`. 
  
  * Global REST Disabled
    * Entity health _ and Entity REST _ : REST Health checks are `omitted` from Health Report.

* Global Health Disabled
  * Global GraphQL/REST _
    * Entity health _ and Entity GraphQL/REST _ : Original Health Format Report

## Limitations

+ We do not support health checks for stored procedures.
+ Hot-Reload is not supported in Comprehensive Health Endpoint.
+ Multiple data-source configs are not supported in the HealthCheck Report