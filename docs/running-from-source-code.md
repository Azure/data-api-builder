# Running Data API Builder for Azure Databases from source code

Please note that familiarity with Git commands and tooling is assumed throughout the tutorial. Make sure `git` is installed in your machine.

## Clone the Data API builder for Azure Databases engine

Clone the repository locally:

```
git clone https://github.com/Azure/data-api-builder.git
```

and then make sure you are using the M1.5 release:

```bash
cd .\data-api-builder\
git checkout release/M1.5
```

create a configuration configuration file (`dab-config.json`) manually or using the [DAB CLI](./dab-cli.md) tool. If you want to create the file manually you can use the `./samples/basic-empty-dab-config.json` as a starting point.

Make sure to add some entities (you can follow the [Getting Started](./getting-started/getting-started.md) guide if you want) and then you can start the Data API Builder engine.

## Run the Data API builder for Azure Databases engine

Make sure you have [.NET 6.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0.) installed. Clone the repository and then execute, from the root folder of the repository, 

```sh
dotnet run --project ./src/Service
``` 

The Data API Builder engine will try to load the configuration from the `dab-config.json` file in the same folder, if present.

If there is no `dab-config.json` the engine will start anyway but it will not be able to serve anything.

You may use the optional `--ConfigFileName` option to specify which configuration file will be used:

```sh
dotnet run --project ./src/Service  --ConfigFileName ../../samples/my-sample-dab-config.json
```


