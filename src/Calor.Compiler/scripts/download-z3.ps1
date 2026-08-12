# Downloads Z3 managed assembly and native libraries for all supported platforms
# The managed Microsoft.Z3.dll must come from the same Z3 release as the native libraries
# to ensure compatibility (NuGet package 4.12.2 doesn't work with Z3 4.15.7 natives)
$ErrorActionPreference = "Stop"

$Z3_VERSION = "4.15.7"
$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
$RUNTIMES_DIR = Join-Path $SCRIPT_DIR "..\runtimes"

# Z3 chain closure (#916 review F2): purge the dropped osx-x64 RID unconditionally —
# the provenance stamp does not invalidate on RID-set changes, and stale natives
# would be resurrected into local packs by the runtimes/** glob.
Remove-Item -Recurse -Force (Join-Path $RUNTIMES_DIR "osx-x64") -ErrorAction SilentlyContinue
$Z3_DIR = Join-Path $SCRIPT_DIR "..\z3"
$TEMP_DIR = Join-Path $SCRIPT_DIR "..\.z3-temp"

# W1 Slice 2 (#789, #834 review M3): verify every downloaded archive against
# the committed SHA-256 manifest before extracting anything from it.
$CHECKSUM_MANIFEST = Join-Path $SCRIPT_DIR "z3-upstream-$Z3_VERSION.sha256"
function Invoke-Download {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$OutFile,
        [int]$MaxAttempts = 8
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            Invoke-WebRequest -Uri $Uri -OutFile $OutFile
            return
        } catch {
            Remove-Item -Force $OutFile -ErrorAction SilentlyContinue
            if ($attempt -eq $MaxAttempts) {
                throw
            }

            $delaySeconds = [Math]::Min(30, [Math]::Pow(2, $attempt - 1))
            Write-Warning "Download attempt $attempt/$MaxAttempts failed: $($_.Exception.Message)"
            Write-Host "Retrying in $delaySeconds second(s)..."
            Start-Sleep -Seconds $delaySeconds
        }
    }
}

function Test-ArchiveChecksum {
    param([string]$ZipFile)
    $name = Split-Path -Leaf $ZipFile
    if (-not (Test-Path $CHECKSUM_MANIFEST)) {
        throw "Checksum manifest $CHECKSUM_MANIFEST missing - refusing to use unverified Z3 archives"
    }
    $entry = (Get-Content $CHECKSUM_MANIFEST |
        Where-Object { $_ -notmatch '^#' -and (($_ -split '\s+')[1]) -eq $name } |
        Select-Object -First 1)
    if (-not $entry) {
        throw "No checksum entry for $name in $CHECKSUM_MANIFEST"
    }
    $parts = $entry -split '\s+'
    $expected = $parts[0]
    $expectedSize = [int64]$parts[2]
    $actualSize = (Get-Item $ZipFile).Length
    if ($actualSize -ne $expectedSize) {
        Remove-Item -Force $ZipFile
        throw "Unexpected size for ${name}: expected $expectedSize, got $actualSize"
    }
    $actual = (Get-FileHash -Algorithm SHA256 -Path $ZipFile).Hash.ToLowerInvariant()
    if ($actual -ne $expected.ToLowerInvariant()) {
        Remove-Item -Force $ZipFile
        throw "Checksum mismatch for ${name}: expected $expected, got $actual"
    }
    Write-Host "  [verified] $name"
}

# Platform mappings
$PLATFORMS = @(
    @{ rid = "osx-arm64"; archive = "z3-$Z3_VERSION-arm64-osx-15.7.3"; lib = "libz3.dylib" },
    # osx-x64 removed (Z3 chain closure): upstream x64-osx archive contains an arm64 binary.
    @{ rid = "win-arm64"; archive = "z3-$Z3_VERSION-arm64-win"; lib = "libz3.dll" },
    @{ rid = "win-x64"; archive = "z3-$Z3_VERSION-x64-win"; lib = "libz3.dll" },
    @{ rid = "win-x86"; archive = "z3-$Z3_VERSION-x86-win"; lib = "libz3.dll" },
    @{ rid = "linux-arm64"; archive = "z3-$Z3_VERSION-arm64-glibc-2.38"; lib = "libz3.so" },
    @{ rid = "linux-x64"; archive = "z3-$Z3_VERSION-x64-glibc-2.39"; lib = "libz3.so" }
)

