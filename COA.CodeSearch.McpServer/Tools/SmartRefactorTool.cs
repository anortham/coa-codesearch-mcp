using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.ResponseBuilders;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Julie;
using COA.CodeSearch.McpServer.Services.Sqlite;
using COA.CodeSearch.McpServer.Tools.Models;
using COA.CodeSearch.McpServer.Tools.Parameters;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Interfaces;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Storage;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Smart refactoring tool for semantic code transformations.
/// Unlike simple text editing, this tool understands code structure and performs
/// changes safely across the entire workspace using AST-validated symbol positions.
/// </summary>
public class SmartRefactorTool : CodeSearchToolBase<SmartRefactorParameters, AIOptimizedResponse<SmartRefactorResult>>
{
    private readonly IReferenceResolverService _referenceResolver;
    private readonly ISQLiteSymbolService _sqliteService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly SmartRefactorResponseBuilder _responseBuilder;
    private readonly ILogger<SmartRefactorTool> _logger;

    public SmartRefactorTool(
        IServiceProvider serviceProvider,
        IReferenceResolverService referenceResolver,
        ISQLiteSymbolService sqliteService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        ILogger<SmartRefactorTool> logger) : base(serviceProvider, logger)
    {
        _referenceResolver = referenceResolver ?? throw new ArgumentNullException(nameof(referenceResolver));
        _sqliteService = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _keyGenerator = keyGenerator ?? throw new ArgumentNullException(nameof(keyGenerator));
        _responseBuilder = new SmartRefactorResponseBuilder(logger as ILogger<SmartRefactorResponseBuilder>, storageService);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override string Name => ToolNames.SmartRefactor;

    public override string Description =>
        "SAFE SEMANTIC REFACTORING - Symbol-aware code transformations using AST-validated positions. " +
        "Performs rename_symbol, extract_to_file, move_symbol_to_file, extract_interface operations safely across entire workspace. " +
        "ALWAYS use find_references BEFORE refactoring to understand impact. " +
        "Unlike simple text editing, this tool preserves code structure and updates all references.";

    public override ToolCategory Category => ToolCategory.Refactoring;

    protected override async Task<AIOptimizedResponse<SmartRefactorResult>> ExecuteInternalAsync(
        SmartRefactorParameters parameters,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // Validate required parameters
        var operation = ValidateRequired(parameters.Operation, nameof(parameters.Operation));
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));

        // Resolve to absolute path
        workspacePath = Path.GetFullPath(workspacePath);

        _logger.LogInformation("🔄 Smart refactor operation: {Operation} (dry_run: {DryRun})", operation, parameters.DryRun);

        try
        {
            SmartRefactorResult result = operation.ToLowerInvariant() switch
            {
                "rename_symbol" => await HandleRenameSymbolAsync(parameters, workspacePath, cancellationToken),
                "extract_to_file" => await HandleExtractToFileAsync(parameters, workspacePath, cancellationToken),
                "move_symbol_to_file" => await HandleMoveSymbolToFileAsync(parameters, workspacePath, cancellationToken),
                "extract_interface" => await HandleExtractInterfaceAsync(parameters, workspacePath, cancellationToken),
                _ => new SmartRefactorResult
                {
                    Success = false,
                    Operation = operation,
                    DryRun = parameters.DryRun,
                    Errors = new List<string>
                    {
                        $"Unknown operation: '{operation}'. Supported: rename_symbol, extract_to_file, move_symbol_to_file, extract_interface"
                    },
                    NextActions = new List<string>
                    {
                        "Use operation='rename_symbol' for renaming symbols across workspace",
                        "Use operation='extract_to_file' to extract a symbol to a new file",
                        "Use operation='move_symbol_to_file' to move a symbol to a new file (extract + remove from source)",
                        "Use operation='extract_interface' to create an interface from a class"
                    }
                }
            };

            result.Duration = stopwatch.Elapsed;

            // Build response
            var responseContext = new ResponseContext
            {
                ResponseMode = "adaptive",
                TokenLimit = parameters.MaxTokens,
                StoreFullResults = true,
                ToolName = Name
            };

            return await _responseBuilder.BuildResponseAsync(result, responseContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Smart refactor failed");

            var errorResult = new SmartRefactorResult
            {
                Success = false,
                Operation = operation,
                DryRun = parameters.DryRun,
                Errors = new List<string> { $"Operation failed: {ex.Message}" },
                Duration = stopwatch.Elapsed
            };

            var responseContext = new ResponseContext
            {
                ResponseMode = "adaptive",
                TokenLimit = parameters.MaxTokens,
                StoreFullResults = false,
                ToolName = Name
            };

            return await _responseBuilder.BuildResponseAsync(errorResult, responseContext);
        }
    }

