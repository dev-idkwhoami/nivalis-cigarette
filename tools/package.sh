#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
variant="${1:?Pass standalone or api}"
case "$variant" in
    standalone) suffix='' ;;
    api) suffix='-API' ;;
    *) echo 'Unknown package variant; use standalone or api.' >&2; exit 1 ;;
esac
version="$("$DOTNET" msbuild src/Cigarette.csproj -nologo -getProperty:Version)"
RELEASE_TAG="$version" bash tools/check_release.sh
(cd dependencies && sha256sum --check SHA256SUMS)
mkdir -p -- "${PACKAGE_DIR:-bin}"
output="$(cd "${PACKAGE_DIR:-bin}" && pwd)"
staging="$(mktemp -d "$output/.package.XXXXXXXX")"
trap 'rm -rf -- "$staging"' EXIT
mkdir -p -- "$staging/files/BepInEx/plugins"
cp -- dependencies/Nivalis.ModCompanion.dll "$staging/files/BepInEx/plugins/Nivalis.ModCompanion.dll"
cp -- "bin/$variant/Nivalis.Cigarette.dll" "$staging/files/BepInEx/plugins/Nivalis.Cigarette.dll"
if [[ "$variant" == api ]]; then
    cp -- bin/api/Nivalis.Cigarette.Api.dll "$staging/files/BepInEx/plugins/Nivalis.Cigarette.Api.dll"
fi
"$DOTNET" msbuild tools/Package.proj -nologo -target:Package \
    "-p:PackageSource=$staging/files" "-p:PackageArchive=$staging/archive.zip"
"$DOTNET" run --project tests/SessionTests.csproj --configuration "${CONFIGURATION:-Release}" --no-build --     --check-archive "$variant" "bin/$variant/Nivalis.Cigarette.dll" "$staging/archive.zip" dependencies/Nivalis.ModCompanion.dll
archive="Nivalis.Cigarette-$version$suffix.zip"
mv -f -- "$staging/archive.zip" "$output/$archive"
(cd "$output" && sha256sum "$archive" > "$archive.sha256")
printf 'Package (%s): %s/%s\n' "$variant" "$output" "$archive"
