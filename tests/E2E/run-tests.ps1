# Calor E2E Test Runner for Windows
# Runs all scenarios in tests/E2E/scenarios/

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$Compiler = Join-Path $RepoRoot "src\Calor.Compiler\bin\Debug\net8.0\calor.exe"
$ScenariosDir = Join-Path $ScriptDir "scenarios"

$Script:Passed = 0
$Script:Failed = 0
$Script:Skipped = 0

function Write-Info { param($Message) Write-Host "[INFO] $Message" -ForegroundColor Blue }
function Write-Pass { param($Message) Write-Host "[PASS] $Message" -ForegroundColor Green; $Script:Passed++ }
function Write-Fail { param($Message) Write-Host "[FAIL] $Message" -ForegroundColor Red; $Script:Failed++ }
function Write-Skip { param($Message) Write-Host "[SKIP] $Message" -ForegroundColor Yellow; $Script:Skipped++ }

function Build-Compiler {
    Write-Info "Building Calor compiler..."
    Push-Location $RepoRoot
    try {
        dotnet build src/Calor.Compiler/Calor.Compiler.csproj -c Debug --nologo -v q
        if (-not $?) { throw "Build failed" }
    }
    finally {
        Pop-Location
    }
    Write-Info "Compiler built successfully"
    Write-Host ""
}

function Invoke-Scenario {
    param($ScenarioDir)

    $scenarioName = Split-Path -Leaf $ScenarioDir
    $inputFile = Join-Path $ScenarioDir "input.calr"
    $runScript = Join-Path $ScenarioDir "run.ps1"
    $verifyScript = Join-Path $ScenarioDir "verify.ps1"
    $outputFile = Join-Path $ScenarioDir "output.g.cs"

    # Check for required files
    if (-not (Test-Path $inputFile) -and -not (Test-Path $runScript)) {
        Write-Skip "$scenarioName - no input.calr or run.ps1"
        return
    }

    Write-Host "Running: $scenarioName" -ForegroundColor Blue

    # Use custom run.ps1 if present, otherwise default compile
    if (Test-Path $runScript) {
        try {
            & $runScript $ScenarioDir $Compiler
            if (-not $?) { throw "run.ps1 failed" }
        }
        catch {
            Write-Fail "$scenarioName - run.ps1 failed"
            return
        }
    }
    elseif (Test-Path $inputFile) {
        # Compile Calor to C#
        try {
            & $Compiler --input $inputFile --output $outputFile 2>$null
            if (-not $?) { throw "Compilation failed" }
        }
        catch {
            Write-Fail "$scenarioName - compilation failed"
            return
        }
    }

    # Run verification script if present
    if (Test-Path $verifyScript) {
        try {
            & $verifyScript $ScenarioDir
            if (-not $?) { throw "Verification failed" }
        }
        catch {
            Write-Fail "$scenarioName - verification failed"
            return
        }
    }

    Write-Pass $scenarioName
}

function Clear-GeneratedFiles {
    Write-Info "Cleaning up generated files..."
    Get-ChildItem -Path $ScenariosDir -Filter "*.g.cs" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $ScenariosDir -Filter ".workdir" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $ScenariosDir -Directory -Filter "bin" -Recurse | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $ScenariosDir -Directory -Filter "obj" -Recurse | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

function Write-Summary {
    Write-Host ""
    Write-Host "================================"
    Write-Host "E2E Test Summary"
    Write-Host "================================"
    Write-Host "Passed:  $Script:Passed" -ForegroundColor Green
    Write-Host "Failed:  $Script:Failed" -ForegroundColor Red
    Write-Host "Skipped: $Script:Skipped" -ForegroundColor Yellow
    Write-Host "================================"

    if ($Script:Failed -gt 0) {
        exit 1
    }
}

function Main {
    param($Args)

    Write-Host ""
    Write-Host "Calor E2E Test Suite"
    Write-Host "===================="
    Write-Host ""

    # Parse arguments
    $cleanOnly = $false
    foreach ($arg in $Args) {
        switch ($arg) {
            "--clean" { $cleanOnly = $true }
            "--help" {
                Write-Host "Usage: .\run-tests.ps1 [--clean] [--help]"
                Write-Host "  --clean  Clean generated files only"
                Write-Host "  --help   Show this help"
                exit 0
            }
        }
    }

    if ($cleanOnly) {
        Clear-GeneratedFiles
        Write-Info "Cleanup complete"
        exit 0
    }

    Build-Compiler

    # Run each scenario
    $scenarios = Get-ChildItem -Path $ScenariosDir -Directory | Sort-Object Name
    foreach ($scenario in $scenarios) {
        Invoke-Scenario $scenario.FullName
    }

    Write-Summary
}

Main $args
