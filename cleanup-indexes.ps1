# PowerShell script to clean up unnecessary indexes
# Only keep the 3 expected indexes: project-memory, local-memory, and main workspace

$indexPath = ".codesearch\index"
$metadata = Get-Content "$indexPath\metadata.json" | ConvertFrom-Json

Write-Host "Current indexes:" -ForegroundColor Yellow
$metadata.Indexes | Get-Member -MemberType NoteProperty | ForEach-Object {
    $hash = $_.Name
    $info = $metadata.Indexes.$hash
    Write-Host "  $hash -> $($info.OriginalPath)"
}

# Indexes to keep
$keepPaths = @(
    ".codesearch\project-memory",
    ".codesearch\local-memory",
    "C:\source\COA Roslyn MCP"
)

$toDelete = @()
$metadata.Indexes | Get-Member -MemberType NoteProperty | ForEach-Object {
    $hash = $_.Name
    $info = $metadata.Indexes.$hash
    if ($keepPaths -notcontains $info.OriginalPath) {
        $toDelete += @{Hash=$hash; Path=$info.OriginalPath}
    }
}

if ($toDelete.Count -eq 0) {
    Write-Host "`nNo unnecessary indexes found." -ForegroundColor Green
    exit 0
}

Write-Host "`nIndexes to delete:" -ForegroundColor Red
$toDelete | ForEach-Object {
    Write-Host "  $($_.Hash) -> $($_.Path)"
}

$confirm = Read-Host "`nDo you want to delete these indexes? (y/n)"
if ($confirm -ne 'y') {
    Write-Host "Cancelled." -ForegroundColor Yellow
    exit 0
}

# Delete the indexes
$toDelete | ForEach-Object {
    $dir = Join-Path $indexPath $_.Hash
    if (Test-Path $dir) {
        Remove-Item $dir -Recurse -Force
        Write-Host "Deleted: $dir" -ForegroundColor Green
    }
}

# Update metadata to remove deleted entries
$newMetadata = @{Indexes = @{}}
$metadata.Indexes | Get-Member -MemberType NoteProperty | ForEach-Object {
    $hash = $_.Name
    $info = $metadata.Indexes.$hash
    if ($keepPaths -contains $info.OriginalPath) {
        $newMetadata.Indexes[$hash] = $info
    }
}

$newMetadata | ConvertTo-Json -Depth 10 | Set-Content "$indexPath\metadata.json"
Write-Host "`nMetadata updated." -ForegroundColor Green
Write-Host "Cleanup complete!" -ForegroundColor Green