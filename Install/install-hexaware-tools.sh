#!/usr/bin/env bash
set -euo pipefail

dotnet pack ./HexGenerate/HexGenerate.csproj -c Release
dotnet pack ./HexQuery/HexQuery.csproj -c Release

dotnet tool install --global --add-source ./HexGenerate/nupkg HexAware.HexGenerate
dotnet tool install --global --add-source ./HexQuery/nupkg HexAware.HexQuery

echo ""
dotnet tool list --global
