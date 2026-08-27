#!/usr/bin/env bash
set -euo pipefail

dotnet tool uninstall --global HexAware.HexGenerate || true
dotnet tool uninstall --global HexAware.HexQuery || true

echo ""
dotnet tool list --global
