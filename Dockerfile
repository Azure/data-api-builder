# Version values referenced from https://hub.docker.com/_/microsoft-dotnet-aspnet

FROM mcr.microsoft.com/dotnet/sdk:8.0-cbl-mariner2.0. AS build


WORKDIR /src
COPY [".", "./"]
RUN dotnet build "./src/Service/Azure.DataApiBuilder.Service.csproj" -c Docker -o /out -r linux-x64

FROM mcr.microsoft.com/dotnet/aspnet:8.0-cbl-mariner2.0 AS runtime

COPY --from=build /out /App
# Add default dab-config.json to /App in the image
COPY dab-config.json /App/dab-config.json
WORKDIR /App
ENV ASPNETCORE_URLS=http://+:5000

# Run as the non-root "app" user (UID/GID 64198) that ships with the
# mcr.microsoft.com/dotnet/aspnet base image. DAB is just an ASP.NET Core
# process and does not require root privileges. Declaring USER explicitly
# sets the image's Config.User field so image scanners (e.g. Checkmarx One)
# that require a non-root user in the final stage are satisfied.
# Port 5000 is above 1024 so a non-root user can bind to it without CAP_NET_BIND_SERVICE.
USER app

ENTRYPOINT ["dotnet", "Azure.DataApiBuilder.Service.dll"]
