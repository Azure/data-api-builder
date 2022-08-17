# Running Data API Builder for Azure Databases from source code

Make sure you have [.NET 6.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0.) installed. Clone the repository and then execute, from the root folder of the repository, 

```sh
dotnet run --project ./src/Service
``` 

The Data API Builder engine will try to load the configuration from the `dab-config.json` file in the same folder, if present.

If there is no `dab-config.json` the engine will start anyway but it will not be able to serve anything.

You may use the optional `--ConfigFileName` option to specify which configuration file will be used:

```sh
dotnet run --project ./src/Service  --ConfigFileName ../../samples/getting-started/library-dab-config.json
```


