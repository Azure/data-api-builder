#
# Build and copy GraphQL binaries and run them.
#
# Version values referenced from https://hub.docker.com/_/microsoft-dotnet-aspnet

FROM mcr.microsoft.com/dotnet/sdk:6.0-cbl-mariner2.0. AS build

RUN file="$(ls -1)" && echo $file
WORKDIR /src
COPY [".", "./"]
RUN file="$(ls -1)" && echo $file
RUN file="$(ls -1 /src/Service/)" && echo $file
RUN file="$(ls -1 /src/out/engine/net6.0)" && echo $file
RUN file="$(ls -1 ..)" && echo $file

RUN dotnet restore "/src/Service/Azure.DataApiBuilder.Service.csproj" --packages /src/out/engine/net6.0
RUN dotnet build "/src/Service/Azure.DataApiBuilder.Service.csproj" --no-restore -c Docker -o /out -r linux-x64

FROM mcr.microsoft.com/dotnet/aspnet:6.0-cbl-mariner2.0 AS runtime

COPY --from=build /out /App
WORKDIR /App
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Azure.DataApiBuilder.Service.dll"]
