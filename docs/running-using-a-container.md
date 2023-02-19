# Running Data API Builder for Azure Databases using a container

With Docker, you can run Data API builder using a container from `mcr.microsoft.com/data-api-builder`:

```sh
docker run -it -v <host-directory>://App/<container-directory> -p 5000:5000 mcr.microsoft.com/data-api-builder:<tag> --ConfigFileName <configuration-file>
```

The proceeding command makes the following assumptions:

- You are working in the `c:\data-api-builder` folder
- The configuration file you want to use in the `samples` folder and is named `my-sample-dab-config.json`
- You want to use the latest release which can be identified from the [Releases](https://github.com/Azure/data-api-builder/releases) page.

```bash
docker run -it -v "c:\data-api-builder\samples://App/samples" -p 5000:5000 pull mcr.microsoft.com/data-api-builder:0.5.* --ConfigFileName ./samples/my-sample-dab-config.json
```

You may also use one of the provided Docker compose files, available in the `docker` folder:

```bash
docker compose -f "./docker-compose.yml" up
```

When using your own Docker compose file, make sure you update your docker-compose file to point to the configuration file you want to use.
