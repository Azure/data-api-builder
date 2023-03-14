# Running Data API builder in Azure

Data API builder can be run in Azure in two different ways: using Azure Static Web Apps or using Azure Container Instances or Azure App Service.

## Use Azure Static Web Apps

When running Data API builder in Azure Static Web Apps, you don't have to worry about the infrastructure as it will be managed by Azure. When running Data API builder in Azure Container Instances or Azure App Service, you will have to manage the infrastructure yourself.

To learn how to use Data API builder with Azure Static Web Apps, refer to the Azure Static Web Apps documentation: [Connecting to a database with Azure Static Web Apps](https://learn.microsoft.com/en-us/azure/static-web-apps/database-overview).

## Use a Container

If you prefer to manage the infrastructure yourself, you can deploy the Data API builder container in Azure. Data API builder image is available on the Microsoft Container Registry: https://mcr.microsoft.com/en-us/product/azure-databases/data-api-builder/about

To run Data API builder in Azure Container Instances, you need to

- Create a resource group
- Create a storage account, with File Share enabled
- Upload the `dab-config.json` file to the storage account
- Create the Azure Container Instance, using the image from the Microsoft Container Registry and mounting the storage account file share so that it can accessed by Data API builder

A sample shell script that can be run on Linux (using the [Cloud Shell](https://learn.microsoft.com/en-us/azure/cloud-shell/overview) if you don't have a Linux machine or WSL installed) is available in `/samples/azure` folder.

On first run, the script will create an `.env` file that you will have to fill out with the correct values for your environment.

- `RESOURCE_GROUP`: name of the resource group you are using (eg: `my-dab-rg`)
- `STORAGE_ACCOUNT`: the name for the Storage Account you want to create (eg: `dabstorage`)
- `LOCATION`: the region where you want to create the resources (eg: `westus2`)
- `CONTAINER_INSTANCE_NAME`: the name of the Container Instance you want to create (eg: `dab-backend`)
- `DAB_CONFIG_FILE`: the configuration file you want to use (eg: `./my-dab-config.json`). Please note the the file must be in the same folder where the `./azure-deploy.sh` script is located. 

After the script has finished running, it will return the public container IP address. Use your favorite REST or GraphQL client to access the Data API builder exposed endpoints as configured in the configuration file you provided.
