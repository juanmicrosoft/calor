# Pre-commit hook (Windows PowerShell): enforces the append-only invariant on
# docs/experiments/registry.json. Sister script to .githooks/pre-commit-registry.
#
# Install (Windows):
#   git config core.hooksPath .githooks
# Then create .git/hooks/pre-commit.ps1 that invokes this file, or use a bash
# pre-commit wrapper via Git Bash.
#
# CI is the authoritative enforcement layer; this hook is for fast local feedback.

$ErrorActionPreference = "Stop"
$RegistryPath = "docs/experiments/registry.json"

# Only run if registry.json is staged.
$staged = git diff --cached --name-only | Select-String -Pattern "^$([regex]::Escape($RegistryPath))$" -Quiet
if (-not $staged) {
    exit 0
}

Write-Host "[pre-commit-registry] Validating append-only invariant on $RegistryPath..."

$baseFile = New-TemporaryFile
$headFile = New-TemporaryFile
try {
    # Base: HEAD (last committed version).
    $baseContent = git show "HEAD:$RegistryPath" 2>$null
    if (-not $baseContent) { $baseContent = '{"entries":[]}' }
    Set-Content -Path $baseFile -Value $baseContent -NoNewline

    # Head: staged version.
    $headContent = git show ":$RegistryPath"
    Set-Content -Path $headFile -Value $headContent -NoNewline

    # Prefer the `calor` global tool if installed; fall back to dotnet run.
    if (Get-Command calor -ErrorAction SilentlyContinue) {
        calor evaluation registry-validate --base-file $baseFile --head-file $headFile
    } else {
        dotnet run --project src/Calor.Compiler -- evaluation registry-validate `
            --base-file $baseFile --head-file $headFile
    }

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
} finally {
    Remove-Item -Force -ErrorAction SilentlyContinue $baseFile, $headFile
}
