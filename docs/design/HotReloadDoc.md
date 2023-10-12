# Design Document: Hot Reloading for Runtime Configuration
## Objective:
The objective of this task is to enable "hot reloading" of the config file when we are not in a hosted scenario and we the Host Mode is set to `development`, in other words, in a local development scenario. To start we will implement this feature only working for the "runtime" section of the config file, but care must be made to implement this in a way that we do not block or have to change our implementation significantly for the other sections. This will allow users to update the configuration without the need to restart the service, providing more flexibility and efficiency within our application. After investigation it became apparent that hot reloading only 1 section of the config would be additional work without being able to make use of `IOptionsMonitor` to automatically update an individual section of the config that followed the `option` pattern. Because of the way we instantiate our object model of the configuration file, using `IOptionsMonitor`` in such a way was not possible at this time. We therefore will hot reload the entire config, but will lack the validation and implementation details for this to function inititally outside of Runtime section. Support for the other sections will be added incrementally, and when the entire hot reload feature is working, it will be documented for customer use.

## Current Setup:
Currently, the application uses a RuntimeConfig class, which is a record. It holds a string as the `Schema`, and record types implemented as custom classes `DataSource`, `RuntimeOptions`, and `RuntimEntities`. The `RuntimeConfig` is populated through the `RuntimeConfigProvider` which makes use of the `RuntimeConfigLoader` to handle the reading of the configuration and deserializing into the needed objects. As mentioned, the `RuntimeConfig` consists of three major sections: Connection, Runtime, and Entities. While we consider all sections, to start this task, we will focus on the Runtime section.

## Proposed Changes:
We will modify the application to support hot reloading of the confiuration file when we are in a local development scenario. This means when the config file is changed while the application is running, and we are not in a hosted scenarior, and the config file is set to development mode, the objects populated to form the RuntimeConfig should be automatically updated to reflect the changes to the config file without requiring a service restart. Because the `RuntimeConfig` is a record, a new one will need to be created entirely.

## Implementation Steps:
### File Monitoring:

Implement a file monitoring mechanism to watch for changes in the the config file. This will involve using the `.NET` `FileSystemWatcher` as a part of a custom class we implement as the `ConfigFileWatcher`. This object will need to be instantiated during startup so that hot reloading is possible prior to the application being usable. The file monitor will also need to make a callback in order to trigger the updating, which means it will need a reference to the `RuntimeConfigProvider`, which is the class responsible for handling the `RuntimeConfig`, both in terms of calling into the file loader to create the `RuntimeConfig` and in terms of accessing the `RuntimeConfig` once it is created. This fact, that the `RuntimeConfigProvider` is the one that is handling access to and the triggering of loading of the `RuntimeConfig` needs to be taken into consideration as an implementation detail. This is because the `RuntimeConfigProvider` has a `RuntimeConfigLoader`, and therefore the namespace that the `RuntimeConfigProvider` is within, `namespace Azure.DataApiBuilder.Core.Configurations` needs to reference the namespace that the `RuntimeConfigLoader` is within, `namespace Azure.DataApiBuilder.Config`. Since the `ConfigFileWatcher` will need a reference to the `RuntimeConfigProvider`, it will not be possible to have it live within the class or the project of the `RuntimeConfigLoader`, because this will create a circular dependancy. We therefore have decided to place the `ConfigFileWatcher` in the same namespace as the `RuntimeConfigProvider` and strongly couple the `ConfigFileWatcher` to the `RuntimeConfigProvider`, since it is a callback within the file watcher which will trigger a call into the provider, these classes are programmatically coupled already. This will then enable the file watcher to access the data that is needed from the file loader through the reference it has to the provider, since the provider has a file loader. Our file watcher then registers a trigger function which will itself call a function within the provider to handle the actual hot reload process.

One consideration is that in order to handle hot reloading of the sections outside of the runtime section we may need to walk a tree of config files. Since with recent changes we can now reference another configuration file, which can itself reference another configuration file, to be certain that we are hot reloading when any change is made to a configuration file, we will need to walk that tree and monitor all of those files. However, this will only be needed for the sections beyond the runtime section, because the root in this tree will always have the runtime data.

### Hot Reloading Trigger:

Define a trigger mechanism to initiate the hot reloading process when changes are detected in the config file. This trigger function will then call into the `RuntimeConfigProvider` initiating the update of the `RuntimeConfig` by reloading the new config and creating a new object model. This trigger function lives in the `ConfigFileWatcher` and during construction we register this function with the underlying `FileSystemWatcher.Changed` field. This function then calls into the `RuntimeConfigProvider` which has a hot reload function that will then use the config loader to create the new object model that we save as the `RuntimeConfig` in the `RuntimeConfigProvider`.

### Config Object Update:

When the hot reloading is triggered, create a new `RuntimeConfig` record using the same deserialization process as on startup. We call into this through the same function provided by the config loader that we use on startup. We use the same config file path in the same file system again, as provided by the config loader.

By using the regular deserialization process we should make sure that we correctly walk through the tree of config files and correctly deserialize from all of them to create the right object model, but we need to make certain that this is the case during implementation.

### Dependency Updating:

Implement a mechanism to update services that are already registered during startup and depend on configuration settings that have changed. Authorization in particular is only configured on startup. In order to reset those authorization settings when we change the config file we need a way to have those particular settings reset and update. This is a detail that remains in design.

# Potential Classes still in consideration

## IConfigurableService Interface:

### Responsibilities:
Interface to be implemented by services that depend on configuration settings.
Contains the `UpdateConfigurations()` method that will be called during hot reloading to update the configuration-related parts of the service.

## GraphQLSchemaManager Class:

### Responsibilities:
Handles dynamic updates to the GraphQL schema based on changes in the entities.
Validates and applies the updated schema. Note that we do not have to worry about concurrency as this is in the local dev scenario and inconsistent behavior with active requests is expected when hot reloading the config file.


# Diagram

![Class Diagram](HotReloadDiagram.jpg)