#
# Copy GraphQL binaries and run them.
#

FROM mcr.microsoft.com/dotnet/sdk:5.0
# TODO once we have a story for release process, change this to release
COPY GraphQL.Service/bin/Release/net5.0 /App
WORKDIR /App
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "DataGateway.Service.dll"]

