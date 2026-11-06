# Building and publishing container images

The Dockerfile supports `linux/amd64` and `linux/arm64`. The SDK runs on the
builder's native architecture and cross-compiles for the requested platform;
the ASP.NET runtime base image is selected for that platform. Both the default
`runtime` target and the opt-in `runtime-nonroot` target support these platforms.

## Local build

Build and load a single platform into the local Docker image store:

```sh
docker buildx build --platform linux/arm64 --target runtime --load -t dab:local .
```

Omit `--platform` to build for the local builder's default platform. Running an
image for another architecture requires emulation. Cross-building
`runtime-nonroot` also requires emulation (or native builder nodes for both
architectures) because its permissions setup executes in the target image.

## Release publishing

Use a Buildx builder with multi-platform support, for example:

```sh
docker buildx create --name dab-multiarch --driver docker-container --use
docker buildx inspect --bootstrap
```

Authenticate to the release pipeline's **writable registry**, then publish all
tags for a variant in one build. Replace the example registry and version with
the release pipeline's existing destination and version:

```sh
docker login <registry>
bash scripts/publish-container.sh runtime \
  <registry>/data-api-builder:<version> \
  <registry>/data-api-builder:latest
bash scripts/publish-container.sh runtime-nonroot \
  <registry>/data-api-builder:<version>-nonroot \
  <registry>/data-api-builder:latest-nonroot
```

The script pushes both platform images and their shared image index for each
supplied tag. Do not push the architectures separately to the same tag: the
last push would replace the earlier architecture instead of publishing an
index. A multi-platform build uses `--push`, not `--load`.

The official MCR release pipeline is not defined in this repository. Its
maintainers must replace the existing single-platform build/push with this
script (or the equivalent `docker buildx build --platform
linux/amd64,linux/arm64 --push` command), preserving their existing registry
authentication, tag selection, and MCR promotion process. Every promoted tag
must retain the multi-platform index and both referenced platform images.
Changing the Dockerfile alone does not make existing MCR tags multi-platform.

Verify the resulting tag in the writable registry and again after MCR promotion:

```sh
docker buildx imagetools inspect <registry>/data-api-builder:<version>
docker buildx imagetools inspect mcr.microsoft.com/azure-databases/data-api-builder:<version>
```

Both must list `linux/amd64` and `linux/arm64`. Provenance attestations may also
appear as `unknown/unknown`; these do not replace the required platform images.
The `Container build` workflow builds both variants on native x64 and ARM64
runners and checks the application's runtime identifier and non-root log access.
