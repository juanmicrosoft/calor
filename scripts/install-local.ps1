# Local Install Script for opalc (Windows)
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

Write-Host "Building opalc..."
dotnet build src/Opal.Compiler/Opal.Compiler.csproj -c Release

Write-Host "Packing..."
dotnet pack src/Opal.Compiler/Opal.Compiler.csproj -c Release -o ./nupkg

Write-Host "Installing globally..."
try {
    dotnet tool install -g --add-source ./nupkg opalc
} catch {
    dotnet tool update -g --add-source ./nupkg opalc
}

Write-Host "Verifying..."
opalc --help

Write-Host "Done! opalc installed globally."
