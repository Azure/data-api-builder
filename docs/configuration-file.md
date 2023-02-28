# Configuration File

- [Configuration File](#configuration-file)
  - [Summary](#summary)
  - [Environments Support](#environments-support)
  - [Accessing Environment Variables](#accessing-environment-variables)
  - [File Structure](#file-structure)
    - [Schema](#schema)
    - [Data Source](#data-source)
    - [Runtime global settings](#runtime-global-settings)
      - [REST](#rest)
      - [GraphQL](#graphql)
      - [Host](#host)
    - [Entities](#entities)
      - [GraphQL Settings](#graphql-settings)
        - [GraphQL Type](#graphql-type)
        - [GraphQL Operation](#graphql-operation)
      - [REST Settings](#rest-settings)
        - [REST Path](#rest-path)
        - [REST Methods](#rest-methods)
      - [Database object source](#database-object-source)
      - [Relationships](#relationships)
        - [One-To-Many Relationship](#one-to-many-relationship)
        - [Many-To-One Relationship](#many-to-one-relationship)
        - [Many-To-Many Relationship](#many-to-many-relationship)
      - [Permissions](#permissions)
        - [Roles](#roles)
        - [Actions](#actions)
        - [Fields](#fields)
        - [Policies](#policies)
        - [Limitations](#limitations)
      - [Mappings](#mappings)
      - [Sample Config](#sample-config)

## Summary

Data API builder configuration file contains the information to

+ Define the backend database and the related connection info
+ Define global/runtime configuration
+ Define what entities are exposed
+ Define authentication method
+ Define the security rules needed to access those identities
+ Define name mapping rules
+ Define relationships between entities (if not inferrable from the underlying database)
+ Define specific behavior related to the chosen backend database

using the minimum amount of code.

## Environments Support

Data API builder configuration file will be able to support multiple environments, following the same behavior offered by ASP.NET Core for the `appSettings.json` file, as per: [Default Configuration](https://learn.microsoft.com/aspnet/core/fundamentals/configuration/?view=aspnetcore-6.0#default-configuration). For example:

1. dab-config.json
2. dab-config.Development.json

The environment variable used to set the chosen environment is `DAB_ENVIRONMENT`

> Configuration providers that are added later override previous key settings. For example, if `MyKey` is set in both `dab-config.json` and the environment-specific file, the environment value is used.

## Accessing Environment Variables

To avoid storing sensitive data into the configuration file itself, a developer can use the `@env()` function to access environment data. `env()` can be used anywhere a scalar value is needed. For example:

```json
{
  "connection-string": "@env('my-connection-string')"
}
```

## File Structure

### Schema

The configuration file has a `$schema` property as the first property in the config to explicit the [JSON schema](https://code.visualstudio.com/Docs/languages/json#_json-schemas-and-settings) to be used for validation.

```json
"$schema": "..."
```

From version 0.4.11-alpha schema is available at:

```txt
https://dataapibuilder.azureedge.net/schemas/<VERSION>-<suffix>/dab.draft.schema.json
```

make sure to replace the **VERSION-suffix** placeholder with the version you want to use, for example:

```txt
https://dataapibuilder.azureedge.net/schemas/v0.4.11-alpha/dab.draft.schema.json
```

### Data Source

The `data-source` element contains the information needed to connect to the backend database.

```json
"data-source" {
  "database-type": "",
  "connection-string": ""
}
```

`database-type` is a `enum string` and is used to specify what is the used backend database. Allowed values are:

+ `mssql`: for Azure SQL DB, Azure SQL MI and SQL Server
+ `postgresql`: for PostgresSQL
+ `mysql`: for MySQL
+ `cosmos`: for Cosmos DB

while `connection-string` contains the ADO.NET connection string that Data API builder will use to connect to the backend database

### Runtime global settings

This section contains options that will affect the runtime behavior and/or all exposed entities.

```json
"runtime": {
  "rest": {
    "path": "/api",
  },
  "graphql": {
    "path": "/graphql",
  },
  "host": {
    "mode": "production" | "development",
    "cors": {
      "origins": <array of string>,
      "credentials": true | false
    },
    "authentication":{
      "provider": "StaticWebApps" | "AppService" | "AzureAD" | "Simulator",
      "jwt": {
        "audience": "",
        "issuer": ""
      }
    }
  }
}
```

#### REST

`path`: defines the URL path where all exposed REST endpoints will be made available. For example if set to `/api`, the REST endpoint will be exposed `/api/<entity>`. No sub-paths allowed. Optional. Default is `/api`.

#### GraphQL

`path`: defines the URL path where the GraphQL endpoint will be made available. For example if set to `/graphql`, the GraphQL endpoint will be exposed `/graphql`. No sub-paths allowed. Optional. Default is `graphql`. Currently, a customized path value for GraphQL endpoint is not supported.

#### Host

`mode`: Define if the engine should run in `production` mode or in `development` mode. Only when running in development mode the underlying database errors will be exposed in detail. Optional. Default value is `production`. With `production` mode, the default `--LogLevel` is `Error` whereas with `development` mode it is `Debug`. These default log levels can be overridden by starting the engine through `dab` cli as mentioned [here](./running-using-dab-cli.md#run-engine-using-dab-cli).

`cors`: CORS configuration

`cors.origins`: Array with a list of allowed origins.

`cors.credentials`: Set [`Access-Control-Allow-Credentials`](https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Access-Control-Allow-Credentials) CORS header. By default, it is `false`.

`authentication`: Configure the authentication process.

`authentication.provider`: What authentication provider is used. The supported values are `StaticWebApps`, `AppService` or `Azure AD`.

`authentication.provider.jwt`: Needed if authentication provider is `Azure AD`. In this section you have to specify the `audience` and the `issuer` to allow the received JWT token to be validated and checked against the `Azure AD` tenant you want to use for authentication

### Entities

The `entities` section is where mapping between database objects to exposed endpoint is done, along with properties mapping and permission definition.

Each exposed entity is enclosed in a dedicated section. The property name will be used as the name of the entity to be exposed. For example

```json
"entities" {
  "User": {
    ...
  }
}
```

will instruct Data API builder to expose a GraphQL entity named `User` and a REST endpoint reachable via `/User` url path.

Within the entity section, there are feature specific sections:

#### GraphQL Settings
 
##### GraphQL Type

The `graphql` property defines the name with which the entity is exposed as a GraphQL type, if that is different from the entity name:

```json
"graphql":{
  "type": "my-alternative-name"
}
```

or, if needed

```json
"graphql":{
  "type": {
    "singular": "my-alternative-name",
    "plural": "my-alternative-name-pluralized"
  }
}
```

which instructs Data API builder runtime to expose the GraphQL type for the related entity and to name it using the provided type name. `plural` is optional and can be used to tell Data API builder the correct plural name for that type. If omitted Data API builder will try to pluralize the name automatically, following the english rules for pluralization (eg: https://engdic.org/singular-and-plural-noun-rules-definitions-examples)

##### GraphQL Operation

The `graphql` element will contain the `operation` property only for stored-procedures. The `operation` property defines the GraphQL operation that is configured for the stored procedure. It can be one of `Query` or `Mutation`.

For example:

```json
  {
    "graphql": "true",
    "operation": "query"
  }
```

instructs the engine that the stored procedure is exposed for graphQL through `Query` operation.

#### REST Settings

##### REST Path
The `path` property defines the endpoint through which the entity is exposed for REST APIs, if that is different from the entity name:

```json
"rest":{
  "path": "/entity-path"
}
```

##### REST Methods
The `methods` property is only valid for stored procedures. This property defines the REST HTTP actions that the stored procedure is configured for.

For example:

```json
"rest":{
  "path": "/entity-path"
  "methods": [ "GET", "POST" ]
}

```
instructs the engine that GET and POST actions are configured for this stored procedure.  

#### Database object source

The `source` property tells Data API builder what is the underlying database object to which the exposed entity is connected to.

The simplest option is to specify just the name of the table or the collection:

```json
{
  "source": "dbo.users"
}
```

a more complete option is to specify the full description of the database if that is not a table or a collection:

```json
{
  "source": {
    "object": <string>
    "type": "view" | "stored-procedure" | "table",
    "key-fields": <array-of-strings>
    "parameters": {
        "<name>": <value>,
        ...
        "<name>": <value>
    }        
  }
}
```

where

+ `object` is the name of the database object to be used
+ `type` describes if the object is a table, a view or a stored procedure
+ `key-fields` is a list of columns to be used to uniquely identify an item. Needed if type is `view` or if type is `table` and there is no Primary Key defined on it
+ `parameters` is optional and can be used if type is `stored-procedure`. The key-value pairs specified in this object will be used to supply values to stored procedures parameters, in case those are not specified in the HTTP request

More details on how to use Views and Stored Procedure in the related documentation [Views and Stored Procedures](./views-and-stored-procedures.md)

#### Relationships

The `relationships` section defines how an entity is related to other exposed entities, and optionally provides details on what underlying database objects can be used to support such relationships. Objects defined in the `relationship` section will be exposed as GraphQL field in the related entity. The format is the following:

```json
"relationships": {
  "<relationship-name>": {
    "cardinality": "one" | "many",
    "target.entity": "<entity-name>",
    "source.fields": <array-of-strings>,
    "target.fields": <array-of-strings>,
    "linking.[object|entity]": "<entity-or-db-object-name",
    "linking.source.fields": <array-of-strings>,
    "linking.target.fields": <array-of-strings>
  }
}
```

##### One-To-Many Relationship

Using the following configuration snippet as an example:

```json
"entities": {
  "Category": {
    "relationships": {
      "todos": {
        "cardinality": "many",
        "target.entity": "Todo",
        "source.fields": ["id"],
        "target.fields": ["category_id"]
      }
    }
  }
}
```

the configuration is telling Data API builder that the exposed `category` entity has a One-To-Many relationship with the `Todo` entity (defined elsewhere in the configuration file) and so the resulting exposed GraphQL schema (limited to the `Category` entity) should look like the following:

```graphql
type Category
{
  id: Int!
  ...
  todos: [TodoConnection]!
}
```

`source.fields` and `target.fields` are optional and can be used to specify which database columns will be used to create the query behind the scenes:

+ `source.fields`: database fields, in the *source* entity (`Category` in the example), that will be used to connect to the related item in the `target` entity
+ `target.fields`: database fields, in the *target* entity (`Todo` in the example), that will be used to connect to the related item in the `source` entity

These are optional if there is a Foreign Key constraint on the database, between the two tables, that can be used to infer that information automatically.

##### Many-To-One Relationship

Very similar, to the One-To-Many, but cardinality is set to `one`. Using the following configuration snippet as an example:

```json
"entities": {
  "Todo": {
    "relationships": {
      "category": {
        "cardinality": "one",
        "target.entity": "Category",
        "source.fields": ["category_id"],
        "target.fields": ["id"]
      }
    }
  }
}
```

the configuration is telling Data API builder that the exposed `Todo` entity has a Many-To-One relationship with the `Category` entity (defined elsewhere in the configuration file) and so the resulting exposed GraphQL schema (limited to the `Todo` entity) should look like the following:

```graphql
type Todo
{
  id: Int!
  ...
  category: Category
}
```

`source.fields` and `target.fields` are optional and can be used to specify which database columns will be used to create the query behind the scenes:

+ `source.fields`: database fields, in the *source* entity (`Todo` in the example), that will be used to connect to the related item in the `target` entity
+ `target.fields`: database fields, in the *target* entity (`Category` in the example), that will be used to connect to the related item in the `source` entity

These are optional if there is a Foreign Key constraint on the database, between the two tables, that can be used to infer that information automatically.

##### Many-To-Many Relationship

A many to many relationship is configured in the same way the other relationships type are configured, with the addition on of information about the association table or entity used to create the M:N relationship in the backend database.

```json
"entities": {
  "Todo": {
    "relationships": {
      "assignees": {
        "cardinality": "many",
        "target.entity": "User",
        "source.fields": ["id"],
        "target.fields": ["id"],
        "linking.object": "s005.users_todos",
        "linking.source.fields": ["todo_id"],
        "linking.target.fields": ["user_id"]
      }
    }
  }
}
```

the `linking` prefix in elements identifies those elements used to provide association table or entity information:

+ `linking.object`: the database object (if not exposed via Hawaii) that is used in the backend database to support the M:N relationship
+ `linking.source.fields`: database fields, in the *linking* object (`s005.users_todos` in the example), that will be used to connect to the related item in the `source` entity (`Todo` in the sample)
+ `linking.target.fields`: database fields, in the *linking* object (`s005.users_todos` in the example), that will be used to connect to the related item in the `target` entity (`User` in the sample)

The expected GraphQL schema generated by the above configuration is something like:

```graphql
type User
{
  id: Int!
  ...
  todos: [TodoConnection]!
}

type Todo
{
  id: Int!
  ...
  assignees: [UserConnection]!
}
```

#### Permissions

The section `permissions` defines who (in terms of roles) can access the related entity and using which actions. Actions are the usual CRUD operations: `create`, `read`, `update`, `delete`.

```json
{
  "permissions": [
    {
      "role": "...",
      "actions": ["create", "read", "update", "delete"],
      }
  ]
}
```

##### Roles

The `role` string contains the name of the role to which the defined permission will apply.

```json
{
  "role": "reader"
}
```

##### Actions

The `actions` array details what actions are allowed on the associated role. When the entity is either a table or view, roles can be configured with a combination of the actions: `create`, `read`, `update`, `delete`.

The following example tells Data API builder that the contributor role permits the `read` and `create` actions on the entity:

```json
{
  "role": "contributor",
  "actions": ["read", "create"]
}
```

In case all actions are allowed, the wildcard character `*` can be used as a shortcut to represent all actions supported for the type of entity:

```json
{
  "role": "editor",
  "actions": ["*"]
}
```

For stored procedures, roles can only be configured with the `execute` action or the wildcard `*`. The wildcard `*` will expand to the `execute` action for stored precedures.
For tables and views, the wildcard `*` action expands to the actions `create, read, update, delete`.

##### Fields

Role configuration supports granularly defining which database columns (fields) are permitted to be accessed in the section `fields`:

```json
{
  "role": "read-only",
  "action": "read",
  "fields": {
    "include": ["*"],
    "exclude": ["field_xyz"]
  }
}
```

That will indicate to Data API builder that the role *read-only* can `read` from all fields except from `field_xyz`.

Both the simplified and granular `action` definitions can be used at the same time. For example, the following configuration limits the `read` action to specific fields, while implicitly allowing the `create` action to operate on all fields:

```json
{
  "role": "reader",
  "actions": [
    {
      "action": "read",
      "fields": {
        "include": ["*"],
        "exclude": ["last_updated"]
      }
    },
    "create"
  ]
}
```

In the `fields` section above, the wildcard `*` in the `include` section indicates all fields. The fields noted in the `exclude` section have precedence over fields noted in the `include` section. The definition translates to *include all fields except for the field 'last_updated'*.

##### Policies

The `policy` section, defined per `action`, defines item-level security rules (database policies) which limit the results returned from a request. The sub-section `database` denotes the database policy expression that will be evaluated during request execution.

```json
  "policy": {
    "database": "<Expression>"
  }
```

- `database` policy: an OData expression that is translated into a query predicate that will be evaluated by the database.
  - e.g. The policy expression `@item.OwnerId eq 2000` is translated to the query predicate `WHERE Table.OwnerId  = 2000`

> A *predicate* is an expression that evaluates to TRUE, FALSE, or UNKNOWN. Predicates are used in the search condition of [WHERE](https://learn.microsoft.com/sql/t-sql/queries/where-transact-sql) clauses and [HAVING](https://learn.microsoft.com/sql/t-sql/queries/select-having-transact-sql) clauses, the join conditions of [FROM](https://learn.microsoft.com/sql/t-sql/queries/from-transact-sql) clauses, and other constructs where a Boolean value is required.
([Microsoft Learn Docs](https://learn.microsoft.com/sql/t-sql/queries/predicates?view=sql-server-ver16))

In order for results to be returned for a request, the request's query predicate resolved from a database policy must evaluate to `true` when executing against the database.

Two types of directives can be used when authoring a database policy expression:

- `@claims`: access a claim within the validated access token provided in the request.
- `@item`: represents a field of the entity for which the database policy is defined.

> [!NOTE]
> When Azure Static Web Apps authentication (EasyAuth) is configured, a limited number of claims types are available for use in database policies: `identityProvider`, `userId`, `userDetails`, and `userRoles`. See Azure Static Web App's [Client principal data](https://learn.microsoft.com/azure/static-web-apps/user-information?tabs=javascript#client-principal-data) documentation for more details.

For example, a policy that utilizes both directive types, pulling the UserId from the access token and referencing the entity's OwnerId field would look like:

```json
  "policy": {
    "database": "@claims.UserId eq @item.OwnerId"
  }
```

Data API builder will compare the value of the `UserId` claim to the value of the database field `OwnerId`. The result payload will only include records that fulfill **both** the request metadata and the database policy expression.

##### Limitations

Database policies are supported for tables and views. Stored procedures cannot be configured with policies.

Database policies are only supported for the `actions` **read**, **update**, and **delete**.

Database policy OData expression syntax supports:

- Binary operators [BinaryOperatorKind - Microsoft Learn](https://learn.microsoft.com/dotnet/api/microsoft.odata.uriparser.binaryoperatorkind?view=odata-core-7.0) such as `and`, `or`, `eq`, `gt`, `lt`, and more.
- Unary operators [UnaryOperatorKind - Microsoft Learn](https://learn.microsoft.com/dotnet/api/microsoft.odata.uriparser.unaryoperatorkind?view=odata-core-7.0) such as the negate (`-`) and `not` operators.

#### Mappings

The `mappings` section enables configuring aliases, or exposed names, for database object fields. The configured exposed names apply to both the GraphQL and REST endpoints. For entities with GraphQL enabled, the configured exposed name **must** meet GraphQL naming requirements. [GraphQL - October 2021 - Names ](https://spec.graphql.org/October2021/#sec-Names)

The format is: `<database_field>: <entity_field>`

For example:

```json
  "mappings": {
    "sku_title": "title",
    "sku_status": "status"
  }
```

means the `sku_title` field in the related database object will be mapped to the exposed name `title` and `sku_status` will be mapped to `status`. Both GraphQL and REST will require using `title` and `status` instead of `sku_title` and `sku_status` and will additionally use those mapped values in all response payloads.

#### Sample Config

This is a sample config file to give an idea of how the json config consumed by Data API builder might look like:

```json
{
  "$schema": "https://dataapibuilder.azureedge.net/schemas/v0.5.0-beta/dab.draft.schema.json",
  "data-source": {
    "database-type": "mssql",
    "connection-string": "Server=localhost;Database=PlaygroundDB;User ID=PlaygroundUser;Password=ReplaceMe;TrustServerCertificate=false;Encrypt=True"
  },
  "mssql": {
    "set-session-context": true
  },
  "runtime": {
    "rest": {
      "enabled": true,
      "path": "/api"
    },
    "graphql": {
      "allow-introspection": true,
      "enabled": true,
      "path": "/graphql"
    },
    "host": {
      "mode": "development",
      "cors": {
        "origins": [],
        "allow-credentials": false
      },
      "authentication": {
        "provider": "StaticWebApps"
      }
    }
  },
  "entities": {
    "Author": {
      "source": "authors",
      "rest": false,
      "graphql": true,
      "permissions": [
        {
          "role": "anonymous",
          "actions": [ "*" ]
        }
      ]
    },
    "Book": {
      "source": "books",
      "rest": false,
      "graphql": true,
      "permissions": [
        {
          "role": "anonymous",
          "actions": [ "*" ]
        }
      ]
    }
  }
}
```
