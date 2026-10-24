#!/usr/bin/env sh
# Builds the images used by the Kubernetes manifests (k8s/).
# Usage: ./scripts/build-images.sh [version]   (default 1.0.0 = tag used in k8s/deployment.yaml)
set -eu

VERSION="${1:-1.0.0}"

# Always run from the repo root: the Dockerfiles use it as build context.
cd "$(dirname "$0")/.."

command -v docker >/dev/null 2>&1 || { echo "docker not found in PATH" >&2; exit 1; }

build() {
  name="$1"
  dockerfile="$2"
  echo ">> Building $name:$VERSION"
  docker build -f "$dockerfile" -t "$name:$VERSION" .
}

build telemetryworker   src/Telemetry.Worker/Dockerfile
build telemetryproducer src/Telemetry.Producer/Dockerfile

echo ">> Done:"
docker image ls --filter "reference=telemetry*:$VERSION"
