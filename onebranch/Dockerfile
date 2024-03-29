#
# Build Data API builder Docker container in OneBranch
#
# Version values referenced from https://hub.docker.com/_/microsoft-dotnet-aspnet
# dotnet/sdk and dotnet/aspnet required to build working container.
FROM mcr.microsoft.com/dotnet/sdk:6.0-cbl-mariner2.0. AS build
WORKDIR /src
FROM mcr.microsoft.com/dotnet/aspnet:6.0-cbl-mariner2.0 AS runtime

# The ./src/out path below points to the finalized build bits created by the pipeline.
# The path is relative to the "Docker build context" specified by the parameter dockerFileContextPath
# in task: onebranch.pipeline.imagebuildinfo@1
# The "Docker build context" contains all the files that will be used by the command "docker build"
# OneBranch uses the injected task Copy Artifacts to
# copy artifacts from pipeline artifacts: "/mnt/{...}/out" 
# to the container running the docker tasks: /mnt/{...}/docker/artifacts/
COPY ./src/out/engine/net6.0 /App

# Change working directory to the /App folder within the Docker image and configure
# how Data Api buider is started when the container runs.
WORKDIR /App
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Azure.DataApiBuilder.Service.dll"]
