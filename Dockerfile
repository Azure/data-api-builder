# Version values referenced from https://hub.docker.com/_/microsoft-dotnet-aspnet

FROM mcr.microsoft.com/dotnet/sdk:10.0-azurelinux3.0 AS build


WORKDIR /src
COPY [".", "./"]
RUN dotnet build "./src/Service/Azure.DataApiBuilder.Service.csproj" -c Docker -o /out -r linux-x64

# ---------------------------------------------------------------------------
# Common runtime base.
#
# Not intended as a final build target. Both the `runtime` (root, default)
# and `runtime-nonroot` (non-root, scanner-friendly) variants derive from
# this stage so the shared setup stays in one place.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-azurelinux3.0 AS runtime-base

COPY --from=build /out /App
# Add default dab-config.json to /App in the image
COPY --from=build /out/dab-config.json /App/dab-config.json
WORKDIR /App
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "Azure.DataApiBuilder.Service.dll"]

# ---------------------------------------------------------------------------
# Non-root variant. Build explicitly with:
#   docker build --target runtime-nonroot -t <repo>:<version>-nonroot .
#
# Runs as the non-root user that ships with the
# mcr.microsoft.com/dotnet/aspnet base image (UID/GID 1654, exposed via the
# APP_UID env var, on the azurelinux3.0 variant). DAB does not require root,
# and declaring USER explicitly sets the image's Config.User field so image
# scanners (e.g. Checkmarx One) that require a non-root user in the final
# stage are satisfied. We use the numeric `USER $APP_UID` form (rather than
# `USER app`) per .NET container guidance: a numeric UID is friendlier to
# image scanners and to Kubernetes `runAsNonRoot`/`runAsUser` checks, which
# cannot resolve a username to a UID at admission time.
#
# Safeguards applied to minimize the chance of runtime breakage:
#   * Pre-create /App/logs and `chown app:app /App/logs` (non-recursive) so
#     the documented default file-sink path ("logs/dab-log.txt", relative to
#     WORKDIR /App) is writable. Ownership of the published assemblies under
#     /App is intentionally left unchanged - a recursive chown would
#     duplicate every assembly layer and roughly double the image size for
#     no runtime benefit, since DAB only needs write access to /App/logs.
#   * Default port stays at 5000, which is above 1024, so binding works
#     without CAP_NET_BIND_SERVICE. Users overriding ASPNETCORE_URLS to a
#     privileged port (<1024) must add `--cap-add=NET_BIND_SERVICE` to
#     `docker run` or front DAB with a reverse proxy.
#
# Notes for consumers of this image:
#   * Host bind-mounts (config, logs, certs, etc.) must be readable - and
#     writable, if DAB needs to write them - by UID 1654 on the host.
#     Either `chown -R 1654:1654 /host/path` or, in Kubernetes, set
#     `securityContext.fsGroup: 1654`.
#   * `docker exec` defaults to UID 1654. Use `docker exec --user 0`
#     for administrative actions inside a running container.
#   * Downstream Dockerfiles (FROM <this image>) that need to install
#     packages or write outside /App should add `USER 0` before those
#     instructions, then restore `USER $APP_UID` at the end.
# ---------------------------------------------------------------------------
FROM runtime-base AS runtime-nonroot

RUN mkdir -p /App/logs && chown $APP_UID:$APP_UID /App/logs
USER $APP_UID

LABEL org.opencontainers.image.title="Data API builder (non-root)" \
      org.opencontainers.image.description="Data API builder running as the non-root 'app' user (UID 1654 on azurelinux3.0)." \
      org.opencontainers.image.source="https://github.com/Azure/data-api-builder"

# ---------------------------------------------------------------------------
# Default (root-running) variant. Build with either:
#   docker build -t <repo>:<version> .                       # no --target needed
#   docker build --target runtime -t <repo>:<version> .
#
# This is the LAST stage in the file, so a plain `docker build` with no
# --target argument produces this image. Keeping the root-running variant
# as the default preserves backwards compatibility with the previously
# published image - existing users see no behavior change. The non-root
# variant is opt-in via `--target runtime-nonroot`.
# ---------------------------------------------------------------------------
FROM runtime-base AS runtime
