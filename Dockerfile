#
# Build and copy GraphQL binaries and run them.
#
# Version values referenced from https://hub.docker.com/_/microsoft-dotnet-aspnet

FROM mcr.microsoft.com/dotnet/sdk:6.0-cbl-mariner2.0. AS build
RUN pwd
RUN file="$(ls -1)" && echo $file
WORKDIR /src
RUN pwd
COPY [".", "./"]
RUN file="$(ls -1)" && echo $file
RUN ls

FROM mcr.microsoft.com/dotnet/aspnet:6.0-cbl-mariner2.0 AS runtime

COPY --from=build /out /App
WORKDIR /App
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Azure.DataApiBuilder.Service.dll"]
