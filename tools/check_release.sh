#!/usr/bin/env bash
set -euo pipefail
export DOTNET_NOLOGO=1

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
tag="${RELEASE_TAG:-}"
if [[ ! "$tag" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
    echo 'Release tags must use MAJOR.MINOR.PATCH, for example 1.0.0 (no v prefix).' >&2
    exit 1
fi
version="$("${DOTNET:-dotnet}" msbuild "$root/src/Cigarette.csproj" -nologo -getProperty:Version)"
plugin_version="$(sed -n 's/^[[:space:]]*public const string Version = "\([^"]*\)";[[:space:]]*$/\1/p' "$root/src/Plugin.cs")"
if [[ "$version" != "$tag" || "$plugin_version" != "$tag" ]]; then
    echo 'Tag, csproj Version, and Plugin.Version must match before publishing.' >&2
    exit 1
fi
echo "Release version verified: $tag"
