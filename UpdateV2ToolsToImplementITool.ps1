# PowerShell script to update V2 tool classes to override ITool properties
# Run this from the COA.CodeSearch.McpServer directory

$v2ToolsInfo = @{
    "FindReferencesToolV2" = @{ Name = "find_references_v2"; Desc = "AI-optimized reference finding with insights"; Category = "Navigation" }
    "SearchSymbolsToolV2" = @{ Name = "search_symbols_v2"; Desc = "AI-optimized symbol search with structured response"; Category = "Search" }
    "FastTextSearchToolV2" = @{ Name = "fast_text_search_v2"; Desc = "AI-optimized text search with insights"; Category = "Search" }
    "FastFileSearchToolV2" = @{ Name = "fast_file_search_v2"; Desc = "AI-optimized file search with hotspots"; Category = "Search" }
    "GetDiagnosticsToolV2" = @{ Name = "get_diagnostics_v2"; Desc = "AI-optimized diagnostics with categorization"; Category = "Analysis" }
    "DependencyAnalysisToolV2" = @{ Name = "dependency_analysis_v2"; Desc = "AI-optimized dependency analysis"; Category = "Analysis" }
    "ProjectStructureAnalysisToolV2" = @{ Name = "project_structure_analysis_v2"; Desc = "AI-optimized project analysis"; Category = "Analysis" }
    "GetImplementationsToolV2" = @{ Name = "get_implementations_v2"; Desc = "AI-optimized implementation discovery"; Category = "Analysis" }
    "GetCallHierarchyToolV2" = @{ Name = "get_call_hierarchy_v2"; Desc = "AI-optimized call hierarchy analysis"; Category = "Analysis" }
    "RenameSymbolToolV2" = @{ Name = "rename_symbol_v2"; Desc = "AI-optimized symbol renaming with risk assessment"; Category = "Refactoring" }
    "BatchOperationsToolV2" = @{ Name = "batch_operations_v2"; Desc = "AI-optimized batch operations"; Category = "Batch" }
    "FlexibleMemorySearchToolV2" = @{ Name = "flexible_memory_search_v2"; Desc = "AI-optimized memory search"; Category = "Memory" }
}

function Update-V2ToolFile {
    param(
        [string]$ClassName,
        [hashtable]$Info
    )
    
    $filePath = "Tools\$ClassName.cs"
    if (-not (Test-Path $filePath)) {
        Write-Host "File not found: $filePath" -ForegroundColor Yellow
        return
    }
    
    $content = Get-Content $filePath -Raw
    
    # Check if already has override properties
    if ($content -match "public override string ToolName") {
        Write-Host "$ClassName already has ITool overrides" -ForegroundColor Green
        return
    }
    
    # Pattern to match class declaration  
    $pattern = "(public\s+class\s+$ClassName\s*:\s*ClaudeOptimizedToolBase[^\{]*\{)"
    
    $replacement = @"
`$1
    public override string ToolName => "$($Info.Name)";
    public override string Description => "$($Info.Desc)";
    public override ToolCategory Category => ToolCategory.$($Info.Category);
"@
    
    $newContent = $content -replace $pattern, $replacement
    
    # Save the file
    Set-Content $filePath $newContent -NoNewline
    Write-Host "Updated $ClassName" -ForegroundColor Green
}

# Process each V2 tool
foreach ($tool in $v2ToolsInfo.GetEnumerator()) {
    Update-V2ToolFile -ClassName $tool.Key -Info $tool.Value
}

Write-Host "`nDone updating V2 tools!" -ForegroundColor Cyan