    /// <summary>
    /// Handle rename symbol operation using AST-validated identifier positions
    /// </summary>
    private async Task<SmartRefactorResult> HandleRenameSymbolAsync(
        SmartRefactorParameters parameters,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        // Parse operation parameters
        var paramsDoc = JsonDocument.Parse(parameters.Params);
        var oldName = paramsDoc.RootElement.TryGetProperty("old_name", out var oldProp)
            ? oldProp.GetString()
            : throw new ArgumentException("Missing required parameter: old_name");

        var newName = paramsDoc.RootElement.TryGetProperty("new_name", out var newProp)
            ? newProp.GetString()
            : throw new ArgumentException("Missing required parameter: new_name");

        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("old_name and new_name cannot be empty");
        }

        _logger.LogInformation("🎯 Rename '{OldName}' → '{NewName}'", oldName, newName);

        // Step 1: Find all references using AST-validated identifier positions
        var references = await _referenceResolver.FindReferencesAsync(
            workspacePath,
            oldName,
            caseSensitive: false,
            cancellationToken);

        if (!references.Any())
        {
            return new SmartRefactorResult
            {
                Success = false,
                Operation = "rename_symbol",
                DryRun = parameters.DryRun,
                Errors = new List<string>
                {
                    $"No references found for symbol '{oldName}'"
                },
                NextActions = new List<string>
                {
                    "Verify symbol name spelling",
                    "Run symbol_search to locate the symbol"
                }
            };
        }

        _logger.LogInformation("📍 Found {Count} references across {FileCount} files",
            references.Count,
            references.Select(r => r.Identifier.FilePath).Distinct().Count());

        // Step 2: Group references by file
        var fileGroups = references
            .GroupBy(r => r.Identifier.FilePath)
            .OrderBy(g => g.Key);

        // Step 3: Process each file
        var changes = new List<FileRefactorChange>();
        var errors = new List<string>();
        int totalChanges = 0;

        foreach (var fileGroup in fileGroups)
        {
            if (changes.Count >= parameters.MaxFiles)
            {
                errors.Add($"Reached max files limit ({parameters.MaxFiles}). Stopping.");
                break;
            }

            try
            {
                var fileChange = await ProcessFileRenamesAsync(
                    fileGroup.Key,
                    fileGroup.ToList(),
                    oldName,
                    newName,
                    parameters.DryRun,
                    cancellationToken);

                changes.Add(fileChange);
                totalChanges += fileChange.ReplacementCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process file: {FilePath}", fileGroup.Key);
                errors.Add($"❌ {Path.GetFileName(fileGroup.Key)}: {ex.Message}");
            }
        }

        // Build result
        var result = new SmartRefactorResult
        {
            Success = errors.Count == 0 || totalChanges > 0,
            Operation = "rename_symbol",
            DryRun = parameters.DryRun,
            FilesModified = changes.Select(c => c.FilePath).ToList(),
            ChangesCount = totalChanges,
            Changes = changes,
            Errors = errors
        };

        // Add next actions
        if (parameters.DryRun)
        {
            result.NextActions.Add("Set dry_run=false to apply changes");
        }
        else
        {
            result.NextActions.Add("Run tests to verify changes");
            result.NextActions.Add("Use find_references to validate rename completion");
            result.NextActions.Add("Review git diff to inspect changes");
        }

