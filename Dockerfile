# Version values referenced from https://hub.docker.com/_/microsoft-dotnet-aspnet
ARG BUILDTIME
ARG VERSION
ARG REVISION

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build

WORKDIR /src
COPY [".", "./"]
RUN dotnet publish "./src/Service/Azure.DataApiBuilder.Service.csproj" \
    -c Release \
    -o /out \
    -r linux-x64 \
    --self-contained false \
    /p:PublishTrimmed=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

WORKDIR /App
COPY --from=build /out .

# Create config directory
RUN mkdir -p /App/config

# Environment variables
ENV ASPNETCORE_URLS=http://+:5000 \
    ASPNETCORE_ENVIRONMENT=Production

# Metadata labels
LABEL org.opencontainers.image.created="${BUILDTIME}" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}" \
      org.opencontainers.image.title="Data API Builder" \
      org.opencontainers.image.description="Data API builder for Azure Databases" \
      org.opencontainers.image.source="https://github.com/Karo-Data-Management-Limited/wellbeing-os-module-dab"

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:5000/health || exit 1

EXPOSE 5000

ENTRYPOINT ["dotnet", "Azure.DataApiBuilder.Service.dll"]
