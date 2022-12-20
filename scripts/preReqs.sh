#!/bin/bash
sudo apt install jq zip

curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash

curl -sL  https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh > dotnet-install.sh
chmod +x *.sh
./dotnet-install.sh --channel 7.0

export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools

dotnet="$( dotnet --list-sdks | grep -G ^7.*$sdk | wc -l )"
echo $dotnet
if [ "${dotnet}" == 0 ]; then
echo "Please install dotnet 7.0 manually. https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu"
fi



