$ErrorActionPreference = "Stop"

Write-Host "Running Calor Enforcement Tests..." -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan

# Run enforcement-specific tests
Write-Host ""
Write-Host "1. Running enforcement tests..." -ForegroundColor Yellow
dotnet test tests/Calor.Enforcement.Tests -c Release --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Run existing compiler tests to ensure no regressions
Write-Host ""
Write-Host "2. Running compiler tests..." -ForegroundColor Yellow
dotnet test tests/Calor.Compiler.Tests -c Release --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Run evaluation tests
Write-Host ""
Write-Host "3. Running evaluation tests..." -ForegroundColor Yellow
dotnet test tests/Calor.Evaluation -c Release --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "==================================" -ForegroundColor Cyan
Write-Host "All tests passed!" -ForegroundColor Green
