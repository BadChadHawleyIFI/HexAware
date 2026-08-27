$ErrorActionPreference = "Stop"

Write-Host "Packing HexGenerate..."
dotnet pack .\HexGenerate\HexGenerate.csproj -c Release

Write-Host "Packing HexQuery..."
dotnet pack .\HexQuery\HexQuery.csproj -c Release

Write-Host "Installing HexAware.HexGenerate..."
dotnet tool install --global --add-source .\HexGenerate\nupkg HexAware.HexGenerate

Write-Host "Installing HexAware.HexQuery..."
dotnet tool install --global --add-source .\HexQuery\nupkg HexAware.HexQuery

Write-Host ""
Write-Host "Installed tools:"
dotnet tool list --global
