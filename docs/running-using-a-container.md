# Running Data API builder for Azure Databases using a container

With Docker, you can run Data API builder using a container from `mcr.microsoft.com/azure-databases/data-api-builder`:

```shell
docker run -it -v <configuration-file>:/App/<configuration-file> -p 5000:5000 pull mcr.microsoft.com/azure-databases/data-api-builder:<tag> --ConfigFileName <configuration-file>
```

The proceeding command makes the following assumptions:

- Let's say you are in the directory: `C:\data-api-builder` folder
- The configuration file you want to use in the `samples` folder and is named `my-sample-dab-config.json`
- You want to use the latest release which can be identified from the [Releases](https://github.com/Azure/data-api-builder/releases) page. For Example, If you would like to use the image with the tag `0.5.34`, run the following command:

```shell
docker run -it -v "c:\data-api-builder\samples:/App/samples" -p 5000:5000 pull mcr.microsoft.com/azure-databases/data-api-builder:0.5.34 --ConfigFileName ./samples/my-sample-dab-config.json
```

You may also use one of the provided Docker compose files, available in the `docker` folder:

```shell
docker compose -f "./docker-compose.yml" up
```

When using your own Docker compose file, make sure you update your docker-compose file to point to the configuration file you want to use.
