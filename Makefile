.DEFAULT_GOAL := help

GAME_PATH ?= $(NIVALIS_GAME_PATH)
CONFIGURATION ?= Release
DOTNET ?= $(if $(wildcard .tools/dotnet/dotnet),./.tools/dotnet/dotnet,dotnet)
PACKAGE_DIR ?= bin
PROJECT := src/Cigarette.csproj

export RELEASE_TAG GAME_PATH CONFIGURATION DOTNET PROJECT PACKAGE_DIR
export DOTNET_CLI_TELEMETRY_OPTOUT := 1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE := 1
export DOTNET_CLI_HOME := $(CURDIR)/.tools/cli-home
export NUGET_PACKAGES := $(CURDIR)/.tools/nuget

.PHONY: help check-sdk check-game build test check-package install package deploy clean check-release
help:
	@printf '%s\n' 'make build GAME_PATH="..."    Build the DLL' 'make test                     Run managed timing/audio checks' 'make package GAME_PATH="..."  Build, test and create the ZIP in bin/' 'make deploy GAME_PATH="..."   Build, test, tag if needed and publish a GitHub release' 'make check-release RELEASE_TAG=1.0.0  Check version consistency' 'make clean                    Remove generated build output' 'Optional: DOTNET="/path/to/dotnet" CONFIGURATION=Release PACKAGE_DIR=bin'

check-sdk:
	@command -v "$$DOTNET" >/dev/null 2>&1 || { printf '%s\n' '.NET SDK not found. Install SDK 8 on PATH, place it in .tools/dotnet/, or pass DOTNET="/path/to/dotnet".'; exit 1; }

check-game:
	@test -n "$$GAME_PATH" || { printf '%s\n' 'Set GAME_PATH to the game root (or export NIVALIS_GAME_PATH).'; exit 1; }
	@test -f "$$GAME_PATH/BepInEx/interop/Assembly-CSharp.dll" -a -f "$$GAME_PATH/BepInEx/core/BepInEx.Unity.IL2CPP.dll" || { printf '%s\n' 'Game references missing. Launch the game with BepInEx IL2CPP once first.'; exit 1; }

build: check-sdk check-game
	@"$$DOTNET" build "$$PROJECT" --configuration "$$CONFIGURATION" "-p:GamePath=$$(cd "$$GAME_PATH" && pwd)" --nologo

# Pure managed checks do not require game assemblies.
test: check-sdk
	@"$$DOTNET" run --project tests/SessionTests.csproj --configuration "$$CONFIGURATION"

check-package: build
	@version="$$("$$DOTNET" msbuild "$$PROJECT" -nologo -getProperty:Version)"; "$$DOTNET" run --project tests/SessionTests.csproj --configuration "$$CONFIGURATION" -- bin/Nivalis.Cigarette.dll "$$version"

install: build
	@install -Dm644 bin/Nivalis.Cigarette.dll "$$GAME_PATH/BepInEx/plugins/Nivalis.Cigarette.dll"
	@rm -f -- "$$GAME_PATH/BepInEx/plugins/Cigarette.dll"

package: check-package
	@set -eu; \
	version="$$("$$DOTNET" msbuild "$$PROJECT" -nologo -getProperty:Version)"; \
	RELEASE_TAG="$$version" bash tools/check_release.sh; \
	mkdir -p -- "$$PACKAGE_DIR"; \
	output="$$(cd "$$PACKAGE_DIR" && pwd)"; \
	staging="$$(mktemp -d "$$output/.package.XXXXXXXX")"; \
	trap 'rm -rf -- "$$staging"' EXIT; \
	mkdir -p -- "$$staging/files/BepInEx/plugins"; \
	cp -- bin/Nivalis.Cigarette.dll "$$staging/files/BepInEx/plugins/Nivalis.Cigarette.dll"; \
	"$$DOTNET" msbuild tools/Package.proj -nologo -target:Package \
	    "-p:PackageSource=$$staging/files" "-p:PackageArchive=$$staging/archive.zip"; \
	archive="Nivalis.Cigarette-$$version.zip"; \
	mv -f -- "$$staging/archive.zip" "$$output/$$archive"; \
	(cd "$$output" && sha256sum "$$archive" > "$$archive.sha256"); \
	printf 'Package: %s/%s\n' "$$output" "$$archive"

# Deployment requires a clean checkout; missing version tags are created after verification.
deploy: check-sdk check-game
	@bash tools/deploy.sh

check-release: check-sdk
	@bash tools/check_release.sh

clean:
	@rm -rf -- bin src/obj tests/bin tests/obj

# Opt-in local tooling. Never a dependency of build/install/package/deploy.
.PHONY: build-rail-dev install-rail-dev
build-rail-dev: build
	@"$$DOTNET" build dev/rail-tools/RailDev.csproj --configuration "$$CONFIGURATION" "-p:GamePath=$$(cd "$$GAME_PATH" && pwd)" --nologo

install-rail-dev: build-rail-dev
	@install -Dm644 bin/Nivalis.Cigarette.dll "$$GAME_PATH/BepInEx/plugins/Nivalis.Cigarette.dll"
	@install -Dm644 dev/rail-tools/bin/Nivalis.Cigarette.RailDev.dll "$$GAME_PATH/BepInEx/plugins/Nivalis.Cigarette.RailDev.dll"

.PHONY: test-rail-dev
test-rail-dev: check-sdk
	@"$$DOTNET" run --project dev/tests/RailDevTests.csproj --configuration "$$CONFIGURATION"
