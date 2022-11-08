# Configuration File

+ [Configuration File](#configuration-file)
  + [Summary](#summary)
  + [Environments Support](#environments-support)
  + [Accessing Environment Variables](#accessing-environment-variables)
  + [File Structure](#file-structure)
    + [Schema](#schema)
    + [Data Source](#data-source)
    + [Runtime global settings](#runtime-global-settings)
      + [REST](#rest)
      + [GraphQL](#graphql)
      + [Host](#host)
    + [Entities](#entities)
    + [GraphQL type](#graphql-type)
    + [Database object source](#database-object-source)
    + [Relationships](#relationships)
      + [One-To-Many Relationship](#one-to-many-relationship)
      + [Many-To-One Relationship](#many-to-one-relationship)
      + [Many-To-Many Relationship](#many-to-many-relationship)
    + [Permissions](#permissions)
      + [Roles](#roles)
      + [Actions](#actions)

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
    "mode": ["production" | "development"],
    "cors": {
      "origins": <array of string>,
      "credentials": [true | false]
    },
    "authentication":{
      "provider": ["StaticWebApps" | "AppService" | "AzureAD"],
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

`mode`: Define if the engine should run in `production` mode or in `development` mode. Only when running in development mode the underlying database errors will be exposed in detail. Optional. Default value is `production`.

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

### GraphQL type

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

### Database object source

The `source` property tells Data API builder what is the underlying database object to which the exposed entity is connected to.

```json
{
  "source": "dbo.users"
}
```

> **NOTE**:
- A table or a view defined as the source must have a primary key to be usable by Data API Builder.
- Source can either be string or DatabaseSourceObject (with properties such as source-type, parameters, and key-fields).
- parameters is an optional property only for Stored-Procedure.
- key-fields is an optional property only for Table/view.
- By Default if `type` is not specified, it is inferred as Table.

- Examples:
1. **View**
```json
{
	"object": "bookView",
	"type": "view",
	"key-fields":["id", "regNo"]
}
```

2. **Table with KeyFields**
```json
{
	"object": "bookTable",
	"type": "table",
	"key-fields":["id", "regNo"]
}
```

3. **Table without KeyFields**
```json
{
	"source": "bookTable"
}
```
### Relationships

The `relationships` section defines how an entity is related to other exposed entities and optionally provide details on what underlying database objects can be used to support such relationships. Objects defined in the `relationship` section will be exposed as GraphQL field in the related entity. The format is the following:

```json
"relationships": {
  "<relationship-name>": {
    "cardinality": ["one"|"many"],
    "target.entity": "<entity-name>",
    "source.fields": [<array-of-strings>],
    "target.fields": [<array-of-strings>],
    "linking.[object|entity]": "<entity-or-db-object-name",
    "linking.source.fields": [<array-of-strings>],
    "linking.target.fields": [<array-of-strings>]
  }
}
```

#### One-To-Many Relationship

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

#### Many-To-One Relationship

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

#### Many-To-Many Relationship

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

### Permissions

The section `permissions` defines who (in terms of roles) can access the related entity and using which actions. Actions are the usual CRUD operations: `create`, `read`, `update`, `delete`.

```json
{
  "permissions": [
    {
      "role": "...",
      "actions": [...],
      }
  ]
}
```

#### Roles

The `role` string contains the name of the role to which the defined permission will apply.

```json
{
  "role": "reader"
}
```

#### Actions

The `actions` array is a mixed-type array that details what actions are allowed to related roles. In a simple case,  value is one of the following: `create`, `read`, `update`, `delete`

For example:

```json
{
  "actions": ["read", "create"]
}
```

tells Data API builder that the related role can perform `read` and `create` actions on the related entity.

In case all actions are allowed, it is possible to use the wildcard character `*` to indicate that. For example:

```json
{
  "actions": ["*"]
}
```

Another option is to specify an object with also details on what fields - defined in the `fields` object, are allowed and what are not:

```json
{
  "action": "read",
  "fields: {
    "include": ["*"],
    "exclude": ["field_xyz"]
  }
}
```

That will indicate to Data API builder that the related role can `read` from all fields except from `field_xyz`.

Both the simple and the more complex definition can be used at the same time, for example, to limit the `read` action to specific fields, while allowing create to operate on all fields:

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
}
```

In the `fields` objects, the `*` can be used as the wildcard character to indicate all fields. Exclusions have precedence over inclusions.

The `policy` section contains detail about item-level security rules.

+ `database` policy: define a rule - a predicate - that will be injected in the query sent to the database

In order for an request or item to be returned, the policies must be evaluated to `true`.

Two types of directives can be used when configuring a database policy:

+ `@claims`: access a claim stored in the authentication token
+ `@item`: access an entity's field in the underlying database.

For example a policy could be the following:

```json
  "policy": {
    "database": "@claims.UserId eq @item.OwnerId"
  }
```

Data API Builder will take the value of the claim named `UserId` and it will compare it with the value of the field `OwnerId` existing in the entity where the policy has been defined. Only those elements for which the expression will result to be true, will be allowed to be accessed.

*PLEASE NOTE* that at the moment support for policies is limited to:

+ Binary operators [BinaryOperatorKind - Microsoft Learn](https://learn.microsoft.com/dotnet/api/microsoft.odata.uriparser.binaryoperatorkind?view=odata-core-7.0) such as `and`, `or`, `eq`, `gt`, `lt`, and more.
+ Unary operators [UnaryOperatorKind - Microsoft Learn](https://learn.microsoft.com/dotnet/api/microsoft.odata.uriparser.unaryoperatorkind?view=odata-core-7.0) such as the negate (`-`) and `not` operators.

