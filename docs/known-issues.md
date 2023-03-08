# Known Issues and Limitations

## General

- Mutations are not correctly created for many-to-many relationship. [Issue #479](https://github.com/Azure/data-api-builder/issues/479)
- Database authorization policies are not supported for the `create` action. [Issue #1216](https://github.com/Azure/data-api-builder/issues/1216)

## Azure SQL and SQL Server

- Table with triggers error out on UPDATE. [Issue #452](https://github.com/Azure/data-api-builder/issues/452)
- JSON data is escaped in the response. [Issue #444](https://github.com/Azure/data-api-builder/issues/444)

## MySQL 

- Update fails on tables with Computed columns. [Issue #1001](https://github.com/Azure/data-api-builder/issues/1001)
- Update fails on views· [Issue #938](https://github.com/Azure/data-api-builder/issues/938)
- Support for CREATE/UPDATE actions on view is missing. [Issue #894](https://github.com/Azure/data-api-builder/issues/894)
- Nested Filtering in GraphQL are not yet supported. [Issue #1019](https://github.com/Azure/data-api-builder/issues/1019)
- Entities backed by Stored Procedures are not yet supported. [Issue #1024](https://github.com/Azure/data-api-builder/issues/1024)
- Database policies for PUT/PATCH operations in REST are not yet supported. [Issue #1267](https://github.com/Azure/data-api-builder/issues/1267)

## PostgreSQL

- Entities backed by Stored Procedures are not yet supported. [Issue #1023](https://github.com/Azure/data-api-builder/issues/1023)