        return result;
    }

    /// <summary>
    /// Process renames for a single file using byte-offset replacement
    /// </summary>
    private async Task<FileRefactorChange> ProcessFileRenamesAsync(
        string filePath,
        List<ResolvedReference> references,
        string oldName,
        string newName,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        // Read file content
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var originalContent = content;

        // Sort references by byte position (DESCENDING - last to first)
        // This preserves byte offsets as we replace from end to start
        var sortedRefs = references
            .OrderByDescending(r => r.Identifier.StartByte)
            .ToList();

        _logger.LogDebug("Processing {FilePath}: {Count} replacements",
            Path.GetFileName(filePath), sortedRefs.Count);

        // Apply replacements from end to start
        var builder = new StringBuilder(content);
        var lines = new List<int>();

        foreach (var reference in sortedRefs)
        {
            var startByte = reference.Identifier.StartByte;
            var endByte = reference.Identifier.EndByte;

            // Skip if byte positions are not available
            if (!startByte.HasValue || !endByte.HasValue)
            {
                _logger.LogWarning("Missing byte positions for {Name} at line {Line} in {File}",
                    reference.Identifier.Name, reference.Identifier.StartLine, Path.GetFileName(filePath));
                continue;
            }

            var start = startByte.Value;
            var end = endByte.Value;

            // Validate byte positions
            if (start < 0 || end > content.Length || start >= end)
            {
                _logger.LogWarning("Invalid byte positions: {Start}-{End} in {File}",
                    start, end, Path.GetFileName(filePath));
                continue;
            }

            // Replace at byte position
            builder.Remove(start, end - start);
            builder.Insert(start, newName);

            lines.Add(reference.Identifier.StartLine);
        }

        var newContent = builder.ToString();

        // Write file if not dry run
        if (!dryRun && newContent != originalContent)
        {
            await File.WriteAllTextAsync(filePath, newContent, cancellationToken);
            _logger.LogInformation("✅ Updated {FilePath}: {Count} changes",
                Path.GetFileName(filePath), sortedRefs.Count);
        }

        // Create change preview
        string? preview = null;
        if (dryRun)
        {
            preview = $"{sortedRefs.Count} occurrences of '{oldName}' → '{newName}' at lines: {string.Join(", ", lines.Distinct().OrderBy(l => l))}";
        }

        return new FileRefactorChange
        {
            FilePath = filePath,
            ReplacementCount = sortedRefs.Count,
            ChangePreview = preview,
            Lines = lines.Distinct().OrderBy(l => l).ToList()
        };
    }

    /// <summary>
    /// Handle extract to file operation - extracts a symbol to a new file
    /// </summary>
    private async Task<SmartRefactorResult> HandleExtractToFileAsync(
        SmartRefactorParameters parameters,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        // Parse operation parameters
        var paramsDoc = JsonDocument.Parse(parameters.Params);
        var symbolName = paramsDoc.RootElement.TryGetProperty("symbol_name", out var symbolProp)
            ? symbolProp.GetString()
            : throw new ArgumentException("Missing required parameter: symbol_name");

        var targetFile = paramsDoc.RootElement.TryGetProperty("target_file", out var targetProp)
            ? targetProp.GetString()
            : throw new ArgumentException("Missing required parameter: target_file");

        if (string.IsNullOrWhiteSpace(symbolName) || string.IsNullOrWhiteSpace(targetFile))
        {
            throw new ArgumentException("symbol_name and target_file cannot be empty");
        }

        // Resolve target file to absolute path
        if (!Path.IsPathRooted(targetFile))
        {
            targetFile = Path.Combine(workspacePath, targetFile);
        }

        _logger.LogInformation("📤 Extract '{Symbol}' → '{Target}'", symbolName, Path.GetFileName(targetFile));

        // Step 1: Find symbol definition
        var symbols = await _sqliteService.GetSymbolsByNameAsync(workspacePath, symbolName, caseSensitive: false, cancellationToken);
        var symbolDef = symbols.FirstOrDefault(s => s.Kind == "class" || s.Kind == "interface" || s.Kind == "struct");

        if (symbolDef == null)
        {
            return new SmartRefactorResult
            {
                Success = false,
                Operation = "extract_to_file",
                DryRun = parameters.DryRun,
                Errors = new List<string>
                {
                    $"Symbol '{symbolName}' not found or not a class/interface/struct"
                },
                NextActions = new List<string>
                {
                    "Use symbol_search to find the symbol first",
                    "Only classes, interfaces, and structs can be extracted"
                }
            };
        }

        _logger.LogInformation("📍 Found {Kind} '{Name}' at {File}:{Line}",
            symbolDef.Kind, symbolDef.Name, Path.GetFileName(symbolDef.FilePath), symbolDef.StartLine);

        // Step 2: Read source file and extract symbol code
        var sourceContent = await File.ReadAllTextAsync(symbolDef.FilePath, cancellationToken);
        var sourceLines = sourceContent.Split('\n');

        if (symbolDef.EndLine < symbolDef.StartLine || symbolDef.EndLine > sourceLines.Length)
        {
            return new SmartRefactorResult
            {
                Success = false,
                Operation = "extract_to_file",
                DryRun = parameters.DryRun,
                Errors = new List<string>
                {
                    $"Invalid line range for symbol: {symbolDef.StartLine}-{symbolDef.EndLine}"
                }
            };
        }

        // Extract symbol code (lines are 1-based, arrays are 0-based)
        var symbolLines = sourceLines.Skip(symbolDef.StartLine - 1)
                                     .Take(symbolDef.EndLine - symbolDef.StartLine + 1)
                                     .ToList();

        var symbolCode = string.Join("\n", symbolLines);

        // Step 3: Extract namespace and using statements from source
        var namespaceMatch = System.Text.RegularExpressions.Regex.Match(sourceContent, @"namespace\s+([\w\.]+)");
        var namespaceDecl = namespaceMatch.Success ? namespaceMatch.Value : "";

        var usingStatements = System.Text.RegularExpressions.Regex.Matches(sourceContent, @"using\s+[^;]+;")
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Value)
            .Distinct()
            .ToList();

        // Step 4: Build target file content
        var targetContent = new StringBuilder();

        // Add using statements
        foreach (var usingStmt in usingStatements)
        {
            targetContent.AppendLine(usingStmt);
        }

        if (usingStatements.Any())
        {
            targetContent.AppendLine();
        }

        // Add namespace if found
        if (!string.IsNullOrEmpty(namespaceDecl))
        {
            targetContent.AppendLine(namespaceDecl);
            targetContent.AppendLine("{");
            targetContent.AppendLine(symbolCode);
            targetContent.AppendLine("}");
        }
        else
        {
            targetContent.AppendLine(symbolCode);
        }

        // Step 5: Write target file (if not dry run)
        if (!parameters.DryRun)
        {
            // Ensure target directory exists
            var targetDir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Check if file already exists
            if (File.Exists(targetFile))
            {
                return new SmartRefactorResult
                {
                    Success = false,
                    Operation = "extract_to_file",
                    DryRun = parameters.DryRun,
                    Errors = new List<string>
                    {
                        $"Target file already exists: {targetFile}"
                    },
                    NextActions = new List<string>
                    {
                        "Choose a different target file name",
                        "Delete existing file first if intentional"
                    }
                };
            }

            await File.WriteAllTextAsync(targetFile, targetContent.ToString(), cancellationToken);
            _logger.LogInformation("✅ Created {File} with {Kind} {Name}",
                Path.GetFileName(targetFile), symbolDef.Kind, symbolName);
        }

        // Build result
        var result = new SmartRefactorResult
        {
            Success = true,
            Operation = "extract_to_file",
            DryRun = parameters.DryRun,
            FilesModified = parameters.DryRun ? new List<string>() : new List<string> { targetFile },
            ChangesCount = 1,
            Changes = new List<FileRefactorChange>
            {
                new FileRefactorChange
                {
                    FilePath = targetFile,
                    ReplacementCount = 1,
                    ChangePreview = parameters.DryRun
                        ? $"Will create {Path.GetFileName(targetFile)} with {symbolDef.Kind} '{symbolName}' ({symbolLines.Count} lines)"
                        : $"Created {Path.GetFileName(targetFile)} with {symbolDef.Kind} '{symbolName}'",
                    Lines = new List<int> { symbolDef.StartLine }
                }
            }
        };

        // Add next actions
        if (parameters.DryRun)
        {
            result.NextActions.Add("Set dry_run=false to create the file");
        }
        else
        {
            result.NextActions.Add($"Review {Path.GetFileName(targetFile)} to verify extraction");
            result.NextActions.Add("Update imports in other files if needed");
            result.NextActions.Add("Consider removing original symbol from source file");
        }

        return result;
    }

    /// <summary>
    /// Handle move to file operation - extracts a symbol to a new file AND removes it from source
    /// </summary>
    private async Task<SmartRefactorResult> HandleMoveSymbolToFileAsync(
        SmartRefactorParameters parameters,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        // Parse operation parameters
        var paramsDoc = JsonDocument.Parse(parameters.Params);
        var symbolName = paramsDoc.RootElement.TryGetProperty("symbol_name", out var symbolProp)
            ? symbolProp.GetString()
            : throw new ArgumentException("Missing required parameter: symbol_name");

        var targetFile = paramsDoc.RootElement.TryGetProperty("target_file", out var targetProp)
            ? targetProp.GetString()
            : throw new ArgumentException("Missing required parameter: target_file");

        if (string.IsNullOrWhiteSpace(symbolName) || string.IsNullOrWhiteSpace(targetFile))
        {
            throw new ArgumentException("symbol_name and target_file cannot be empty");
        }

        // Resolve target file to absolute path
        if (!Path.IsPathRooted(targetFile))
        {
            targetFile = Path.Combine(workspacePath, targetFile);
        }

        _logger.LogInformation("🚚 Move '{Symbol}' → '{Target}' (extract + remove from source)",
            symbolName, Path.GetFileName(targetFile));

        // Step 1: Find symbol definition
        var symbols = await _sqliteService.GetSymbolsByNameAsync(workspacePath, symbolName, caseSensitive: false, cancellationToken);
        var symbolDef = symbols.FirstOrDefault(s => s.Kind == "class" || s.Kind == "interface" || s.Kind == "struct");

        if (symbolDef == null)
        {
            return new SmartRefactorResult
            {
                Success = false,
                Operation = "move_symbol_to_file",
                DryRun = parameters.DryRun,
                Errors = new List<string>
                {
                    $"Symbol '{symbolName}' not found or not a class/interface/struct"
                },
                NextActions = new List<string>
                {
                    "Use symbol_search to find the symbol first",
                    "Only classes, interfaces, and structs can be moved"
                }
            };
        }

        var sourceFile = symbolDef.FilePath;
        _logger.LogInformation("📍 Found {Kind} '{Name}' at {File}:{Line}",
            symbolDef.Kind, symbolDef.Name, Path.GetFileName(sourceFile), symbolDef.StartLine);

        // Step 2: Read source file and extract symbol code
        var sourceContent = await File.ReadAllTextAsync(sourceFile, cancellationToken);
        var sourceLines = sourceContent.Split('\n');

        if (symbolDef.EndLine < symbolDef.StartLine || symbolDef.EndLine > sourceLines.Length)
        {
            return new SmartRefactorResult
            {
                Success = false,
                Operation = "move_symbol_to_file",
                DryRun = parameters.DryRun,
                Errors = new List<string>
                {
                    $"Invalid line range for symbol: {symbolDef.StartLine}-{symbolDef.EndLine}"
                }
            };
        }

        // Extract symbol code (lines are 1-based, arrays are 0-based)
        var symbolLines = sourceLines.Skip(symbolDef.StartLine - 1)
                                     .Take(symbolDef.EndLine - symbolDef.StartLine + 1)
                                     .ToList();

        var symbolCode = string.Join("\n", symbolLines);

        // Step 3: Extract namespace and using statements from source
        var namespaceMatch = System.Text.RegularExpressions.Regex.Match(sourceContent, @"namespace\s+([\w\.]+)");
        var namespaceDecl = namespaceMatch.Success ? namespaceMatch.Value : "";

        var usingStatements = System.Text.RegularExpressions.Regex.Matches(sourceContent, @"using\s+[^;]+;")
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Value)
            .Distinct()
            .ToList();

        // Step 4: Build target file content
        var targetContent = new StringBuilder();

        // Add using statements
        foreach (var usingStmt in usingStatements)
        {
            targetContent.AppendLine(usingStmt);
        }

        if (usingStatements.Any())
        {
            targetContent.AppendLine();
        }

        // Add namespace if found
        if (!string.IsNullOrEmpty(namespaceDecl))
        {
            targetContent.AppendLine(namespaceDecl);
            targetContent.AppendLine("{");
            targetContent.AppendLine(symbolCode);
            targetContent.AppendLine("}");
        }
        else
        {
            targetContent.AppendLine(symbolCode);
        }

        // Step 5: Remove symbol from source file
        var modifiedSourceLines = new List<string>();
        for (int i = 0; i < sourceLines.Length; i++)
        {
            int lineNum = i + 1; // Lines are 1-based
            // Skip lines that contain the symbol definition
            if (lineNum < symbolDef.StartLine || lineNum > symbolDef.EndLine)
            {
                modifiedSourceLines.Add(sourceLines[i]);
            }
        }

        var modifiedSourceContent = string.Join("\n", modifiedSourceLines);

        // Step 6: Write files (if not dry run)
        var changes = new List<FileRefactorChange>();
        var filesModified = new List<string>();

        if (!parameters.DryRun)
        {
            // Ensure target directory exists
            var targetDir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Check if target file already exists
            if (File.Exists(targetFile))
            {
                return new SmartRefactorResult
                {
                    Success = false,
                    Operation = "move_symbol_to_file",
                    DryRun = parameters.DryRun,
                    Errors = new List<string>
                    {
                        $"Target file already exists: {targetFile}"
                    },
                    NextActions = new List<string>
                    {
                        "Choose a different target file name",
                        "Delete existing file first if intentional"
                    }
                };
            }

            // Write target file
            await File.WriteAllTextAsync(targetFile, targetContent.ToString(), cancellationToken);
            _logger.LogInformation("✅ Created {File} with {Kind} {Name}",
                Path.GetFileName(targetFile), symbolDef.Kind, symbolName);

            // Update source file (remove symbol)
            await File.WriteAllTextAsync(sourceFile, modifiedSourceContent, cancellationToken);
            _logger.LogInformation("✅ Removed {Kind} {Name} from {File}",
                symbolDef.Kind, symbolName, Path.GetFileName(sourceFile));

            filesModified.Add(targetFile);
            filesModified.Add(sourceFile);
        }

        // Build changes list
        changes.Add(new FileRefactorChange
        {
            FilePath = targetFile,
            ReplacementCount = 1,
            ChangePreview = parameters.DryRun
                ? $"Will create {Path.GetFileName(targetFile)} with {symbolDef.Kind} '{symbolName}' ({symbolLines.Count} lines)"
                : $"Created {Path.GetFileName(targetFile)} with {symbolDef.Kind} '{symbolName}'",
            Lines = new List<int> { 1 }
        });

        changes.Add(new FileRefactorChange
        {
            FilePath = sourceFile,
            ReplacementCount = 1,
            ChangePreview = parameters.DryRun
                ? $"Will remove {symbolLines.Count} lines (lines {symbolDef.StartLine}-{symbolDef.EndLine}) from {Path.GetFileName(sourceFile)}"
                : $"Removed {symbolDef.Kind} '{symbolName}' ({symbolLines.Count} lines)",
            Lines = new List<int> { symbolDef.StartLine }
        });

        // Build result
        var result = new SmartRefactorResult
        {
            Success = true,
            Operation = "move_symbol_to_file",
            DryRun = parameters.DryRun,
            FilesModified = filesModified,
            ChangesCount = 2,
            Changes = changes
        };

        // Add next actions
        if (parameters.DryRun)
        {
            result.NextActions.Add("Set dry_run=false to apply the move");
        }
        else
        {
            result.NextActions.Add($"Review {Path.GetFileName(targetFile)} to verify extraction");
            result.NextActions.Add($"Review {Path.GetFileName(sourceFile)} to verify removal");
            result.NextActions.Add("Update imports in other files to reference new location");
            result.NextActions.Add("Run tests to ensure nothing broke");
        }

        return result;
    }

    /// <summary>
    /// Handle extract interface operation - creates an interface from a class's public API
    /// </summary>
    private async Task<SmartRefactorResult> HandleExtractInterfaceAsync(
        SmartRefactorParameters parameters,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        // Parse operation parameters
        var paramsDoc = JsonDocument.Parse(parameters.Params);
        var className = paramsDoc.RootElement.TryGetProperty("class_name", out var classProp)
            ? classProp.GetString()
            : throw new ArgumentException("Missing required parameter: class_name");

        var interfaceName = paramsDoc.RootElement.TryGetProperty("interface_name", out var interfaceProp)
            ? interfaceProp.GetString()
            : $"I{className}"; // Default: IClassName

        var targetFile = paramsDoc.RootElement.TryGetProperty("target_file", out var targetProp)
            ? targetProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(className))
        {
            throw new ArgumentException("class_name cannot be empty");
        }

        // Default target file to same directory as class
        _logger.LogInformation("🔌 Extract interface from '{Class}' → '{Interface}'", className, interfaceName);

        // Step 1: Find class definition
        var symbols = await _sqliteService.GetSymbolsByNameAsync(workspacePath, className, caseSensitive: false, cancellationToken);
        var classDef = symbols.FirstOrDefault(s => s.Kind == "class");

        if (classDef == null)
        {
            return new SmartRefactorResult
            {
                Success = false,
                Operation = "extract_interface",
                DryRun = parameters.DryRun,
                Errors = new List<string>
                {
                    $"Class '{className}' not found"
                },
                NextActions = new List<string>
                {
                    "Use symbol_search to find the class first",
                    "Only classes can have interfaces extracted"
                }
            };
        }

        _logger.LogInformation("📍 Found class '{Name}' at {File}:{Line}",
            classDef.Name, Path.GetFileName(classDef.FilePath), classDef.StartLine);

        // Step 2: Read class file and extract public members
        var sourceContent = await File.ReadAllTextAsync(classDef.FilePath, cancellationToken);
        var sourceLines = sourceContent.Split('\n');

        if (classDef.EndLine < classDef.StartLine || classDef.EndLine > sourceLines.Length)
        {
            return new SmartRefactorResult
            {
                Success = false,
                Operation = "extract_interface",
                DryRun = parameters.DryRun,
                Errors = new List<string>
                {
                    $"Invalid line range for class: {classDef.StartLine}-{classDef.EndLine}"
                }
            };
        }

        // Extract class code
        var classLines = sourceLines.Skip(classDef.StartLine - 1)
                                    .Take(classDef.EndLine - classDef.StartLine + 1)
                                    .ToList();

        var classCode = string.Join("\n", classLines);

        // Step 3: Parse public methods and properties
        var publicMembers = ExtractPublicMembers(classCode);

        if (!publicMembers.Any())
        {
            return new SmartRefactorResult
            {
                Success = false,
                Operation = "extract_interface",
                DryRun = parameters.DryRun,
                Errors = new List<string>
                {
                    $"No public members found in class '{className}'"
                },
                NextActions = new List<string>
                {
                    "Ensure the class has public methods or properties",
                    "Check that the class definition is complete"
                }
            };
        }

        _logger.LogInformation("📋 Found {Count} public members", publicMembers.Count);

        // Step 4: Build interface content
        var namespaceMatch = System.Text.RegularExpressions.Regex.Match(sourceContent, @"namespace\s+([\w\.]+)");
        var namespaceDecl = namespaceMatch.Success ? namespaceMatch.Value : "";

        var interfaceContent = new StringBuilder();

        // Add using statements (basic set)
        interfaceContent.AppendLine("using System;");
        interfaceContent.AppendLine("using System.Collections.Generic;");
        interfaceContent.AppendLine("using System.Threading.Tasks;");
        interfaceContent.AppendLine();

        // Add namespace if found
        if (!string.IsNullOrEmpty(namespaceDecl))
        {
            interfaceContent.AppendLine(namespaceDecl);
            interfaceContent.AppendLine("{");
        }

        // Add interface declaration
        interfaceContent.AppendLine($"    public interface {interfaceName}");
        interfaceContent.AppendLine("    {");

        // Add public members
        foreach (var member in publicMembers)
        {
            interfaceContent.AppendLine($"        {member}");
        }

        interfaceContent.AppendLine("    }");

        if (!string.IsNullOrEmpty(namespaceDecl))
        {
            interfaceContent.AppendLine("}");
        }

        // Step 5: Determine target file path
        if (string.IsNullOrWhiteSpace(targetFile))
        {
            var classDir = Path.GetDirectoryName(classDef.FilePath);
            targetFile = Path.Combine(classDir ?? "", $"{interfaceName}.cs");
        }
        else if (!Path.IsPathRooted(targetFile))
        {
            targetFile = Path.Combine(workspacePath, targetFile);
        }

        // Step 6: Write interface file (if not dry run)
        if (!parameters.DryRun)
        {
            // Ensure target directory exists
            var targetDir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Check if file already exists
            if (File.Exists(targetFile))
            {
                return new SmartRefactorResult
                {
                    Success = false,
                    Operation = "extract_interface",
                    DryRun = parameters.DryRun,
                    Errors = new List<string>
                    {
                        $"Target file already exists: {targetFile}"
                    },
                    NextActions = new List<string>
                    {
                        "Choose a different interface name",
                        "Delete existing file first if intentional"
                    }
                };
            }

            await File.WriteAllTextAsync(targetFile, interfaceContent.ToString(), cancellationToken);
            _logger.LogInformation("✅ Created interface {Interface} at {File}",
                interfaceName, Path.GetFileName(targetFile));
        }

        // Build result
        var result = new SmartRefactorResult
        {
            Success = true,
            Operation = "extract_interface",
            DryRun = parameters.DryRun,
            FilesModified = parameters.DryRun ? new List<string>() : new List<string> { targetFile },
            ChangesCount = 1,
            Changes = new List<FileRefactorChange>
            {
                new FileRefactorChange
                {
                    FilePath = targetFile,
                    ReplacementCount = publicMembers.Count,
                    ChangePreview = parameters.DryRun
                        ? $"Will create {Path.GetFileName(targetFile)} with {publicMembers.Count} members from '{className}'"
                        : $"Created interface '{interfaceName}' with {publicMembers.Count} members",
                    Lines = new List<int> { 1 }
                }
            }
        };

        // Add next actions
        if (parameters.DryRun)
        {
            result.NextActions.Add("Set dry_run=false to create the interface");
        }
        else
        {
            result.NextActions.Add($"Review {Path.GetFileName(targetFile)} to verify interface");
            result.NextActions.Add($"Add interface to class: public class {className} : {interfaceName}");
            result.NextActions.Add("Update dependency injection to use interface");
        }

        return result;
    }

    /// <summary>
    /// Extract public member signatures from class code
    /// </summary>
    private List<string> ExtractPublicMembers(string classCode)
    {
        var members = new List<string>();

        // Match public methods (including async, generic, with various return types)
        var methodPattern = @"public\s+(?:virtual\s+|override\s+|async\s+)?(?:static\s+)?(\w+(?:<[\w,\s<>]+>)?)\s+(\w+)\s*\(([^)]*)\)";
        var methodMatches = System.Text.RegularExpressions.Regex.Matches(classCode, methodPattern);

        foreach (System.Text.RegularExpressions.Match match in methodMatches)
        {
            var returnType = match.Groups[1].Value;
            var methodName = match.Groups[2].Value;
            var parameters = match.Groups[3].Value;

            // Skip constructors
            if (methodName == classCode.Split('\n').FirstOrDefault(l => l.Contains("class"))?.Split(' ').LastOrDefault()?.Trim('{'))
                continue;

            // Clean up and format
            var signature = $"{returnType} {methodName}({parameters});";
            members.Add(signature);
        }

        // Match public properties
        var propertyPattern = @"public\s+(?:virtual\s+|override\s+)?(?:static\s+)?(\w+(?:<[\w,\s<>]+>)?)\s+(\w+)\s*\{\s*get;(?:\s*(?:private\s+)?set;)?\s*\}";
        var propertyMatches = System.Text.RegularExpressions.Regex.Matches(classCode, propertyPattern);

        foreach (System.Text.RegularExpressions.Match match in propertyMatches)
        {
            var propertyType = match.Groups[1].Value;
            var propertyName = match.Groups[2].Value;

            // Format as interface property (only getter for now)
            var signature = $"{propertyType} {propertyName} {{ get; }}";
            members.Add(signature);
        }

        return members;
    }
}
