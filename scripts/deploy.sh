#!/bin/bash
set -u -e -o pipefail
echo "Building..."
source ./helpers.sh

basedir="$( dirname "$( readlink -f "$0" )" )"

#CONFIG_FILE="${basedir}/./config.json"
CONFIG_FILE="./config.json"

#get-value  ".webappEndpoint"
appName="$( get-value  ".webappEndpoint" | cut -d "." -f 1)"
resourceGroupName="$( get-value  ".initConfig.resourceGroupName" | cut -d "." -f 1)"

echo "App: ${appName}"
echo "Resource group :${resourceGroupName}"
(dotnet publish "../src/AzureAppConfiguration/AzureAppConfiguration.sln" -c DEBUG --output ./temp/publish ) \
    || echo fail \
        | exit 1

cd ./temp/publish ; zip -r ../myapp.zip * ; cd ../../


echo "Deploying..."
az webapp deploy --resource-group "${resourceGroupName}" --name "${appName}" --src-path ./temp/myapp.zip --type zip


appName="$( get-value  ".webappEndpoint" )"

url="https://${appName}/AppConfigDemo"
echo "Calling web api on url: ${url}"
curl --no-progress-meter --connect-timeout 10.0 --max-time 10.0   $url | jq  

echo "Run test.sh to call API"