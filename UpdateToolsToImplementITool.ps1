# PowerShell script to update all tool classes to implement ITool interface
# Run this from the COA.CodeSearch.McpServer directory

$toolsInfo = @{
    # Navigation tools
    "FindReferencesTool" = @{ Name = "find_references"; Desc = "Find all references to a symbol across the codebase"; Category = "Navigation" }
    "GetCallHierarchyTool" = @{ Name = "get_call_hierarchy"; Desc = "Trace method call chains to understand execution flow"; Category = "Navigation" }
    "GetImplementationsTool" = @{ Name = "get_implementations"; Desc = "Discover all concrete implementations of interfaces or abstract classes"; Category = "Navigation" }
    
    # Search tools
    "SearchSymbolsTool" = @{ Name = "search_symbols"; Desc = "Search for C# symbols by name with wildcards and fuzzy matching"; Category = "Search" }
    "FastTextSearchTool" = @{ Name = "fast_text_search"; Desc = "Blazing fast text search across millions of lines using Lucene"; Category = "Search" }
    "FastFileSearchTool" = @{ Name = "fast_file_search"; Desc = "Find files by name with fuzzy matching and typo correction"; Category = "Search" }
    "FastDirectorySearchTool" = @{ Name = "fast_directory_search"; Desc = "Search for directories with fuzzy matching"; Category = "Search" }
    "FastRecentFilesTool" = @{ Name = "fast_recent_files"; Desc = "Find recently modified files using indexed timestamps"; Category = "Search" }
    "FastSimilarFilesTool" = @{ Name = "fast_similar_files"; Desc = "Find files with similar content using 'More Like This'"; Category = "Search" }
    
    # Analysis tools
    "GetDiagnosticsTool" = @{ Name = "get_diagnostics"; Desc = "Check for compilation errors and warnings"; Category = "Analysis" }
    "DependencyAnalysisTool" = @{ Name = "dependency_analysis"; Desc = "Analyze code dependencies and coupling"; Category = "Analysis" }
    "ProjectStructureAnalysisTool" = @{ Name = "project_structure_analysis"; Desc = "Analyze project structure with metrics"; Category = "Analysis" }
    "GetDocumentSymbolsTool" = @{ Name = "get_document_symbols"; Desc = "Get outline of all symbols in a file"; Category = "Analysis" }
    "FastFileSizeAnalysisTool" = @{ Name = "fast_file_size_analysis"; Desc = "Analyze files by size and distribution"; Category = "Analysis" }
    "GetHoverInfoTool" = @{ Name = "get_hover_info"; Desc = "Get detailed type information and documentation"; Category = "Analysis" }
    
    # TypeScript tools
    "TypeScriptGoToDefinitionTool" = @{ Name = "typescript_go_to_definition"; Desc = "Navigate to TypeScript definitions using tsserver"; Category = "TypeScript" }
    "TypeScriptFindReferencesTool" = @{ Name = "typescript_find_references"; Desc = "Find all references to TypeScript symbols"; Category = "TypeScript" }
    "TypeScriptSearchTool" = @{ Name = "search_typescript"; Desc = "Search for TypeScript symbols across codebase"; Category = "TypeScript" }
    "TypeScriptHoverInfoTool" = @{ Name = "typescript_hover_info"; Desc = "Get TypeScript type information and docs"; Category = "TypeScript" }
    "TypeScriptRenameTool" = @{ Name = "typescript_rename_symbol"; Desc = "Rename TypeScript symbols using Language Service"; Category = "TypeScript" }
    
    # Infrastructure tools
    "IndexWorkspaceTool" = @{ Name = "index_workspace"; Desc = "Build search index for blazing fast searches"; Category = "Infrastructure" }
    "SetLoggingTool" = @{ Name = "set_logging"; Desc = "Control file-based logging dynamically"; Category = "Infrastructure" }
    "GetVersionTool" = @{ Name = "get_version"; Desc = "Get version and build information"; Category = "Infrastructure" }
    
    # Batch tools
    "BatchOperationsTool" = @{ Name = "batch_operations"; Desc = "Execute multiple operations in parallel"; Category = "Batch" }
    
    # Memory tools (collections)
    "ClaudeMemoryTools" = @{ Name = "claude_memory"; Desc = "Essential memory system operations"; Category = "Memory" }
    "FlexibleMemoryTools" = @{ Name = "flexible_memory"; Desc = "Flexible memory storage and retrieval"; Category = "Memory" }
    "ChecklistTools" = @{ Name = "checklist"; Desc = "Persistent checklist management"; Category = "Memory" }
    "MemoryLinkingTools" = @{ Name = "memory_linking"; Desc = "Memory relationship management"; Category = "Memory" }
}

function Update-ToolFile {
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
    
    # Check if already implements ITool
    if ($content -match "class\s+$ClassName\s*:\s*ITool") {
        Write-Host "$ClassName already implements ITool" -ForegroundColor Green
        return
    }
    
    # Check if it inherits from a base class
    if ($content -match "class\s+$ClassName\s*:\s*\w+Base") {
        Write-Host "$ClassName inherits from base class, skipping" -ForegroundColor Yellow
        return
    }
    
    # Pattern to match class declaration
    $pattern = "(public\s+class\s+$ClassName)(\s*\{)"
    
    $replacement = @"
`$1 : ITool
{
    public string ToolName => "$($Info.Name)";
    public string Description => "$($Info.Desc)";
    public ToolCategory Category => ToolCategory.$($Info.Category);
"@
    
    $newContent = $content -replace $pattern, $replacement
    
    # Save the file
    Set-Content $filePath $newContent -NoNewline
    Write-Host "Updated $ClassName" -ForegroundColor Green
}

# Process each tool
foreach ($tool in $toolsInfo.GetEnumerator()) {
    Update-ToolFile -ClassName $tool.Key -Info $tool.Value
}

Write-Host "`nDone! Remember to:" -ForegroundColor Cyan
Write-Host "1. Build the project to check for errors" -ForegroundColor Yellow
Write-Host "2. Update V2 tools if needed (they inherit from ClaudeOptimizedToolBase)" -ForegroundColor Yellow
Write-Host "3. Update RenameSymbolTool (inherits from McpToolBase)" -ForegroundColor Yellow
Write-Host "4. Delete ToolUpdateHelper.cs when done" -ForegroundColor Yellow