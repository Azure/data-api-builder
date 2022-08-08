#
# Build and copy GraphQL binaries and run them.
#
# Version values referenced from https://hub.docker.com/_/microsoft-dotnet-aspnet
FROM mcr.microsoft.com/dotnet/sdk:6.0 as build

WORKDIR /src
COPY [".", "./"]
RUN dotnet build "./src/Service/Azure.DataApiBuilder.Service.csproj" -c Docker -o /out -r linux-x64

FROM mcr.microsoft.com/dotnet/aspnet:6.0 as runtime

COPY --from=build /out /App
WORKDIR /App
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Azure.DataApiBuilder.Service.dll"]
