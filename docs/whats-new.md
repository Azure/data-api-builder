# What's New in Data API Builder 0.3.7

The full list of release notes for this version is available here: [version 0.3.7 release notes](https://github.com/Azure/data-api-builder/releases/tag/v0.3.7-alpha)

## Azure Active Directory Authentication

Azure AD is now fully working. Read how to use it here: [Authentication with Azure AD](./authentication-azure-ad.md)

## View Support

Views are now supported both in REST and GraphQL. If you have a view, for example [`dbo.vw_books_details`](../samples/getting-started/azure-sql-db/library.azure-sql.sql#L112) it can be exposed using the following `dab` command:

```sh
dab add BookDetail --source dbo.vw_books_details --source.type View --source.key-fields "id" --permissions "anonymous:read"
```

the `source.key-fields` option is used to specify which fields from the view are used to uniquely identify an item, so that navigation by primary key can be implemented also for views.

## Stored Procedures Support

Stored procedures are now supported for REST requests. If you have a stored procedure, for example [`dbo.stp_get_all_cowritten_books_by_author`](../samples/getting-started/azure-sql-db/library.azure-sql.sql#L138) it can be exposed using the following `dab` command:

```sh

dab add GetCowrittenBooksByAuthor --source dbo.stp_get_all_cowritten_books_by_author --source.type "stored-procedure" --permissions "anonymous:read"
```

The parameter can be passed in the URL query string when calling the REST endpoint:

```
http://<dab-server>/api/GetCowrittenBooksByAuthor?author=isaac%20asimov
```
