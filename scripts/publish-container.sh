#!/usr/bin/env bash
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

set -euo pipefail

if [[ $# -lt 2 || ( "$1" != runtime && "$1" != runtime-nonroot ) ]]; then
    echo "Usage: $0 <runtime|runtime-nonroot> <image:tag> [<image:tag> ...]" >&2
    exit 1
fi

target=$1
shift
tags=()
for tag in "$@"; do
    tags+=(--tag "$tag")
done

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
docker buildx build \
    --file "$repo_root/Dockerfile" \
    --platform linux/amd64,linux/arm64 \
    --target "$target" \
    "${tags[@]}" \
    --push \
    "$repo_root"
