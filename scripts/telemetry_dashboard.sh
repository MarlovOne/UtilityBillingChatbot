#!/bin/bash

docker run -d \
    --rm \
    --name aspire-dashboard \
    -p 18888:18888 \
    -p 4317:18889 \
    -e DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true \
    mcr.microsoft.com/dotnet/aspire-dashboard:latest \
    -it \
    bash
