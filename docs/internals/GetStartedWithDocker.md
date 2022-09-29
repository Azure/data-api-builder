# Introduction

This document provides instruction for running Data API Builder inside a Docker container.

## Running a Docker Container from Azure Container Registry (Pre-built Image)

N.B. If you want to build your own image, use the next section (Build and deploy as Docker Container)

N.B. You might not have access to the container registry where the image is hosted. Reach out to someone on the team to get access.

1. Use the Azure CLI to login to Azure using your web browser. Installation instructions for the Azure CLI can be found [here - Microsoft Learn Docs)](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)

```bash
az login
```

2. Next, login to the Azure Container Registry (ACR) using the Azure CLI:

```bash
az acr login --name hawaiiacr
```

3. Locate the configuration files for your environment:
Get the file path for `dab-config.json` (and `schema.gql` if using Cosmos).

4. Choose a `docker-compose-*.yml` file from the `<git_repo_root>/docker` folder based on your environment (cosmos, sql, postgres)

    - Open the docker-compose file and update the `image` parameter with the tag you want to use. Using the tag `latest` in place of `<TAG_ID>` below will use the image with the latest commit.
    ```yaml
    dockercompose
        version: "3.9"
        services:
            hawaii:
                image: "hawaiiacr.azurecr.io/dab:<TAG_ID>"
                ports:
                    - "5000:5000"
                volumes:
                    - "<LOCAL_PATH>\dab-config.MsSql.json:/App/dab-config.json"
    ```
    - To find a different tag, find the CI run that was automatically triggered after your check-in, view more details on Azure Pipelines, then click `Job`.
        - In the logs of `Build and push docker image` stage, search for `docker push` to find the tag that was pushed.

    - If you are not using the configuration from the repo, update the path to your config/schema to point to your files and map them to `/App/dab-config.json` and for cosmos - `/App/schema.gql` as well.

    - Run `docker compose up` to start the container:
    ```bash
    docker compose -f "../../docker/docker-compose.yml" up
    ```

5. Your container should be accessible at `http://localhost:5000`. 

    - Append the `path` from the `runtime` section of configuration file to access the respective GraphQL or REST endpoint URI.
    e.g. if you are using the configuration example from this repo, GraphQL endpoint URI will be `http://localhost:5000/graphql`
    whereas one of the REST endpoint URIs will be `http://localhost:5000/api/Book`.

    - Use your favorite client like Banana Cake Pop(for GraphQL) or Postman(for both GraphQL and REST) to trigger the requests. 
        - In Banana Cake Pop, make sure to configure the schema endpoint to point to the engine's GraphQL endpoint:
        e.g. `http://localhost:5000/graphql` in its `Connection Settings`-> `General` tab.

    ![Banana Cake Pop Connection Strings](BananaCakePopConnectionSettings.png)

## Build and deploy as Docker Container

If you want to build your own docker image, follow these instructions.

N.B. Ensure you have docker running, with Linux containers chosen.

1. Navigate to the root folder of the repo.
2. Run docker build. If you are on Windows, you need to do this in a WSL terminal.

```bash
docker build -t hawaii:<yourTag> -f ../../docker/Dockerfile .
```

3. To run a container with the image you created, follow the instructions above (Running docker container from ACR (Prebuilt image)). Make sure to replace the image in the docker-compose file with the one you built. You can skip the login step.

### Deploying the Container

If you are planning to deploy the container on Azure App service or elsewhere, you should deploy the image to an ACR.
In the following example we are using `hawaiiacr.azurecr.io/hawaii` ACR, but you can use any other ACR to which have access to.

N.B. We automatically push images to the ACR on every CI build, so if you open a PR, it will generate an image with your changes and push it to the ACR automatically.

1. Update your image tag.

```bash
docker tag hawaii:<yourTag> hawaiiacr.azurecr.io/hawaii/<yourBranch>:<yourTag>
```

Choose something meaningful when tagging your images. This will make it easier to understand what each image is.
For example, on a user branch, one could use the branch name with the commit id (or a date).

```bash
docker tag hawaii hawaiiacr.azurecr.io/hawaii/docker-registry:a046756c97d49347d0fc8584ecc5050029ed5840
```

2. Login to the ACR with the correct credentials

```bash
az acr login --name hawaiiacr
```

3. Push the image

```bash
docker push hawaiiacr.azurecr.io/hawaii/<yourBranch>:<yourTag>
```

## Managing the Pipeline

The pipeline has permissions to push to this ACR through this service connection in ADO: <https://msdata.visualstudio.com/CosmosDB/_settings/adminservices?resourceId=6565800e-5e71-4e19-a610-6013655382b5>.

To push to a different container registry, we need to add a new service connection to the registry and modify the docker task in the build-pipeline.yml file to point to the new registry.
