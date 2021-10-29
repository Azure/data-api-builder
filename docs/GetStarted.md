# Introduction

# Deploy docker container from DockerHub (Prebuilt image)
N.B. This section only applies if you want to use a prebuilt image, if you want to build it yourself, move on to the next section (Build and deploy as Docker Container)

N.B. The image is currently hosted in a temporary location, so you might not have access to it. 

1. Pull the docker image:
```bash
docker pull matrembl/graphql:20211019-154145 # Note to update to the correct tag
```
2. Update the config.json and the appsettings.json files with your connection strings and the resolvers

3. Launch the docker container and map the config.json and appsettings.json files. The command should look something like this (depending on the path to your appsettings and config files, and the image you are using):

```bash
docker run --mount type=bind,source="$(pwd)\DataGateway.Service\appsettings.json",target="/App/appsettings.json" --mount type=bind,source="$(pwd)\DataGateway.Service\config.json",target="/App/config.json" -d -p 5000:5000 matrembl/graphql:20211019-154145
# Note to update to the correct tag
```
4. The container should be accessible at localhost:5000

# Build and deploy as Docker Container

## Build Image

Ensure you have docker running, with Linux containers chosen.
Navigate to the root folder.

On Windows you need to do this in a WSL terminal and run

```bash
dotnet build DataGateway.Service/Azure.DataGateway.Service.sln
```

build a docker image
```bash
docker build -t multiverse-datagateway -f Dockerfile .
```


## Run Container

Create and run container accessible on http://localhost:5000/ by running

```bash
docker run -d -p 5000:5000 multiverse-datagateway
```

## Deploy Container

If you are planning to deploy the container on Azure App service or elsewhere, you should deploy the image to an ACR.
In the following example we are using `multiverseacr.azurecr.io/multiverse-graphql` ACR, but you can use any other ACR to which have access to.

### Push Image

To push the built image to the multiverse ACR, do the following


Tag the image correctly

```bash
docker tag multiverse-datagateway multiverseacr.azurecr.io/multiverse-datagateway
```


Login to the ACR with the correct credentials
```bash
docker login multiverseacr.azurecr.io
```

Push the retagged image
```bash
docker push multiverseacr.azurecr.io/multiverse-datagateway
```
