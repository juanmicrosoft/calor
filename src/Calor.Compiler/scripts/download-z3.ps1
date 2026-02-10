# Downloads Z3 managed assembly and native libraries for all supported platforms
# The managed Microsoft.Z3.dll must come from the same Z3 release as the native libraries
# to ensure compatibility (NuGet package 4.12.2 doesn't work with Z3 4.15.7 natives)
$ErrorActionPreference = "Stop"

$Z3_VERSION = "4.15.7"
$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
$RUNTIMES_DIR = Join-Path $SCRIPT_DIR "..\runtimes"
$Z3_DIR = Join-Path $SCRIPT_DIR "..\z3"
$TEMP_DIR = Join-Path $SCRIPT_DIR "..\.z3-temp"

# Platform mappings
$PLATFORMS = @(
    @{ rid = "osx-arm64"; archive = "z3-$Z3_VERSION-arm64-osx-15.7.3"; lib = "libz3.dylib" },
    @{ rid = "osx-x64"; archive = "z3-$Z3_VERSION-x64-osx-15.7.3"; lib = "libz3.dylib" },
    @{ rid = "win-arm64"; archive = "z3-$Z3_VERSION-arm64-win"; lib = "libz3.dll" },
    @{ rid = "win-x64"; archive = "z3-$Z3_VERSION-x64-win"; lib = "libz3.dll" },
    @{ rid = "win-x86"; archive = "z3-$Z3_VERSION-x86-win"; lib = "libz3.dll" },
    @{ rid = "linux-arm64"; archive = "z3-$Z3_VERSION-arm64-glibc-2.38"; lib = "libz3.so" },
    @{ rid = "linux-x64"; archive = "z3-$Z3_VERSION-x64-glibc-2.39"; lib = "libz3.so" }
)

# Use one platform to get the managed DLL (it's the same across all platforms)
$MANAGED_DLL_ARCHIVE = "z3-$Z3_VERSION-x64-win"

$BASE_URL = "https://github.com/Z3Prover/z3/releases/download/z3-$Z3_VERSION"

Write-Host "Z3 Library Downloader"
Write-Host "====================="
Write-Host "Version: $Z3_VERSION"
Write-Host ""

# Check if managed DLL exists
$managedDllPath = Join-Path $Z3_DIR "Microsoft.Z3.dll"
$managedDllExists = Test-Path $managedDllPath

# Check if all native libraries already exist
$allNativesExist = $true
foreach ($platform in $PLATFORMS) {
    $targetFile = Join-Path $RUNTIMES_DIR "$($platform.rid)\native\$($platform.lib)"
    if (-not (Test-Path $targetFile)) {
        $allNativesExist = $false
        break
    }
}

if ($managedDllExists -and $allNativesExist) {
    Write-Host "All Z3 libraries already present. Skipping download."
    exit 0
}

# Create directories
New-Item -ItemType Directory -Force -Path $TEMP_DIR | Out-Null
New-Item -ItemType Directory -Force -Path $Z3_DIR | Out-Null

# Download managed DLL if needed
if (-not $managedDllExists) {
    Write-Host "[Managed] Downloading Microsoft.Z3.dll..."

    $zipFile = Join-Path $TEMP_DIR "$MANAGED_DLL_ARCHIVE.zip"

    # Download if not cached
    if (-not (Test-Path $zipFile)) {
        Invoke-WebRequest -Uri "$BASE_URL/$MANAGED_DLL_ARCHIVE.zip" -OutFile $zipFile
    }

    # Extract
    Expand-Archive -Path $zipFile -DestinationPath $TEMP_DIR -Force

    # Find and copy Microsoft.Z3.dll
    $foundDll = Get-ChildItem -Path $TEMP_DIR -Recurse -Filter "Microsoft.Z3.dll" | Select-Object -First 1
    if ($foundDll) {
        Copy-Item -Path $foundDll.FullName -Destination $managedDllPath -Force
        Write-Host "[Managed] Done."
    } else {
        Write-Warning "[Managed] Could not find Microsoft.Z3.dll in archive"
    }
}

# Download native libraries
foreach ($platform in $PLATFORMS) {
    $rid = $platform.rid
    $archive = $platform.archive
    $lib = $platform.lib

    $targetDir = Join-Path $RUNTIMES_DIR "$rid\native"
    $targetFile = Join-Path $targetDir $lib

    if (Test-Path $targetFile) {
        Write-Host "[$rid] Already exists, skipping."
        continue
    }

    Write-Host "[$rid] Downloading..."

    $zipFile = Join-Path $TEMP_DIR "$archive.zip"
    $extractDir = Join-Path $TEMP_DIR $archive

    # Download if not cached
    if (-not (Test-Path $zipFile)) {
        Invoke-WebRequest -Uri "$BASE_URL/$archive.zip" -OutFile $zipFile
    }

    # Extract
    Expand-Archive -Path $zipFile -DestinationPath $TEMP_DIR -Force

    # Create target directory
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

    # Find and copy the library
    $foundLib = Get-ChildItem -Path $TEMP_DIR -Recurse -Filter $lib | Select-Object -First 1
    if ($foundLib) {
        Copy-Item -Path $foundLib.FullName -Destination $targetFile -Force
        Write-Host "[$rid] Done."
    } else {
        Write-Warning "[$rid] Could not find $lib in archive"
    }
}

# Cleanup
Remove-Item -Path $TEMP_DIR -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Z3 libraries ready."
Write-Host "  Managed: $managedDllPath"
Write-Host "  Natives: $RUNTIMES_DIR\*\native\"
