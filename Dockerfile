#
# Copy GraphQL binaries and run them.
#

FROM mcr.microsoft.com/dotnet/sdk:5.0
# TODO once we have a story for release process, change this to release
COPY DataGateway.Service/bin/Release/net5.0 /App
WORKDIR /App
ENV ASPNETCORE_URLS=http://+:7071
RUN mkdir "/Work"
RUN touch "/Work/authfile.txt"
ENV DataGatewayService_ControlPlaneUrl=https://juno-test3.documents-dev.windows-int.net/
ENV DataGatewayService_WorkFolderLocation=/Work
ENV DataGatewayService_GatewayAuthTokenFileName=authfile.txt

ENTRYPOINT ["dotnet", "Azure.DataGateway.Service.dll"]

