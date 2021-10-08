# Introduction


# Deploy as Docker Container

## Build Image

Ensure you have docker running, with Linux containers chosen.
Navigate to the root folder.

On Windows you need to do this in a WSL terminal and run

```bash
dotnet build Cosmos.GraphQL.Service/Cosmos.GraphQL.Service.sln
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
````

Push the retagged image
```bash
docker push multiverseacr.azurecr.io/multiverse-datagateway
``
