#
# Build and copy GraphQL binaries and run them.
#

# Version values referenced from https://hub.docker.com/_/microsoft-dotnet-aspnet
FROM mcr.microsoft.com/dotnet/sdk:6.0 as build

COPY ["Directory.Build.props", "."]
WORKDIR /src

COPY ["DataGateway.Service/", "./"]
RUN dotnet build "./Azure.DataGateway.Service.csproj" -c Docker -o /out -r linux-x64

FROM mcr.microsoft.com/dotnet/aspnet:6.0 as runtime

COPY --from=build /out /App
WORKDIR /App

ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Azure.DataGateway.Service.dll"]

