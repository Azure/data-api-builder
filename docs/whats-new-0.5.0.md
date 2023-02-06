# What's New in Data API Builder 0.5.0

- [Public Microsoft.DataApiBuilder nuget](#public-microsoft.dataapibuilder-nuget)
- [Public JSON Schema](#public-json-schema)
- [New `execute` action for stored procedures in Azure SQL](#new-execute-action-for-stored-procedures-in-azure-sql)
- [New `mappings` section for column renames of tables in Azure SQL](#new-mappings-section)
- [Set session context to add JWT claims as name/value pairs for Azure SQL connections](#set-session-context-in-azure-sql)
- [Support for filter on nested objects within a document in PostgreSQL](#support-for-filter-on-nested-objects-within-a-document-in-postgresql)
- [Support for list of scalars for CosmosDB NoSQL](#support-scalar-list-in-cosmosdb-nosql)
- [Enhanced logging support using `LogLevel`](#enhanced-logging-support-using-loglevel)
- [Updated DAB CLI to support new features](#updated-cli)

The full list of release notes for this version is available here: [version 0.5.0 release notes](https://github.com/Azure/data-api-builder/releases/tag/v0.5.0-beta)

Details on how to install the latest version are here: [Installing DAB CLI](./running-using-dab-cli.md/#install-dab-cli)

## Public Microsoft.DataApiBuilder nuget

`Microsoft.DataApiBuilder` is now available as a public nuget package [here](https://www.nuget.org/packages/Microsoft.DataApiBuilder) for ease of installation using dotnet tool as follows:

```bash
dotnet tool install --global  Microsoft.DataApiBuilder
```

## Public JSON Schema

JSON Schema has been published here:

```text
https://dataapibuilder.azureedge.net/schemas/v0.5.0-beta/dab.draft.schema.json
```

This will give you intellisense if you are using an IDE, like VS Code, that supports JSON Schemas. Take a look at `basic-empty-dab-config.json` in the `samples` folder, to have a starting point when manually creating the `dab-config.json` file.

## New `execute` action for stored procedures in Azure SQL

A new `execute` action is introduced as the only allowed action in the `permissions` section of the configuration file only when an entity is backed by a source type of `stored-procedure`. By default, only `POST` method is allowed for such entities and only the GraphQL `mutation` operation is configured with the prefix `execute` added to their name. This behavior can be overridden by explicitly specifying the allowed `methods` in the `rest` section of the configuration file. Similarly, for GraphQL, the `operation` in the `graphql` section, can be overridden to be `query` instead. For more details, see [here.](./views-and-stored-procedures.md/#stored-procedures)

## New `mappings` section

In the `mappings` section are defined the mappings between database fields and the exposed GraphQL type and REST endpoint fields.

The format is:

`<database_field>: <entity_field>`

For example:

```json
  "mappings":{
    "title": "descriptions",
    "completed": "done"
  }
```
means the `title` field in the related database object will be mapped to `description` field in the GraphQL type or in the REST request and response payload.

## Support for Session Context in Azure SQL
To enable an additional layer of Security (eg. Row Level Security aka RLS), DAB now supports sending data to the underlying Sql Server database via SESSION_CONTEXT. For more details, please refer to this detailed document on SESSION_CONTEXT : [Runtime to Database Authorization](./runtime-to-database-authorization.md).  

## Support for filter on nested objects within a document in PostgreSQL

With PostgreSQL, you can now use the object or array relationship defined in your schema which enables to do filter operations on the nested objects just like Azure SQL.

```graphql
query {
  books(filter: { series: { name: { eq: "Foundation" } } }) {
    items {
      title
      year
      pages
    }
  }
}
```

## Support scalar list in CosmosDB NoSQL

The ability to query `List` of Scalars is now added for Cosmos DB.

Consider the below type definition

```json
type Planet @model(name:""Planet"") {
    id : ID,
    name : String,
    dimension : String,
    stars: [Star]
    tags: [String!]
}
```

It is now possible to run a query that fetches a List such as 

```json
query ($id: ID, $partitionKeyValue: String) {
    planet_by_pk (id: $id, _partitionKeyValue: $partitionKeyValue) {
        tags
    }
}
```

## Enhanced logging support using loglevel
- The default log levels for the engine in `Production` and `Development` are updated to `Error` and `Debug` respectively.
- During engine start-up, for every entity, information such as exposed names, type, whether it is auto-generated, etc. about the primary key is logged.
- In the local execution scenario, all the queries that are generated and executed during engine start-up are logged at `Debug` level.
- For every entity, relationship fields such as `source.fields`, `target.fields` and `cardinality` are logged. Incase of many-many relationships, `linking.object`, `linking.source.fields` and `linking.target.fields` are fetched from the config file and logged.
- For every incoming request, the role and the authentication status of the request are logged. This is not applicable for hosted scenario.
- In CLI, the Microsoft.DataAPIBuilder's version is logged along with the logs associated with the respective command's execution.

## Updated CLI

- `--no-https-redirect` option is added to `start` command. Using this option, the automatic redirection of requests from `http` to `https` can be prevented.
- In MsSql, session context can be enabled using `--set-session-context true` in the `init` command. A sample command is shown below
  - `dab init --database-type mssql --connection-string "Connection String" --set-session-context true` 
  
- Authentication details such as the provider, audience and issuer can be configured using the options `--auth.provider`, `--auth.audience` and `--auth.issuer.` in the `init` command. A sample command is shown below.

  - `dab init --database-type mssql --auth.provider AzureAD --auth.audience "aud" --auth.issuer "iss"`
- User friendly error messaging when the entity name is not specified.
