# Introduction

# Deploy docker container from ACR (Prebuilt image)
N.B. This section only applies if you want to use a prebuilt image, if you want to build it yourself, move on to the next section (Build and deploy as Docker Container)

N.B. You might not have access to the container registry where the image is hosted. Reach out to someone on the team for us to give you permissions.

1. You will need to login to the ACR:

```bash
az acr login --name hawaiiacr
```

2. update the configuration files for your environment:

Update the config.json and the appsettings.json files for your chosen environment.

## Running the container with docker compose
This is the easiest way.

3. Choose a docker-compose-*.yml file based on your environment (cosmos, sql, postgres)


```bash
docker compose -f "./docker-compose.yml" up
```

4 Your container should be accessible at localhost:5000

## Running the container manually

3. Pull the docker image

```bash
docker pull hawaiiacr.azurecr.io/hawaii:latest # Note to use the desired tag here.
```

4. Launch the docker container and map the config.json and appsettings.json files. The command should look something like this (depending on the path to your appsettings and config files, and the image you are using):

```bash
docker run --mount type=bind,source="$(pwd)\DataGateway.Service\appsettings.json",target="/App/appsettings.json" --mount type=bind,source="$(pwd)\DataGateway.Service\config.json",target="/App/config.json" -d -p 5000:5000 hawaiiacr.azurecr.io/hawaii:latest
# Note to update to the correct tag
```

5. The container should be accessible at localhost:5000

## Managing the Pipeline
The pipeline has permissions to push to this ACR through this service connection in ADO: https://msdata.visualstudio.com/CosmosDB/_settings/adminservices?resourceId=6565800e-5e71-4e19-a610-6013655382b5.

To push to a different container registry, we need to add a new service connection to the registry and modify the docker task in the build-pipeline.yml file to point to the new registry.

# Build and deploy as Docker Container

## Build Image

Ensure you have docker running, with Linux containers chosen.
Navigate to the root folder.

On Windows you need to do this in a WSL terminal and run this to build a docker image

```bash
docker build -t hawaii -f Dockerfile .
```

## Run Container

Create and run container accessible on http://localhost:5000/ by running

```bash
docker run -d -p 5000:5000 hawaii
```

## Deploy Container

If you are planning to deploy the container on Azure App service or elsewhere, you should deploy the image to an ACR.
In the following example we are using `hawaiiacr.azurecr.io/hawaii` ACR, but you can use any other ACR to which have access to.

### Push Image

To push the built image to the hawaiiacr ACR, do the following

Tag the image correctly

```bash
docker tag hawaii hawaiiacr.azurecr.io/hawaii/<yourBranch>:<yourTag>
```

Choose something meaningful when tagging your images. This will make it easier to understand what each image is.
For example, on a user branch, one could use the branch name with the commit id (or a date).

```bash
docker tag hawaii hawaiiacr.azurecr.io/hawaii/docker-registry:a046756c97d49347d0fc8584ecc5050029ed5840
```

Login to the ACR with the correct credentials

```bash
az acr login --name hawaiiacr
```

Push the retagged image

```bash
docker push hawaiiacr.azurecr.io/hawaii/<yourBranch>:<yourTag>
```
