#!/usr/bin/env pwsh
# Environment variable dump for debugging

$debugLog = Join-Path $PSScriptRoot "hook-debug.log"
Add-Content -Path $debugLog -Value "`n[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Environment dump:"

# Get all Claude-related environment variables
Get-ChildItem env: | Where-Object { $_.Name -like "*CLAUDE*" } | ForEach-Object {
    Add-Content -Path $debugLog -Value "$($_.Name)=$($_.Value)"
}

# Also check for other relevant variables
@("TEMP", "PWD", "PSScriptRoot") | ForEach-Object {
    $value = Get-Item "env:$_" -ErrorAction SilentlyContinue
    if ($value) {
        Add-Content -Path $debugLog -Value "$_=$($value.Value)"
    }
}

Add-Content -Path $debugLog -Value "Script location: $PSScriptRoot"
Add-Content -Path $debugLog -Value "Current directory: $(Get-Location)"

exit 0