# Known Issues and Limitations

## Stored Procedures only support `read` action

Stored procedures only support the action `read` for both REST and GraphQL. Specifying other actions will not generate any error at startup but will generate errors when invoking the related REST endpoint and will generate an unexpected result set when used via GraphQL. Issues [#1055](https://github.com/Azure/data-api-builder/issues/1055) and [#1056](https://github.com/Azure/data-api-builder/issues/1056).

## Table with triggers error out on UPDATE

See issue [#452](https://github.com/Azure/data-api-builder/issues/452)

## Mutations are not correctly created for many-to-many relationship

See issue [#479](https://github.com/Azure/data-api-builder/issues/479)