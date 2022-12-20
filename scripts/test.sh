#!/bin/bash
echo "Building..."
source ./helpers.sh
CONFIG_FILE="./config.json"

appName="$( get-value  ".webappEndpoint" )"

url="https://${appName}/AppConfigDemo"
echo "Url: ${url}"
watch -n 0.1 "curl --no-progress-meter --connect-timeout 1.50 --max-time 1.50   $url | jq"  