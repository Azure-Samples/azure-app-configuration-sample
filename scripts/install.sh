#!/bin/bash

echo "Running az cli $(az version | jq '."azure-cli"' )"
echo "Running in subscription $( az account show | jq -r '.id') / $( az account show | jq -r '.name'), AAD Tenant $( az account show | jq -r '.tenantId')"

source ./helpers.sh

basedir="$( dirname "$( readlink -f "$0" )" )"

#CONFIG_FILE="${basedir}/./config.json"
CONFIG_FILE="./config.json"

if [ ! -f "$CONFIG_FILE" ]; then
    cp ./config-template.json "${CONFIG_FILE}"
fi


jsonpath=".initConfig.location"
location="$( get-value  "${jsonpath}" )"
[ "${location}" == "" ] && { echo "Please configure ${jsonpath} in file ${CONFIG_FILE}" ; exit 1 ; }

jsonpath=".initConfig.resourceGroupName"
resourceGroupName="$( get-value  "${jsonpath}" )"

if [ "${resourceGroupName}" == "" ]; then
    read -p "Resource group name: " resourceGroupName
    put-value      '.initConfig.resourceGroupName' $resourceGroupName
fi

put-value      '.initConfig.subscriptionId' "$( az account show | jq -r '.id')" 

#
# Get the role definitions, so they can be statically refereced in bicep
#

roles="$( az role definition list | jq '[.[] | {name: .name,  roleName: .roleName}] | map({(.roleName|tostring): .name}) | add' ) "
mkdir -p temp
echo "${roles}" > temp/roles.json

put-value      '.roles.Contributor' "$(echo "${roles}" | jq -r '.Contributor' )" 
put-value      '.roles."App Configuration Data Reader"' "$(echo "${roles}" | jq -r '."App Configuration Data Reader"' )" 
put-value      '.roles."Key Vault Secrets User"' "$(echo "${roles}" | jq -r '."Key Vault Secrets User"' )" 
put-value      '.roles."Key Vault Reader"' "$(echo "${roles}" | jq -r '."Key Vault Reader"' )" 
put-value      '.roles."Azure Service Bus Data Receiver"' "$(echo "${roles}" | jq -r '."Azure Service Bus Data Receiver"' )" 

#
# Create the resource group
#
( az group create --location "${location}"  --name "${resourceGroupName}"  \
    &&  echo "Creation of resource group ${resourceGroupName} complete." ) \
    || echo "Failed to create resource group ${resourceGroupName}."  \
        |  exit 1

#
# Deploy
#
deploymentResultJSON="$( az deployment group create \
    --resource-group "${resourceGroupName}" \
    --template-file "./main.bicep" \
    --parameters \
        location="${location}" \
    --output json )"

echo "ARM Deployment: $( echo "${deploymentResultJSON}" | jq -r .properties.provisioningState )"
echo "${deploymentResultJSON}" > results.json

if ! [ $( echo "${deploymentResultJSON}" | jq -r .properties.provisioningState ) = "Succeeded" ]; then
    echo "Deployment failed. Do not proceed"
    exit 1
fi

put-value      '.connectionStrings.applicationInsights' "$(echo "${deploymentResultJSON}" | jq -r '.properties.outputs.applicationInsights_ConnectionString.value' )" 
put-value      '.connectionStrings.serviceBus' "$(echo "${deploymentResultJSON}" | jq -r '.properties.outputs.changeSubscription_ServiceBusConnectionString.value' )" 
put-value      '.connectionStrings.appConfiguration' "$(echo "${deploymentResultJSON}" | jq -r '.properties.outputs.connectionStrings_AppConfig.value' )" 
put-value      '.webappEndpoint' "$(echo "${deploymentResultJSON}" | jq -r '.properties.outputs.webappEndpoint.value' )" 

./deploy.sh
