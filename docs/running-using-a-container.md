# Running Data API Builder for Azure Databases using a container

Login in into the `hawaiicr` Azure Container Registry:

```bash
az acr login --name hawaiiacr
```

once you are logged in you can run Data API Builder from Docker:

```sh
docker run -it -v <configuration-file>://App/<configuration-file> -p 5000:5000 hawaiiacr.azurecr.io/hawaii/refs/heads/main:latest --ConfigFileName <configuration-file>
```

for example, if the configuration file you want to use is named `library.config.json` and you have cloned the repo in the `c:\data-api-builder` folder:

```
docker run -it -v "c:\data-api-builder\samples://App/samples" -p 5000:5000 hawaiiacr.azurecr.io/hawaii/refs/heads/main:latest --ConfigFileName ./samples/getting-started/library.config.json
```

There is also the option to use one of the provided Docker compose files:

```bash
docker compose -f "./docker-compose.yml" up
```

Make sure to change the docker-compose file configuration so that the volume will point to the configuration file you want to use.