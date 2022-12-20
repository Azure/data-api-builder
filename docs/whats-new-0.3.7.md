# What's New in Data API Builder 0.3.7

- [Public JSON Schema](#public-json-schema)
- [View Support](#view-support)
- [Stored Procedures Support](#stored-procedures-support)
- [Azure Active Directory Authentication](#azure-active-directory-authentication)
- [New "Simulator" Authentication Provider for local authentication](#new-simulator-authentication-provider-for-local-authentication)
- [Support for filter on nested objects within a document in Cosmos DB](#support-for-filter-on-nested-objects-within-a-document-in-cosmos-db)

The full list of release notes for this version is available here: [version 0.3.7 release notes](https://github.com/Azure/data-api-builder/releases/tag/v0.3.7-alpha)

## Public JSON Schema

JSON Schema has been published here:

```text
https://dataapibuilder.blob.core.windows.net/schemas/v0.3.7-alpha/dab.draft.schema.json
```

This will give you intellisense if you are using an IDE, like VS Code, that supports JSON Schemas. Take a look at `basic-empty-dab-config.json` in the `samples` folder, to have a starting point when manually creating the `dab-config.json` file.

Please note that if you are using DAB CLI to create and manage the `dab-config.json` file, DAB CLI is not yet creating the configuration file using the aforementioned reference to the JSON schema file.

## View Support

Views are now supported both in REST and GraphQL. If you have a view, for example [`dbo.vw_books_details`](../samples/getting-started/azure-sql-db/library.azure-sql.sql#L112) it can be exposed using the following `dab` command:

```sh
dab add BookDetail --source dbo.vw_books_details --source.type View --source.key-fields "id" --permissions "anonymous:read"
```

the `source.key-fields` option is used to specify which fields from the view are used to uniquely identify an item, so that navigation by primary key can be implemented also for views. Its the responsibility of the developer configuring DAB to enable or disable actions (for example, the `create` action) depending on if the view is updatable or not.

## Stored Procedures Support

Stored procedures are now supported for REST requests. If you have a stored procedure, for example [`dbo.stp_get_all_cowritten_books_by_author`](../samples/getting-started/azure-sql-db/library.azure-sql.sql#L138) it can be exposed using the following `dab` command:

```sh

dab add GetCowrittenBooksByAuthor --source dbo.stp_get_all_cowritten_books_by_author --source.type "stored-procedure" --permissions "anonymous:read"
```

The parameter can be passed in the URL query string when calling the REST endpoint:

```text
http://<dab-server>/api/GetCowrittenBooksByAuthor?author=isaac%20asimov
```

Its the responsibility of the developer configuring DAB to enable or disable actions (for example, the `create` action) to allow or deny specific HTTP verbs to be used when calling the stored procedure. For example, for the stored procedure used in the example, given that its purpose is to return some data, it would make sense to only allow the `read` action.

## Azure Active Directory Authentication

Azure AD authentication is now fully working. Read how to use it here: [Authentication with Azure AD](./authentication-azure-ad.md)

## New "Simulator" Authentication Provider for local authentication

To simplify testing of authenticated requests when developing locally, a new `simulator` authentication provider has been created; `simulator` is a configurable authentication provider which instructs the Data API Builder engine to treat all requests as authenticated. More details here: [Local Authentication](./local-authentication.md)

## Support for filter on nested objects within a document in Cosmos DB

With Azure Cosmos DB, You can use the object or array relationship defined in your schema which enables to do filter operations on the nested objects.

```graphql
query {
  books(first: 1, filter : { author : { profile : { twitter : {eq : ""@founder""}}}})
    id
    name
  }
}
```
