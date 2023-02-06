# Known Issues and Limitations

## Table with triggers error out on UPDATE

See issue [#452](https://github.com/Azure/data-api-builder/issues/452)

## Mutations are not correctly created for many-to-many relationship

See issue [#479](https://github.com/Azure/data-api-builder/issues/479)

## Knowns issues with MySQL 
Here are some known issues specifically with MySQL database. 
- Update fails on tables with Computed columns. [Issue #1001](https://github.com/Azure/data-api-builder/issues/1001)
- Update fails on views· [Issue #938](https://github.com/Azure/data-api-builder/issues/938)
- Support for CREATE/UPDATE actions on view is missing. [Issue #894](https://github.com/Azure/data-api-builder/issues/894)
- Nested Filtering in GraphQL are not yet supported. [Issue #1019](https://github.com/Azure/data-api-builder/issues/1019)
- Entities backed by Stored Procedures are not yet supported. [Issue #1024](https://github.com/Azure/data-api-builder/issues/1024)

## Knowns issues with PostgreSQL
Here are some known issues specifically with PostgreSQL database.
- Entities backed by Stored Procedures are not yet supported. [Issue #1023](https://github.com/Azure/data-api-builder/issues/1023)