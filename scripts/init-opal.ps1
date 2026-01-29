# OPAL Initialization Script for Windows
# Usage: irm https://raw.githubusercontent.com/juanmicrosoft/opal/main/scripts/init-opal.ps1 | iex

$ErrorActionPreference = "Stop"

$Version = "0.1.0"
$RepoUrl = "https://github.com/juanmicrosoft/opal"
$RawUrl = "https://raw.githubusercontent.com/juanmicrosoft/opal/main"

function Write-Banner {
    Write-Host @"

   ____  _____       _
  / __ \|  __ \ /\  | |
 | |  | | |__) /  \ | |
 | |  | |  ___/ /\ \| |
 | |__| | |  / ____ \ |____
  \____/|_| /_/    \_\_____|

  Optimized Programming for Agent Logic
  Version $Version

"@ -ForegroundColor Cyan
}

function Write-Info { param($Message) Write-Host "[INFO] $Message" -ForegroundColor Blue }
function Write-Success { param($Message) Write-Host "[OK] $Message" -ForegroundColor Green }
function Write-Warn { param($Message) Write-Host "[WARN] $Message" -ForegroundColor Yellow }
function Write-Error-Exit { param($Message) Write-Host "[ERROR] $Message" -ForegroundColor Red; exit 1 }

function Test-Command {
    param($Command)
    $null = Get-Command $Command -ErrorAction SilentlyContinue
    return $?
}

function Test-DotNetVersion {
    if (-not (Test-Command "dotnet")) {
        return $false
    }

    try {
        $version = dotnet --version 2>$null
        $major = [int]($version -split '\.')[0]
        return $major -ge 8
    }
    catch {
        return $false
    }
}

function Test-Prerequisites {
    Write-Info "Checking prerequisites..."

    Write-Success "Detected OS: Windows"

    # Check .NET SDK
    if (Test-DotNetVersion) {
        $version = dotnet --version
        Write-Success ".NET SDK $version installed"
    }
    else {
        Write-Error-Exit ".NET SDK 8.0+ is required. Install from https://dotnet.microsoft.com/download"
    }

    # Check git
    if (Test-Command "git") {
        Write-Success "git installed"
    }
    else {
        Write-Warn "git not found - some features may not work"
    }

    Write-Host ""
}

function Install-Opalc {
    Write-Info "Installing opalc global tool..."

    # Check if already installed
    $toolList = dotnet tool list -g 2>$null
    if ($toolList -match "^opalc\s") {
        Write-Warn "opalc is already installed. Updating..."
        try {
            dotnet tool update -g opalc 2>$null
            Write-Success "opalc updated successfully"
            return
        }
        catch {
            Write-Info "Tool not yet published to NuGet. Building from source..."
            Install-FromSource
            return
        }
    }

    try {
        dotnet tool install -g opalc 2>$null
        Write-Success "opalc installed successfully"
    }
    catch {
        Write-Info "Tool not yet published to NuGet. Building from source..."
        Install-FromSource
    }
}

function Install-FromSource {
    $tempDir = Join-Path $env:TEMP "opal-install-$(Get-Random)"

    try {
        Write-Info "Cloning OPAL repository..."
        git clone --depth 1 $RepoUrl $tempDir 2>$null

        if (-not (Test-Path (Join-Path $tempDir "src"))) {
            Write-Warn "Could not clone from GitHub. This is expected if the repo is not public yet."
            Write-Warn "You can manually install opalc by running:"
            Write-Host "  git clone $RepoUrl"
            Write-Host "  cd opal"
            Write-Host "  dotnet pack src\Opal.Compiler\Opal.Compiler.csproj -c Release -o .\nupkg"
            Write-Host "  dotnet tool install -g --add-source .\nupkg opalc"
            return
        }

        Write-Info "Building opalc..."
        Push-Location $tempDir
        dotnet pack src/Opal.Compiler/Opal.Compiler.csproj -c Release -o ./nupkg
        if (-not $?) { throw "Build failed" }

        Write-Info "Installing from local package..."
        try {
            dotnet tool install -g --add-source ./nupkg opalc 2>$null
        }
        catch {
            dotnet tool update -g --add-source ./nupkg opalc 2>$null
        }

        Pop-Location
        Write-Success "opalc installed from source"
    }
    finally {
        if (Test-Path $tempDir) {
            Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
        }
    }
}

function Install-ClaudeSkills {
    Write-Info "Setting up Claude Code skills..."

    # Create .claude/skills directory
    $skillsDir = ".claude\skills"
    if (-not (Test-Path $skillsDir)) {
        New-Item -ItemType Directory -Path $skillsDir -Force | Out-Null
    }

    # Download skill files
    try {
        Invoke-WebRequest -Uri "$RawUrl/.claude/skills/opal.md" -OutFile "$skillsDir\opal.md" -UseBasicParsing
        Invoke-WebRequest -Uri "$RawUrl/.claude/skills/opal-convert.md" -OutFile "$skillsDir\opal-convert.md" -UseBasicParsing
        Write-Success "Claude Code skills installed in $skillsDir\"
    }
    catch {
        Write-Warn "Could not download skill files: $_"
    }
}

function New-SampleProject {
    if ((Test-Path "*.opal") -or (Test-Path "samples")) {
        Write-Info "Project files already exist. Skipping sample creation."
        return
    }

    Write-Info "Creating sample OPAL project..."

    # Create src directory
    if (-not (Test-Path "src")) {
        New-Item -ItemType Directory -Path "src" | Out-Null
    }

    # Create hello.opal
    @"
`§M[m001:Hello]
`§F[f001:Main:pub]
  `§O[void]
  `§E[cw]
  `§P "Hello from OPAL!"
`§/F[f001]
`§/M[m001]
"@ | Set-Content -Path "src\hello.opal" -Encoding UTF8

    # Create project file
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
"@ | Set-Content -Path "src\Hello.csproj" -Encoding UTF8

    Write-Success "Sample project created in src\"
    Write-Host ""
    Write-Info "To compile and run:"
    Write-Host "  opalc --input src\hello.opal --output src\hello.g.cs"
    Write-Host "  dotnet run --project src\Hello.csproj"
}

function Write-NextSteps {
    Write-Host ""
    Write-Host "=== Setup Complete ===" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "  1. Run 'opalc --help' to see compiler options"
    Write-Host "  2. Use '/opal' in Claude Code to write OPAL code"
    Write-Host "  3. Use '/opal-convert' to convert C# to OPAL"
    Write-Host ""
    Write-Host "Documentation: $RepoUrl"
    Write-Host ""
}

function Main {
    Write-Banner
    Test-Prerequisites
    Install-Opalc
    Install-ClaudeSkills
    New-SampleProject
    Write-NextSteps
}

Main
