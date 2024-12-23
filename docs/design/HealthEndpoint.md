# Design Document: DAB Health Endpoint
## Objective:
The objective of this task is to enhance the "Health Endpoint" for DAB. Currently we only show whether the DAB Engine is healthy or not and which version of DAB is running. However, the objective under this task item is to enhance this to support delatiled description of each supported (enabled) endpoint with all different kinds of parameters that would be defined below.

## Need for DAB Health Check Endpoint
Azure App Service & Azure Kubernetes Service (AKS) support health probes to monitor the health of your application. If a service fails health checks, Azure can automatically restart it or redirect traffic to healthy instances.

Similarly, we need Health Endpoint for Data API builder because if it fails health checks in a way a customer deems past a threshold, they have the option to recycle the container or send an alert to direct engineers.

## Current Setup
There is no official industry standard for the health endpoint. /health or variations like /_health are common by convention. 
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
We want to create a detailed version of the DAB Engine Health endpoint with information regarding all REST and GraphQL endpoints and their behaivour. Here we would update the configuration schema to include what all results or checks do we want to perform on that runtime/data-source/entity (described below).
We want the user to provide a check and threshold value what DAB would carry out and then check if the result is under the threshold, if so, the DAB Engine would be considered healthy for that endpoint, else unhealthy. 

> For Example, if the data source health is being performed and the user mentions that we would do a GET of all resources from the DB and this should be under 25sec. This check would be given as a SQL Query that the users wants to execute on the DB, then after performing this check, we will validate if the condition is satisfied, else this data source moniker would be considered unhealthy.


## Implementation Details
### Schema
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

The runtime configuration would include details like cache-ttl in case we need to cache the response of health checks, the max-drop value which tells you the degree of parallelism i.e. how many queries that DAB shoud run at once to get health results and roles i.e. which role is allowed to view the health information of DAB.

| **Property**   | **Data Type** | **Required** | **Default** | **Description**                                                                                      |
|----------------|---------------|--------------|-------------|------------------------------------------------------------------------------------------------------|
| `enabled`     | Boolean       | No           | `true`      | Enables or disables health checks at the runtime level.                                             |
| `cache-ttl`   | Integer       | No           | `5`         | Time-to-live (in seconds) for caching health check results.                                          |
| `max-dop`     | Integer       | No           | `1`         | Maximum Degree of Parallelism for running health checks.                                             |
| `roles`       | Array         | No           | `*`         | Roles allowed to access the health endpoint (e.g., `anonymous`, `authenticated`).                   |

#### `data-source.health` Configuration

The data source config parameters specify the query that we should run on the data source and the threshold of ms if should come under to qualify as a healthy data source for DAB.

| **Property**      | **Data Type** | **Required** | **Default** | **Description**                                                                                      |
|-------------------|---------------|--------------|-------------|------------------------------------------------------------------------------------------------------|
| `moniker`         | String        | No           | `NULL`      | Identifier for the data source; useful when multiple data sources exist.                            |
| `enabled`         | Boolean       | No           | `true`      | Enables or disables health checks for the data source.                                              |
| `query`           | String        | No           | N/A         | Custom SQL query used to perform the health check.                                                  |
| `threshold-ms`    | Integer       | No           | `10000`     | Threshold in milliseconds for the query response time before the check is considered degraded.      |

#### `<entity-name>.health` Configuration

The Entity config parameters contain information about the GET filter or query which needs to be carried out on the data source entiy and under what threshold should the response be received for it to qualify as a healthy entity.
| **Property**     | **Data Type** | **Required** | **Default** | **Description**                                                                                      |
|------------------|---------------|--------------|-------------|------------------------------------------------------------------------------------------------------|
| `enabled`        | Boolean       | No           | `true`      | Enables or disables health checks for the specific entity.                                          |
| `filter`         | String        | No           | `null`      | Filter condition applied to the health check query (e.g., `"Id eq 1"`).                             |
| `first`          | Integer       | No           | `1`         | Number of records to query during the health check.                                                 |
| `threshold-ms`   | Integer       | No           | `10000`     | Threshold in milliseconds for the query response time before the check is considered degraded.      |

#### Example
```
{
  "runtime" : {
    "health" : {
      "enabled": true, (default: true)
      "cache-ttl": 5, (optional default: 5)
      "max-dop": 5, (optional default: 1)
      "roles": ["anonymous", "authenticated"] (optional default: *)
    }
  }
}
{
  "data-source" : {
    "health" : {
      "moniker": "sqlserver", (optional default: NULL) // not required, on purpose, most have just one
      "enabled": true, (default: true)
      "query": "SELECT TOP 1 1", (option)
      "threshold-ms": 100 (optional default: 10000)
    }
  }
}
{
  "<entity-name>": {
      "health": {
        "enabled": true, (default: true)
        "filter": "Id eq 1" (optional default: null),
        "first": 1 (optional default: 1),
        "threshold-ms": 100 (optional default: 10000)
      },
      ...
    },
  }
}
```
The idea of using this updates configuration is to allow the developer to influence how the checks work against the datasource/entity. This would provide him with a more detailed process for checking if DAB engine is healthy and would give him an enhanced user experience. 


### Output Sample
```
{
  "status": "Unhealthy",
  "status": "Healthy",
  "version": "1.2.10",
  "app-name": "dab_oss_1.2.10",
  "dab-configuration": {
    "http": true,
    "https": true,
    "rest": true,
    "graphql": true,
    "telemetry": true,
    "caching": true,
    "mode": "development",
    "dab-configs": [
      "/App/dab-config.json ({data-source-moniker})",
      "/App/dab-config-2.json ({data-source-moniker})"
    ],
    "dab-schemas": [
      "/App/schema.json"
    ]
  },
  "checks": {
    "database-moniker" : {
        "status": "Healthy",
        "tags": ["database", "performance"]
        "description": "Checks if the database is responding within an acceptable timeframe.",
        "data": {
            "responseTimeMs": 10,
            "maxAllowedResponseTimeMs": 10
        }
    },
    "database-moniker" : {
        "status": "Unhealthy",
        "tags": ["database", "performance"],
        "description": "Checks if the database is responding within an acceptable timeframe.",
        "data": {
            "responseTimeMs": 20,
            "maxAllowedResponseTimeMs": 10
        }
    },
    "database-moniker" : {
        "status": "Unhealthy",
        "tags": ["database", "performance"]
        "description": "Checks if the database is responding within an acceptable timeframe.",
        "data": { 
            "responseTimeMs": NULL,
            "maxAllowedResponseTimeMs": 10
        },
        "exception": "TimeoutException: Database query timed out."
    },
    "<entity-name>": {
      "status": "Healthy",
      "description": "Checks if the endpoint is responding within an acceptable timeframe.",
      "tags": ["endpoint", "performance"]
      "data": {
          "responseTimeMs": 10,
          "maxAllowedResponseTimeMs": 10
      }
   },
    "<entity-name>": {
      "status": "Unhealthy",
      "description": "Checks if the endpoint is responding within an acceptable timeframe.",
      "tags": ["endpoint", "performance"]
      "data": {
          "responseTimeMs": 20,
          "maxAllowedResponseTimeMs": 10
      }
   },
    "<entity-name>": {
      "status": "Unhealthy",
      "description": "Checks if the endpoint is responding within an acceptable timeframe.",
      "tags": ["endpoint", "performance"]
      "data": {
          "responseTimeMs": 20,
          "maxAllowedResponseTimeMs": 10
      }
      "exception": "{exception-message-here}"
   },
  }
}
```