# One archive supplies the managed wrapper for every platform. It is NOT "the
# same across all platforms" as previously assumed: upstream's x64-win archive
# ships a PE32+/AMD64 Microsoft.Z3.dll that fails to load in an arm64 process,
# whereas the glibc archives ship the ordinary PE32/I386 AnyCPU shape. The
# wrapper is pure P/Invoke (the per-RID native libz3 carries the platform
# difference), so the architecture-neutral one is correct everywhere — and it is
# the only choice that works on win-arm64. Keep in sync with MANAGED_DLL_ARCHIVE
# in download-z3.sh and .github/workflows/build-z3.yml.
$MANAGED_DLL_ARCHIVE = "z3-$Z3_VERSION-x64-glibc-2.39"

$BASE_URL = "https://github.com/Z3Prover/z3/releases/download/z3-$Z3_VERSION"

Write-Host "Z3 Library Downloader"
Write-Host "====================="
Write-Host "Version: $Z3_VERSION"
Write-Host ""

# Existing outputs are reusable only if every binary matches the committed
# release-asset size/hash manifest.
$REPO_ROOT = (Resolve-Path (Join-Path $SCRIPT_DIR "..\..\..")).Path
$ASSET_VERIFIER = Join-Path $REPO_ROOT "scripts\verify-z3-assets.py"
if ((Test-Path (Join-Path $Z3_DIR "Microsoft.Z3.dll")) -or (Test-Path $RUNTIMES_DIR)) {
    & python $ASSET_VERIFIER *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Existing Z3 assets failed committed size/hash verification."
        Write-Host "Discarding them and refetching verified upstream archives."
        Remove-Item -Path $Z3_DIR -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -Path $RUNTIMES_DIR -Recurse -Force -ErrorAction SilentlyContinue
    }
}

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
    & python $ASSET_VERIFIER --write-provenance
    if ($LASTEXITCODE -ne 0) { throw "Z3 output verification failed" }
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
        Invoke-Download -Uri "$BASE_URL/$MANAGED_DLL_ARCHIVE.zip" -OutFile $zipFile
    }
    Test-ArchiveChecksum -ZipFile $zipFile

    # Extract
    Expand-Archive -Path $zipFile -DestinationPath $TEMP_DIR -Force

    # Search only THIS archive's extract. Several archives contain a
    # Microsoft.Z3.dll and they are not interchangeable — the x64-win one is
    # PE32+/AMD64 and cannot load in an arm64 process — so a recursive search
    # over the shared temp dir could pick a wrapper from a different archive
    # depending only on enumeration order.
    $managedExtract = Join-Path $TEMP_DIR $MANAGED_DLL_ARCHIVE
    $foundDll = Get-ChildItem -Path $managedExtract -Recurse -Filter "Microsoft.Z3.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($foundDll) {
        Copy-Item -Path $foundDll.FullName -Destination $managedDllPath -Force
        Write-Host "[Managed] Done."
    } else {
        throw "Could not find Microsoft.Z3.dll in $MANAGED_DLL_ARCHIVE"
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
        Invoke-Download -Uri "$BASE_URL/$archive.zip" -OutFile $zipFile
    }
    Test-ArchiveChecksum -ZipFile $zipFile

    # Extract
    Expand-Archive -Path $zipFile -DestinationPath $TEMP_DIR -Force

    # Create target directory
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

    # Search only THIS archive's extract, not the whole temp dir. The library
    # names are not unique across archives (three ship a libz3.dll, two a
    # libz3.so, two a libz3.dylib), so a shared-directory search could install
    # another RID's binary here — e.g. an arm64 libz3.dll under win-x64. The
    # checksum guard would not catch it: it verifies the ZIP, not the extract.
    $foundLib = Get-ChildItem -Path $extractDir -Recurse -Filter $lib -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($foundLib) {
        Copy-Item -Path $foundLib.FullName -Destination $targetFile -Force
        Write-Host "[$rid] Done."
    } else {
        throw "Could not find $lib in $archive"
    }
}

# Verify the extracted outputs themselves and record their source/manifests.
& python $ASSET_VERIFIER --write-provenance
if ($LASTEXITCODE -ne 0) { throw "Z3 output verification failed" }

# Cleanup
Remove-Item -Path $TEMP_DIR -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Z3 libraries ready."
Write-Host "  Managed: $managedDllPath"
Write-Host "  Natives: $RUNTIMES_DIR\*\native\"
