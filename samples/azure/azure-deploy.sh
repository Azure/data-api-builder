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
CONTAINER_INSTANCE_NAME="dm-dab-aci"
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

echo "creating resource group '$RESOURCE_GROUP'"  | tee -a log.txt
az group create --name $RESOURCE_GROUP --location $LOCATION \
    -o json >> log.txt

echo "creating storage account: '$STORAGE_ACCOUNT'" | tee -a log.txt
az storage account create --name $STORAGE_ACCOUNT --resource-group $RESOURCE_GROUP --location $LOCATION --sku Standard_LRS \
    -o json >> log.txt

echo "retrieving storage connection string" | tee -a log.txt
STORAGE_CONNECTION_STRING=$(az storage account show-connection-string --name $STORAGE_ACCOUNT -g $RESOURCE_GROUP -o tsv)

echo 'creating file share' | tee -a log.txt
az storage share create -n dab-config --connection-string $STORAGE_CONNECTION_STRING \
    -o json >> log.txt

echo "uploading configuration file '$DAB_CONFIG_FILE'" | tee -a log.txt
az storage file upload --source $DAB_CONFIG_FILE --path dab-config.json --share-name dab-config --connection-string $STORAGE_CONNECTION_STRING \
    -o json >> log.txt

echo "retrieving storage key" | tee -a log.txt
STORAGE_KEY=$(az storage account keys list -g $RESOURCE_GROUP -n $STORAGE_ACCOUNT --query '[0].value' -o tsv) 

echo "creating container" | tee -a log.txt
az container create -g $RESOURCE_GROUP --name $CONTAINER_INSTANCE_NAME \
  --image  mcr.microsoft.com/azure-databases/data-api-builder:latest \
  --ports 5000 \
  --ip-address public \
  --cpu 2 \
  --memory 4 \
  --os-type Linux \
  --azure-file-volume-mount-path /dab-config \
  --azure-file-volume-account-name $STORAGE_ACCOUNT \
  --azure-file-volume-account-key $STORAGE_KEY \
  --azure-file-volume-share-name dab-config \
  --command-line "dotnet Azure.DataApiBuilder.Service.dll --ConfigFileName /dab-config/dab-config.json" \
    -o json >> log.txt

echo "retrieving IP address" | tee -a log.txt
CONTAINER_PUBLIC_IP=$(az container show -g dm-dab-rg -n dmdabaci --query "ipAddress.ip" -o tsv)

echo "container available at http://$CONTAINER_PUBLIC_IP:5000" | tee -a log.txt

echo "done" | tee -a log.txt
