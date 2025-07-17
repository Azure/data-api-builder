# Version values referenced from https://hub.docker.com/_/microsoft-dotnet-aspnet

FROM mcr.microsoft.com/dotnet/sdk:8.0-cbl-mariner2.0. AS build


WORKDIR /src
COPY [".", "./"]
RUN dotnet build "./src/Service/Azure.DataApiBuilder.Service.csproj" -c Docker -o /out -r linux-x64

FROM mcr.microsoft.com/dotnet/aspnet:8.0-cbl-mariner2.0 AS runtime

COPY --from=build /out /App
COPY src/Service.Tests/dab-config.MsSql.json /App/dab-config.json
WORKDIR /App
ENV ASPNETCORE_URLS=http://+:1234
#ENV ASPNETCORE_HTTPS_PORT="443"
ENTRYPOINT ["dotnet", "Azure.DataApiBuilder.Service.dll"]
