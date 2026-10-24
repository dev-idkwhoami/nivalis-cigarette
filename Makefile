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
	@printf '%s\n' 'make build GAME_PATH="..."    Build standalone DLL in bin/standalone/' 'make build-api GAME_PATH="..." Build API-enabled DLLs in bin/api/' 'make test                     Run managed timing/audio checks' 'make package GAME_PATH="..."  Build, test and create both ZIPs in bin/' 'make package-standalone / package-api  Build just one archive' 'make install / install-api    Install the selected variant' 'make deploy GAME_PATH="..."   Build, test, tag if needed and publish a GitHub release' 'make check-release RELEASE_TAG=1.0.0  Check version consistency' 'make clean                    Remove generated build output' 'Optional: DOTNET="/path/to/dotnet" CONFIGURATION=Release PACKAGE_DIR=bin'

check-sdk:
	@command -v "$$DOTNET" >/dev/null 2>&1 || { printf '%s\n' '.NET SDK not found. Install SDK 8 on PATH, place it in .tools/dotnet/, or pass DOTNET="/path/to/dotnet".'; exit 1; }

check-game:
	@test -n "$$GAME_PATH" || { printf '%s\n' 'Set GAME_PATH to the game root (or export NIVALIS_GAME_PATH).'; exit 1; }
	@test -f "$$GAME_PATH/BepInEx/interop/Assembly-CSharp.dll" -a -f "$$GAME_PATH/BepInEx/core/BepInEx.Unity.IL2CPP.dll" || { printf '%s\n' 'Game references missing. Launch the game with BepInEx IL2CPP once first.'; exit 1; }

.PHONY: build-api build-standalone check-package-standalone check-package-api package-standalone package-api install-api

build: build-standalone
build-standalone: check-sdk check-game
	@"$$DOTNET" build "$$PROJECT" --configuration "$$CONFIGURATION" "-p:GamePath=$$(cd "$$GAME_PATH" && pwd)" -p:EnableApi=false --nologo

build-api: check-sdk check-game
	@"$$DOTNET" build "$$PROJECT" --configuration "$$CONFIGURATION" "-p:GamePath=$$(cd "$$GAME_PATH" && pwd)" -p:EnableApi=true --nologo

# Pure managed checks cover the API contract even when no game is installed.
test: check-sdk
	@"$$DOTNET" run --project tests/SessionTests.csproj --configuration "$$CONFIGURATION"

check-package-standalone: build-standalone
	@version="$$("$$DOTNET" msbuild "$$PROJECT" -nologo -getProperty:Version)"; "$$DOTNET" run --project tests/SessionTests.csproj --configuration "$$CONFIGURATION" -- bin/standalone/Nivalis.Cigarette.dll "$$version" standalone

check-package-api: build-api
	@version="$$("$$DOTNET" msbuild "$$PROJECT" -nologo -getProperty:Version)"; "$$DOTNET" run --project tests/SessionTests.csproj --configuration "$$CONFIGURATION" -- bin/api/Nivalis.Cigarette.dll "$$version" api

# Serialize SDK invocations: contract/test outputs are shared, variant outputs are isolated.
check-package:
	@$(MAKE) check-package-standalone
	@$(MAKE) check-package-api

install: build-standalone
	@(cd dependencies && sha256sum --check SHA256SUMS)
	@install -Dm644 dependencies/Nivalis.ModCompanion.dll "$$GAME_PATH/BepInEx/plugins/Nivalis.ModCompanion.dll"
	@install -Dm644 bin/standalone/Nivalis.Cigarette.dll "$$GAME_PATH/BepInEx/plugins/Nivalis.Cigarette.dll"
	@rm -f -- "$$GAME_PATH/BepInEx/plugins/Cigarette.dll"

install-api: build-api
	@(cd dependencies && sha256sum --check SHA256SUMS)
	@install -Dm644 dependencies/Nivalis.ModCompanion.dll "$$GAME_PATH/BepInEx/plugins/Nivalis.ModCompanion.dll"
	@install -Dm644 bin/api/Nivalis.Cigarette.dll "$$GAME_PATH/BepInEx/plugins/Nivalis.Cigarette.dll"
	@install -Dm644 bin/api/Nivalis.Cigarette.Api.dll "$$GAME_PATH/BepInEx/plugins/Nivalis.Cigarette.Api.dll"
	@rm -f -- "$$GAME_PATH/BepInEx/plugins/Cigarette.dll"

package:
	@$(MAKE) package-standalone
	@$(MAKE) package-api

package-standalone: check-package-standalone
	@bash tools/package.sh standalone

package-api: check-package-api
	@bash tools/package.sh api

# Deployment requires a clean checkout; missing version tags are created after verification.
deploy: check-sdk check-game
	@bash tools/deploy.sh

check-release: check-sdk
	@bash tools/check_release.sh

clean:
	@rm -rf -- bin src/obj api/obj tests/bin tests/obj

# Opt-in local tooling. Never a dependency of build/install/package/deploy.
.PHONY: build-rail-dev install-rail-dev
build-rail-dev: build
	@"$$DOTNET" build dev/rail-tools/RailDev.csproj --configuration "$$CONFIGURATION" "-p:GamePath=$$(cd "$$GAME_PATH" && pwd)" --nologo

install-rail-dev: build-rail-dev
	@install -Dm644 bin/standalone/Nivalis.Cigarette.dll "$$GAME_PATH/BepInEx/plugins/Nivalis.Cigarette.dll"
	@install -Dm644 dev/rail-tools/bin/Nivalis.Cigarette.RailDev.dll "$$GAME_PATH/BepInEx/plugins/Nivalis.Cigarette.RailDev.dll"

.PHONY: test-rail-dev
test-rail-dev: check-sdk
	@"$$DOTNET" run --project dev/tests/RailDevTests.csproj --configuration "$$CONFIGURATION"

# Local API integration harness; never included in player archives.
.PHONY: build-api-test
build-api-test: build-api
	@"$$DOTNET" build dev/api-test/ApiTest.csproj --configuration "$$CONFIGURATION" "-p:GamePath=$$(cd "$$GAME_PATH" && pwd)" --nologo
