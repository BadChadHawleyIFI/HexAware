$ErrorActionPreference = "Stop"

Write-Host "Removing HexAware.HexGenerate..."
dotnet tool uninstall --global HexAware.HexGenerate

Write-Host "Removing HexAware.HexQuery..."
dotnet tool uninstall --global HexAware.HexQuery

Write-Host ""
Write-Host "Remaining global tools:"
dotnet tool list --global
