#
# Copy GraphQL binaries and run them.
#

FROM mcr.microsoft.com/dotnet/sdk:5.0
COPY Cosmos.GraphQL.Service/Cosmos.GraphQL.Service/bin/Debug/net5.0 /App
WORKDIR /App
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Cosmos.GraphQL.Service.dll"]

