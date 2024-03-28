#
# Build and copy GraphQL binaries and run them.
#
# Version values referenced from https://hub.docker.com/_/microsoft-dotnet-aspnet

FROM mcr.microsoft.com/dotnet/sdk:6.0-cbl-mariner2.0. AS build
RUN pwd
RUN ls
WORKDIR /src
RUN pwd
RUN ls
RUN ls /mnt

FROM mcr.microsoft.com/dotnet/aspnet:6.0-cbl-mariner2.0 AS runtime

COPY ./src/out /App
WORKDIR /App
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Azure.DataApiBuilder.Service.dll"]
