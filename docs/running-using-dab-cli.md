# Running Data API Builder for Azure Databases using CLI

The easiest option that doesn't require cloning the repo is to use the `dab` [CLI tool](./dab-cli.md) that you can find in the `Release` tab:

## Install `dab` CLI

You can install the `dab` CLI using [.NET tools](https://learn.microsoft.com/dotnet/core/tools/global-tools):

- Download the latest version of the package: [dab.<version_number>.nupkg](https://github.com/Azure/data-api-builder/releases/)
- Navigate to the folder where the package file is downloaded.

then, to install this tool globally, use:

```bash
dotnet tool install -g --add-source ./ dab --version <version_number>
```

> **ATTENTION**: if you are running on Linux or MacOS, you may need to add .NET global tools to your PATH to call `dab` directly. Once installed run:
> `export PATH=$PATH:~/.dotnet/tools`

## Update `dab` CLI to latest version

If you already have an older version of `dab` CLI installed, update the tool using:

```bash
dotnet tool update -g --add-source ./ dab --version <version_number>
```

### Validate the Install

Installing the package will make the `dab` command available on your development machine. To validate your installation, you can check the installed version with:

```bash
dab --version
```

## Run engine using `dab` CLI

To start the Data API builder engine, use the `start` action:

```bash
dab start
```

## Get started using `dab` CLI

To quickly get started using the CLI, make sure you have read the [Getting Started](./getting-started/getting-started.md) guide to become familiar with basic Data API builder concepts and then use the [Getting started with Data API Builder (`dab`) CLI](./getting-started/getting-started-dab-cli.md) to learn how to use the CLI tool.

## Uninstall `dab` CLI

For any reason, if you need to uninstall `dab` cli, simply do:

```bash
dotnet tool uninstall -g dab
```
