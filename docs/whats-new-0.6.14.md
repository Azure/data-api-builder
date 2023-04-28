# What's New in Data API builder 0.6.14

This is the patch for March 2023 release for Data API builder for Azure Databases

## Bug Fixes
- Address query filter access deny issue for Cosmos in #1436.
- Cosmos DB currently doesn't support field level authorization, to avoid the situation when the users accidentally pass in the ```field``` permissions in the runtime config, we added a validation check in #1449 