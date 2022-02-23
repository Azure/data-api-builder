#
# Build and copy GraphQL binaries and run them.
#

FROM mcr.microsoft.com/dotnet/sdk:5.0 as build

COPY ["Directory.Build.props", "."]
WORKDIR /src
COPY ["DataGateway.Service/", "./"]
RUN dotnet build "./Azure.DataGateway.Service.csproj" -c Docker -o /out

FROM mcr.microsoft.com/dotnet/aspnet:5.0 as runtime

COPY --from=build /out /App
WORKDIR /App

ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Azure.DataGateway.Service.dll"]

