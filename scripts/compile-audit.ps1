param([string]$Dir)

$files = Get-ChildItem -Path $Dir -Filter "*.calr" -Recurse
$pass = 0
$fail = 0
$failFiles = @()
$errorPatterns = @{}

foreach ($f in $files) {
    $output = "" | & calor -i $f.FullName --permissive-effects 2>&1 | Out-String
    $hasError = $output -match 'error Calor\d+'
    if (-not $hasError) {
        $pass++
    } else {
        $fail++
        $relPath = $f.FullName.Substring($Dir.Length).TrimStart('\')
        # Extract first error line
        $firstErr = ($output -split "`n" | Where-Object { $_ -match 'error Calor' } | Select-Object -First 1).Trim()
        $failFiles += "$relPath | $firstErr"
        
        # Count error codes
        $codes = [regex]::Matches($output, 'error (Calor\d+)')
        foreach ($m in $codes) {
            $code = $m.Groups[1].Value
            if ($errorPatterns.ContainsKey($code)) { $errorPatterns[$code]++ } else { $errorPatterns[$code] = 1 }
        }
    }
}

Write-Host "=== RESULTS ==="
Write-Host "PASS: $pass  FAIL: $fail  TOTAL: $($pass + $fail)"
Write-Host ""
Write-Host "=== ERROR CODE DISTRIBUTION ==="
$errorPatterns.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { Write-Host "  $($_.Key): $($_.Value)" }
Write-Host ""
Write-Host "=== FAILING FILES ==="
foreach ($line in $failFiles) { Write-Host "  $line" }
