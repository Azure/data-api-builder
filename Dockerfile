#
# Build and copy GraphQL binaries and run them.
#

FROM mcr.microsoft.com/dotnet/sdk:5.0 as build

COPY ["Directory.Build.props", "."]
WORKDIR /src

COPY ["DataGateway.Service/", "./"]
RUN dotnet build "./Azure.DataGateway.Service.csproj" -c Docker -o /out -r linux-x64
# RUN dotnet publish -r linux-x64 -c Release -o output


FROM mcr.microsoft.com/dotnet/aspnet:5.0 as runtime
WORKDIR /app
COPY --from=build /out /app

ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Azure.DataGateway.Service.dll"]

