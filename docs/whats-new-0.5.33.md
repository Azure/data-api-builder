# What's New in Data API builder 0.5.33

- [Honor REST and GraphQL enabled flag at runtime level](#honor-rest-and-graphql-enabled-flag-at-runtime-level)
- [Add Correlation ID to request logs](#add-correlation-id-to-request-logs)
- [Wildcard Operation Support for Stored Procedures in Engine and CLI](#wildcard-operation-support-for-stored-procedures-in-engine-and-cli)

The full list of release notes for this version is available here: [version 0.5.33 release notes](https://github.com/Azure/data-api-builder/releases/tag/v0.5.33)

## Honor REST and GraphQL enabled flag at runtime level

A new option is added to allow enabling or disabling REST/GraphQL requests for all entities at the runtime level. If disabled globally, no entities would be accessible via REST or GraphQL requests irrespective of the individual entity settings. If enabled globally, individual entities are accessible by default unless disabled explicitly by the entity level settings.

```json
"runtime": {
    "rest": {
      "enabled": false,
      "path": "/api"
    },
    "graphql": {
      "allow-introspection": true,
      "enabled": false,
      "path": "/graphql"
    }
  }
```
## Add Correlation ID to request logs

To assist in debugging we attach a correlation ID to any logs that are generated during a request. Since many requests may be made, having a way to identify the logs to a specific request will be important to assist in the debugging process.

## Wildcard Operation Support for Stored Procedures in Engine and CLI

For stored procedures, roles can now be configured with the wildcard `*` action but it will only expand to the `execute` action.
