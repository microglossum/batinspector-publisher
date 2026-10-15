#!/usr/bin/env bash
# Packs the library (Release, like a consumer gets it) into ./feed under a unique version, so NuGet never serves a cached older build.
set -euo pipefail
cd "$(dirname "$0")"
version="0.1.0-livetest.$(date -u +%Y%m%d%H%M%S)"
mkdir -p feed
dotnet pack ../../src/BatInspectorPublisher -c Release -o feed -p:MinVerVersionOverride="$version" --nologo -v q
echo -n "$version" > feed/version.txt
echo "Packed BatInspectorPublisher $version into tools/LiveTest/feed"
