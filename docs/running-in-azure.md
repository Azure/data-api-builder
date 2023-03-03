# Running Data API builder in Azure

Data API builder can be run in Azure via container image. Therefore it is assumed that you are familiar with Docker and that you have Docker installed on your machine.

## Build and Publish the Docker image

### Create an Azure Registry Container

If you don't have an Azure Container Registry already available, create one, for example (the sample resource group `my-dab-rg` is assumed to exist already):

```bash
az acr create -g my-dab-rg -n dabcr -l WestUs2 --sku Standard --admin-enabled true
```

then login to the newly created registry:

```bash
az acr login --name dabcr
```

get the user name and the password you'll be using to allow the App Service to pull the image from the created registry:

```bash
az acr credential show --name dmdabcr --query "{username:username, password:passwords[0].value}"
```

I'll be using the `username` and `password` values later. If you don't want to use username and password, you can switch to use Managed Identities for more security: [Use managed identity to pull image from Azure Container Registry](https://learn.microsoft.com/azure/app-service/configure-custom-container?pivots=container-linux#use-managed-identity-to-pull-image-from-azure-container-registry)

### Build the Docker image

Now clone the Data API builder repository if you haven't done yet, and from the cloned repository local folder, using the branch you want - Jan2023 in this sample - build the Docker image using the provided [Dockerfile](../Dockerfile), and tag it with the name of the registry you decided to use. For example:

```bash
docker build . -t dabcr.azurecr.io/dab:Jan2023
```

### Publish the Docker image to the Azure Container Registry

Push the created image the container registry:

```bash
docker push dabcr.azurecr.io/dab:Jan2023
```

And you're now ready to run Data API builder in Azure.

## Run Data API builder in App Service

The easiest way to run Data API builder in Azure so that it will be easily accessible from other services is to use an App Service as it will automatically provide HTTPS support.

The steps to have Data API builder running in an App Service are the following (don't worry a script will do everything for you. The list is reported so you know what's going to happen when you run the script):

1. Create an [App Service Plan](https://learn.microsoft.com/azure/app-service/app-service-plan-manage)
1. Create an App Service that uses the previously created Docker image (for reference: [Configure a custom container for Azure App Service](https://learn.microsoft.com/azure/app-service/configure-custom-container?pivots=container-linux))
1. [Create a Storage Account](https://learn.microsoft.com/azure/storage/common/storage-account-create?tabs=azure-portal) to host the Data API builder configuration file
1. Upload the configuration file
1. Mount the created Storage Account to the created App Service (for reference: [Mount storage to Linux container](https://learn.microsoft.com/azure/app-service/configure-connect-to-azure-storage?tabs=cli&pivots=container-linux#mount-storage-to-linux-container))
1. Update the App Service to tell Data API builder to use the configuration file
1. Configure CORS to allow cross-origin calls from your frontend. *Please note that the provided script set CORS to allow ANY origin which is not a best practice from a security standpoint. Only allow accepting request from trusted frontend.*

As a convenience, a shell script `azure-deploy.sh` file is available in `/samples/azure` which performs the previous steps.

On first run, the script will create an `.env` file that you will have to fill out with the correct values for your environment.

- `RESOURCE_GROUP`: name of the resource group you are using (eg: `my-dab-rg`)
- `APP_NAME`: the name of the App Service you want to create (eg: `dab-backend`)
- `APP_PLAN_NAME`: the name of the App Service Plan you want to create (eg: `dab-backend-plan`)
- `DAB_CONFIG_FILE`: the configuration file you want to use (eg: `my-dab-config.json`)
- `STORAGE_ACCOUNT`: the name for the Storage Account you want to create (eg: `dabstorage`)
- `LOCATION`: the region where you want to create the resources (eg: `westus2`)
- `IMAGE_NAME`: the image you want to use (eg: `dabcr.azurecr.io/dab:M1.5`)
- `IMAGE_REGISTRY_USER`: the `username` you have retrieved before from the Azure Container Registry
- `IMAGE_REGISTRY_PASSWORD`: the `password` you have retrieved above from the Azure Container Registry

After the script has finished running, you have to give App Service a few minutes to pull the container and warm everything up, and you'll be good to go. Connect to the created App Service URL, for example https://dab-backend.azurewebsites.net using your favorite REST or GraphQL client and you can start to use your data.
