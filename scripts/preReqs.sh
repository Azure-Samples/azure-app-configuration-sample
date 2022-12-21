#!/bin/bash
sudo apt install jq zip


if  [ $( which az   | wc -l) = 0  ]; then
    curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash
else
    echo "az already installed"
fi

if [ $( dotnet --list-sdks | grep -G ^7.*$sdk | wc -l ) = 0 ]; then
    curl -sL  https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh > dotnet-install.sh
    chmod +x ./dotnet-install.sh
    echo "Installing net7"
    ./dotnet-install.sh --channel 7.0
else
    echo "dotnet already installed. SDKs:"
    dotnet --list-sdks
fi


if  [ $( which dotnet   | wc -l) = 0  ]; then
    export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools
else
    echo "dotnet already in path"
fi





