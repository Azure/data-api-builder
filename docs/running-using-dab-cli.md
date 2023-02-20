# Running Data API Builder for Azure Databases using CLI

The easiest option that doesn't require cloning the repo is to use the `dab` [CLI tool](./dab-cli.md) that you can find in the `Microsoft.DataApiBuilder` nuget package [here.](https://www.nuget.org/packages/Microsoft.DataApiBuilder)

## Install `dab` CLI

You can install the latest `dab` CLI using [.NET tools](https://learn.microsoft.com/dotnet/core/tools/global-tools):

```bash
dotnet tool install --global  Microsoft.DataApiBuilder
```

> **ATTENTION**: if you are running on Linux or MacOS, you may need to add .NET global tools to your PATH to call `dab` directly. Once installed run:
> `export PATH=$PATH:~/.dotnet/tools`

## Update `dab` CLI to latest version

If you already have an older version of `dab` CLI installed, update the tool using:

```bash
dotnet tool update -g Microsoft.DatApiBuilder --version <version_number>
```

### Validate the Install

Installing the package will make the `dab` command available on your development machine. To validate your installation, you can check the installed version with:

```bash
dab --version
```

## Run engine using `dab` CLI

To start the Data API builder engine, use the `start` action if you have the configuration file `dab-config.json` as described [here](./configuration-file.md) in the current directory:

```bash
dab start
```

For providing a custom configuration file, you can use the option `-c` or `--config` followed by the config file name.
```
dab start -c my-custom-dab-config.json
```

You can also start the engine with a custom log level. This will alter the amount of logging that is provided during both startup and runtime of the service. To start the service with a custom log level use the `start` action with `--verbose` or `--LogLevel <0-6>`. `--verbose` will start the service with a log level of `informational` where as `--LogLevel <0-6>` represents one of the following log levels.
![image](https://user-images.githubusercontent.com/93220300/216731511-ea420ee8-3b52-4e1b-a052-87943b135be1.png)

```bash
dab start --verbose
```

```bash
dab start --LogLevel 0
```

This will log the information as follows:

- At startup 
  - what configuration file is being used (Level: Information)

- During the (in-memory schema generation)
  - what entities have been loaded (names, paths) (Level: Information)
  - automatically identified relationships columns (Level: Debug)
  - automatically identified primary keys, column types etc (Level: Debug)

- Whenever a request is received
  - if request has been authenticated or not and which role has been assigned (Level: Debug)
  - the generated queries sent to the database (Level: Debug)

- Internal behavior
  - view which queries are generated (any query, not just those necessarily related to a request) and sent to the database (Level: Debug)


## Get started using `dab` CLI

To quickly get started using the CLI, make sure you have read the [Getting Started](./getting-started/getting-started.md) guide to become familiar with basic Data API builder concepts and then use the [Getting started with Data API Builder (`dab`) CLI](./getting-started/getting-started-dab-cli.md) to learn how to use the CLI tool.

## Uninstall `dab` CLI

For any reason, if you need to uninstall `dab` cli, simply do:

```bash
dotnet tool uninstall -g Microsoft.DataApiBuilder
```
