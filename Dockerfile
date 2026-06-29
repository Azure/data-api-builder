# Version values referenced from https://hub.docker.com/_/microsoft-dotnet-aspnet

FROM mcr.microsoft.com/dotnet/sdk:10.0-azurelinux3.0 AS build


WORKDIR /src
COPY [".", "./"]
RUN dotnet build "./src/Service/Azure.DataApiBuilder.Service.csproj" -c Docker -o /out -r linux-x64
RUN mkdir /package \
	&& cp /src/src/Service/dab-config*.json /package/ \
	&& find /src/src/Service/ -maxdepth 1 -name "*.gql" -exec cp {} /package/ \;

FROM mcr.microsoft.com/dotnet/aspnet:10.0-azurelinux3.0 AS runtime

COPY --from=build /out /App
COPY --from=build /package /App
WORKDIR /App
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Azure.DataApiBuilder.Service.dll"]
