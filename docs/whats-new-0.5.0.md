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

## Set session context in Azure SQL

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

## Enhanced logging support using loglevel

## Updated CLI
