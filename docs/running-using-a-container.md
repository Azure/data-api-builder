# Running Data API Builder for Azure Databases using a container

Login into the `hawaiiacr` Azure Container Registry:

```bash
az acr login --name hawaiiacr
```

once you are logged in you can run Data API Builder from Docker:

```sh
docker run -it -v <host-folder>://App/<,container-folder> -p 5000:5000 hawaiiacr.azurecr.io/dab:<tag> --ConfigFileName <configuration-file>
```

For example, if:

- you are working in the `c:\data-api-builder` folder
- the configuration file you want to use in the `samples` folder and is named `my-sample-dab-config.json`
- you want to use the 0.2.52 (Sept2022 release)

the command to run is the following:

```bash
docker run -it -v "c:\data-api-builder\samples://App/samples" -p 5000:5000 hawaiiacr.azurecr.io/dab:0.2.52 --ConfigFileName ./samples/my-sample-dab-config.json
```

There is also the option to use one of the provided Docker compose files, available in the `docker` folder:

```bash
docker compose -f "./docker-compose.yml" up
```

In this case, also make sure to change the docker-compose file configuration so that the volume will point to the configuration file you want to use.
