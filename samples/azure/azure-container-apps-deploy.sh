#!/bin/bash

# Strict mode, fail on any error
set -euo pipefail

# Azure configuration
FILE=".env"
if [[ -f $FILE ]]; then
	echo "loading from .env"
    export $(egrep . $FILE | xargs -n1)
else
	cat << EOF > .env
RESOURCE_GROUP=""
STORAGE_ACCOUNT=""
LOCATION=""
LOG_ANALYTICS_WORKSPACE=""
CONTAINERAPPS_ENVIRONMENT="dm-dab-aca-env"
CONTAINERAPPS_APP_NAME="dm-dab-aca-app"
DAB_CONFIG_FILE="./dab-config.json"
EOF
	echo "Enviroment file (.env) not detected."
	echo "Please configure values for your environment in the created .env file and run the script again."
	echo "Read the docs/running-in-azure.md to get info on needed enviroment variables."
	exit 1
fi

echo "starting"
cat << EOF > log.txt
EOF

FILE_SHARE="dabconfig"
DAB_CONFIG_FILE_NAME="dab-config.json"
DABCONFIGFOLDER="./${FILE_SHARE}/${DAB_CONFIG_FILE_NAME}"

echo "creating resource group '$RESOURCE_GROUP'"  | tee -a log.txt
az group create --name $RESOURCE_GROUP --location $LOCATION \
    -o json >> log.txt

echo "creating storage account: '$STORAGE_ACCOUNT'" | tee -a log.txt
az storage account create --name $STORAGE_ACCOUNT --resource-group $RESOURCE_GROUP --location $LOCATION --sku Standard_LRS \
    -o json >> log.txt

echo "retrieving storage connection string" | tee -a log.txt
STORAGE_CONNECTION_STRING=$(az storage account show-connection-string --name $STORAGE_ACCOUNT -g $RESOURCE_GROUP -o tsv)

echo 'creating file share' | tee -a log.txt
az storage share create -n $FILE_SHARE --connection-string $STORAGE_CONNECTION_STRING \
    -o json >> log.txt

echo "uploading configuration file '$DAB_CONFIG_FILE'" | tee -a log.txt
az storage file upload --source $DAB_CONFIG_FILE --path $DAB_CONFIG_FILE_NAME --share-name $FILE_SHARE --connection-string $STORAGE_CONNECTION_STRING \
    -o json >> log.txt

echo "create log analytics workspace '$LOG_ANALYTICS_WORKSPACE'" | tee -a log.txt
az monitor log-analytics workspace create \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION \
  --workspace-name $LOG_ANALYTICS_WORKSPACE

echo "retrieving log analytics client id" | tee -a log.txt
LOG_ANALYTICS_WORKSPACE_CLIENT_ID=$(az monitor log-analytics workspace show  \
  --resource-group "$RESOURCE_GROUP" \
  --workspace-name "$LOG_ANALYTICS_WORKSPACE" \
  --query customerId  \
  --output tsv | tr -d '[:space:]')

echo "retrieving log analytics secret" | tee -a log.txt
LOG_ANALYTICS_WORKSPACE_CLIENT_SECRET=$(az monitor log-analytics workspace get-shared-keys \
  --resource-group "$RESOURCE_GROUP" \
  --workspace-name "$LOG_ANALYTICS_WORKSPACE" \
  --query primarySharedKey \
  --output tsv | tr -d '[:space:]')

echo "retrieving storage key" | tee -a log.txt
STORAGE_KEY=$(az storage account keys list -g $RESOURCE_GROUP -n $STORAGE_ACCOUNT --query '[0].value' -o tsv) 

echo "creating container apps environment: '$CONTAINERAPPS_ENVIRONMENT'" | tee -a log.txt
az containerapp env create \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION \
  --name "$CONTAINERAPPS_ENVIRONMENT" \
  --logs-workspace-id "$LOG_ANALYTICS_WORKSPACE_CLIENT_ID" \
  --logs-workspace-key "$LOG_ANALYTICS_WORKSPACE_CLIENT_SECRET"

echo "waiting to finalize the ACA environment" | tee -a log.txt
while [ "$(az containerapp env show -n $CONTAINERAPPS_ENVIRONMENT -g $RESOURCE_GROUP --query properties.provisioningState -o tsv | tr -d '[:space:]')" != "Succeeded" ]; do sleep 10; done

echo "get ACA environment id" | tee -a log.txt
CONTAINERAPPS_ENVIRONMENTID=$(az containerapp env show -n "$CONTAINERAPPS_ENVIRONMENT" -g "$RESOURCE_GROUP" --query id -o tsv |sed 's/\r$//')

echo "mount storage account on azure container apps environment" | tee -a log.txt
RES=$(az containerapp env storage set --name $CONTAINERAPPS_ENVIRONMENT \
  --resource-group $RESOURCE_GROUP \
  --storage-name $FILE_SHARE \
  --azure-file-account-name $STORAGE_ACCOUNT \
  --azure-file-account-key $STORAGE_KEY \
  --azure-file-share-name $FILE_SHARE \
  --access-mode ReadWrite)


echo "creating container app : '$CONTAINERAPPS_APP_NAME' on the environment : '$CONTAINERAPPS_ENVIRONMENT'" | tee -a log.txt
az deployment group create \
  -g $RESOURCE_GROUP \
  -f ./bicep/dab-on-aca.bicep \
  -p appName=$CONTAINERAPPS_APP_NAME dabConfigFileName=$DAB_CONFIG_FILE_NAME mountedStorageName=$FILE_SHARE environmentId=$CONTAINERAPPS_ENVIRONMENTID

echo "get the azure container app FQDN" | tee -a log.txt
ACA_FQDN=$(az containerapp show -n $CONTAINERAPPS_APP_NAME -g $RESOURCE_GROUP --query properties.configuration.ingress.fqdn -o tsv | tr '[:upper:]' '[:lower:]' | tr -d '[:space:]')

echo "you can now try out the API at the following address : https://${ACA_FQDN}/api/<your-entity-name>"

echo "done" | tee -a log.txt
