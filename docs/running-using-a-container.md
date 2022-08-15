# Running Data API Builder for Azure Databases using a container

Login in into the `hawaiicr` Azure Container Registry:

```bash
az acr login --name hawaiiacr
```

once you are logged in you can run Data API Builder from Docker:

```sh
docker run -it -v <configuration-file>://App/<configuration-file> -p 5000:5000 hawaiiacr.azurecr.io/dab:<tag> --ConfigFileName <configuration-file>
```

for example, if 
- you have cloned the repo in the `c:\data-api-builder` folder
- the configuration file you want to use is in the `samples\getting-started` folder and is named `library-dab-config.json` 
- you want ot use the M1.5 release

the command to run is the following:

```
docker run -it -v "c:\data-api-builder\samples://App/samples" -p 5000:5000 hawaiiacr.azurecr.io/dab:M1.5 --ConfigFileName ./samples/getting-started/library-dab-config.json
```

There is also the option to use one of the provided Docker compose files, available in the `docker` folder:

```bash
docker compose -f "./docker-compose.yml" up
```

In this case, also make sure to change the docker-compose file configuration so that the volume will point to the configuration file you want to use.

