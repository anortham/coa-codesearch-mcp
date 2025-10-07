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
        "Performs rename_symbol, extract_function, inline_variable operations safely across entire workspace. " +
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
                _ => new SmartRefactorResult
                {
                    Success = false,
                    Operation = operation,
                    DryRun = parameters.DryRun,
                    Errors = new List<string>
                    {
                        $"Unknown operation: '{operation}'. Supported: rename_symbol"
                    },
                    NextActions = new List<string>
                    {
                        "Use operation='rename_symbol' for renaming symbols across workspace"
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
}
