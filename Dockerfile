#
# Build and copy GraphQL binaries and run them.
#
# Version values referenced from https://hub.docker.com/_/microsoft-dotnet-aspnet

FROM mcr.microsoft.com/dotnet/sdk:6.0-cbl-mariner2.0. AS build


WORKDIR /src
COPY [".", "./"]
RUN file="$(ls -1)" && echo $file
RUN dotnet restore "./Service/Azure.DataApiBuilder.Service.csproj" --packages /out/engine/net6.0
RUN dotnet build "./Service/Azure.DataApiBuilder.Service.csproj" --no-restore -c Docker -o /out -r linux-x64

FROM mcr.microsoft.com/dotnet/aspnet:6.0-cbl-mariner2.0 AS runtime

COPY --from=build /out /App
WORKDIR /App
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Azure.DataApiBuilder.Service.dll"]
