using System.ComponentModel;
using System.Text.Json;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using COA.CodeSearch.McpServer.Tools.Models;
using COA.CodeSearch.McpServer.Tools.Parameters;
using COA.CodeSearch.McpServer.ResponseBuilders;
using COA.Mcp.Framework.Interfaces;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Detects semantic patterns in code using Tree-sitter analysis.
/// Identifies code quality issues, common patterns, and potential improvements.
/// </summary>
public class FindPatternsTool : CodeSearchToolBase<FindPatternsParameters, AIOptimizedResponse<FindPatternsResult>>
{
    private readonly ITypeExtractionService _typeExtractionService;
    private readonly ILogger<FindPatternsTool> _logger;

    /// <summary>
    /// Initializes a new instance of the FindPatternsTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="logger">Logger instance</param>
    public FindPatternsTool(
        IServiceProvider serviceProvider,
        ILogger<FindPatternsTool> logger) : base(serviceProvider, logger)
    {
        _typeExtractionService = serviceProvider.GetRequiredService<ITypeExtractionService>();
        _logger = logger;
    }

    /// <summary>
    /// Gets the tool name identifier.
    /// </summary>
    public override string Name => ToolNames.FindPatterns;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description => "Detects semantic patterns and code quality issues using Tree-sitter analysis. " +
        "Identifies async patterns, empty catches, unused usings, magic numbers, large methods, and dead code (unused private members).";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Query;

    /// <summary>
    /// This tool handles validation internally in ExecuteInternalAsync, so disable framework validation
    /// </summary>
    protected override bool ShouldValidateDataAnnotations => false;

    /// <summary>
    /// Executes the find patterns operation to detect code patterns and quality issues.
    /// </summary>
    /// <param name="parameters">Find patterns parameters including file path and pattern options</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Find patterns results with detected patterns and quality insights</returns>
    protected override async Task<AIOptimizedResponse<FindPatternsResult>> ExecuteInternalAsync(
        FindPatternsParameters parameters, CancellationToken cancellationToken)
    {
        try
        {
            // Validate required parameters
            if (string.IsNullOrWhiteSpace(parameters.FilePath))
            {
                return CreateErrorResponse("File path is required");
            }
            var filePath = parameters.FilePath;
            
            // Convert to absolute path
            if (!Path.IsPathRooted(filePath))
            {
                filePath = Path.GetFullPath(filePath);
            }
            
            // Validate file exists
            if (!File.Exists(filePath))
            {
                return CreateErrorResponse($"File not found: {filePath}");
            }

            // Read file content for pattern analysis
            var fileContent = await File.ReadAllTextAsync(filePath, cancellationToken);
            
            // Extract type information using Tree-sitter
            var extractionResult = await _typeExtractionService.ExtractTypes(fileContent, filePath);

            System.Console.WriteLine($"DEBUG: Type extraction success: {extractionResult.Success}");

            if (!extractionResult.Success)
            {
                System.Console.WriteLine($"DEBUG: Returning error response for type extraction failure");
                return CreateErrorResponse("Failed to extract type information from file");
            }

            // Detect patterns
            var patterns = await DetectPatternsAsync(fileContent, extractionResult, parameters, cancellationToken);

            var result = new FindPatternsResult
            {
                FilePath = filePath,
                Language = extractionResult.Language ?? "unknown",
                PatternsFound = patterns,
                TotalPatterns = patterns.Count,
                AnalysisTime = DateTime.UtcNow
            };

            return CreateSuccessResponse(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in FindPatternsTool execution");
            return CreateErrorResponse($"Internal error: {ex.Message}");
        }
    }

    /// <summary>
    /// Detects semantic patterns in the code based on Tree-sitter analysis and file content.
    /// </summary>
    private Task<List<CodePattern>> DetectPatternsAsync(
        string fileContent, 
        TypeExtractionResult extractionResult, 
        FindPatternsParameters parameters,
        CancellationToken cancellationToken)
    {
        var patterns = new List<CodePattern>();
        var lines = fileContent.Split('\n');

        // Pattern 1: Async methods without ConfigureAwait(false)
        if (parameters.DetectAsyncPatterns)
        {
            patterns.AddRange(DetectAsyncWithoutConfigureAwait(fileContent, extractionResult, lines));
        }

        // Pattern 2: Empty catch blocks
        if (parameters.DetectEmptyCatchBlocks)
        {
            patterns.AddRange(DetectEmptyCatchBlocks(fileContent, lines));
        }

        // Pattern 3: Unused using statements
        if (parameters.DetectUnusedUsings)
        {
            patterns.AddRange(DetectUnusedUsings(fileContent, extractionResult, lines));
        }

        // Pattern 4: Magic numbers/strings
        if (parameters.DetectMagicNumbers)
        {
            patterns.AddRange(DetectMagicNumbers(fileContent, lines));
        }

        // Pattern 5: Large methods (high complexity)
        if (parameters.DetectLargeMethods)
        {
            patterns.AddRange(DetectLargeMethods(extractionResult, lines));
        }

        // Pattern 6: Dead code (unused private methods and fields)
        if (parameters.DetectDeadCode)
        {
            patterns.AddRange(DetectDeadCode(fileContent, extractionResult, lines));
        }

        // Apply severity level filtering
        var filteredPatterns = patterns;
        if (parameters.SeverityLevels != null && parameters.SeverityLevels.Any())
        {
            filteredPatterns = patterns.Where(p => parameters.SeverityLevels.Contains(p.Severity)).ToList();
        }

        // Apply max results limit
        if (parameters.MaxResults > 0)
        {
            filteredPatterns = filteredPatterns.Take(parameters.MaxResults).ToList();
        }

        return Task.FromResult(filteredPatterns.OrderBy(p => p.LineNumber).ToList());
    }

    /// <summary>
    /// Detects async methods that don't use ConfigureAwait(false).
    /// </summary>
    private List<CodePattern> DetectAsyncWithoutConfigureAwait(string fileContent, TypeExtractionResult extractionResult, string[] lines)
    {
        var patterns = new List<CodePattern>();
        
        // Find await statements without ConfigureAwait
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            // Remove comments before checking
            var codeOnlyLine = System.Text.RegularExpressions.Regex.Replace(line, @"//.*$", "").Trim();
            if (codeOnlyLine.Contains("await ") && !codeOnlyLine.Contains("ConfigureAwait"))
            {
                // Check if this is in a library context (not main/console app)
                // Updated: Also detect in test files and general library code
                var isConsoleApp = fileContent.Contains("static void Main(") || 
                                 fileContent.Contains("static async Task Main(") ||
                                 fileContent.Contains("class Program");
                
                if (!isConsoleApp)
                {
                    // Extract the await expression for better suggestion
                    var awaitMatch = System.Text.RegularExpressions.Regex.Match(line, @"await\s+([^;]+);?");
                    var suggestion = awaitMatch.Success ? 
                        line.Replace(awaitMatch.Value, $"await {awaitMatch.Groups[1].Value.Trim().TrimEnd(';')}.ConfigureAwait(false);") :
                        line.Replace(";", ".ConfigureAwait(false);");
                        
                    patterns.Add(new CodePattern
                    {
                        Type = "AsyncWithoutConfigureAwait",
                        Severity = "Warning",
                        Message = "Consider using ConfigureAwait(false) for library code to avoid deadlocks",
                        LineNumber = i + 1,
                        LineContent = lines[i].TrimStart(),
                        Suggestion = suggestion
                    });
                }
            }
        }

        return patterns;
    }

    /// <summary>
    /// Detects empty catch blocks that swallow exceptions.
    /// </summary>
    private List<CodePattern> DetectEmptyCatchBlocks(string fileContent, string[] lines)
    {
        var patterns = new List<CodePattern>();
        
        for (int i = 0; i < lines.Length - 1; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("catch"))
            {
                // Find the opening brace
                int braceIndex = -1;
                for (int j = i; j < Math.Min(i + 3, lines.Length); j++)
                {
                    if (lines[j].Trim() == "{")
                    {
                        braceIndex = j;
                        break;
                    }
                }
                
                if (braceIndex != -1)
                {
                    // Look for closing brace and check if block is effectively empty
                    bool isEmpty = true;
                    int closingBraceIndex = -1;
                    
                    for (int j = braceIndex + 1; j < lines.Length; j++)
                    {
                        var innerLine = lines[j].Trim();
                        if (innerLine == "}")
                        {
                            closingBraceIndex = j;
                            break;
                        }
                        // If line has content other than comments/whitespace, not empty
                        if (!string.IsNullOrWhiteSpace(innerLine) && !innerLine.StartsWith("//"))
                        {
                            isEmpty = false;
                            break;
                        }
                    }
                    
                    if (isEmpty && closingBraceIndex != -1)
                    {
                        patterns.Add(new CodePattern
                        {
                            Type = "EmptyCatchBlock",
                            Severity = "Error",
                            Message = "Empty catch block swallows exceptions. Consider logging or rethrowing.",
                            LineNumber = i + 1,
                            LineContent = lines[i].TrimStart(),
                            Suggestion = "Add proper exception handling: logging, rethrowing, or specific handling"
                        });
                    }
                }
            }
        }

        return patterns;
    }

    /// <summary>
    /// Detects potentially unused using statements.
    /// </summary>
    private List<CodePattern> DetectUnusedUsings(string fileContent, TypeExtractionResult extractionResult, string[] lines)
    {
        var patterns = new List<CodePattern>();
        var usings = new List<(int lineNumber, string usingStatement, string namespaceName)>();
        
        
        // Collect all using statements
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("using ") && line.Contains(";") && !line.Contains("="))
            {
                // Extract namespace name up to the semicolon (ignoring comments)
                var semicolonIndex = line.IndexOf(';');
                var namespaceName = line.Substring(6, semicolonIndex - 6).Trim();
                usings.Add((i + 1, line, namespaceName));
            }
        }

        // Check each using for potential usage
        foreach (var (lineNumber, usingStatement, namespaceName) in usings)
        {
            // Simple heuristic: check if namespace types appear in code
            var parts = namespaceName.Split('.');
            var lastPart = parts.LastOrDefault();
            
            if (!string.IsNullOrEmpty(lastPart))
            {
                // Skip EXACT common framework namespaces that are commonly used
                // But allow detecting unused specific sub-namespaces like System.Unused.Namespace
                var exactCommonNamespaces = new[] { 
                    "System", "System.Collections.Generic", "System.Linq", "System.Threading",
                    "Microsoft.Extensions.Logging", "Microsoft.Extensions.DependencyInjection" 
                };
                if (exactCommonNamespaces.Contains(namespaceName))
                    continue;
                    
                // Check if any part of the namespace appears in the actual code
                bool isUsed = false;
                
                // Create a version of the file content without using statements and comments
                // Remove using statements first
                var codeWithoutUsings = System.Text.RegularExpressions.Regex.Replace(fileContent, @"using\s+[^;]+;\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
                
                // Remove single-line comments (// comments)
                var codeWithoutComments = System.Text.RegularExpressions.Regex.Replace(codeWithoutUsings, @"//.*$", "", System.Text.RegularExpressions.RegexOptions.Multiline);
                
                // Remove multi-line comments (/* comments */)
                codeWithoutComments = System.Text.RegularExpressions.Regex.Replace(codeWithoutComments, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);
                
                // Check if the last part of namespace appears as a word boundary in actual code
                // Use word boundaries to avoid partial matches
                var lastPartPattern = @"\b" + System.Text.RegularExpressions.Regex.Escape(lastPart) + @"\b";
                if (System.Text.RegularExpressions.Regex.IsMatch(codeWithoutComments, lastPartPattern))
                {
                    isUsed = true;
                }
                
                // Also check if any other parts are used (for nested namespaces)
                if (!isUsed && parts.Length > 1)
                {
                    for (int i = parts.Length - 2; i >= 0; i--)
                    {
                        if (!string.IsNullOrEmpty(parts[i]))
                        {
                            var partPattern = @"\b" + System.Text.RegularExpressions.Regex.Escape(parts[i]) + @"\b";
                            if (System.Text.RegularExpressions.Regex.IsMatch(codeWithoutComments, partPattern))
                            {
                                isUsed = true;
                                break;
                            }
                        }
                    }
                }
                
                if (!isUsed)
                {
                    patterns.Add(new CodePattern
                    {
                        Type = "PotentialUnusedUsing",
                        Severity = "Info",
                        Message = $"Using statement '{namespaceName}' may be unused",
                        LineNumber = lineNumber,
                        LineContent = lines[lineNumber - 1].TrimStart(),
                        Suggestion = "Remove if not needed to reduce compilation time"
                    });
                }
            }
        }

        return patterns;
    }

    /// <summary>
    /// Detects magic numbers and strings that should be constants.
    /// </summary>
    private List<CodePattern> DetectMagicNumbers(string fileContent, string[] lines)
    {
        var patterns = new List<CodePattern>();
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            // Simple regex to find numeric literals (excluding small numbers 0-10 and -1 which are commonly acceptable)
            var matches = System.Text.RegularExpressions.Regex.Matches(line, @"\b(?!0\b|[1-9]\b|10\b|-1\b)\d+\b");
            
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                // Skip if it's in a comment or string
                var beforeMatch = line.Substring(0, match.Index);
                if (beforeMatch.Contains("//") || beforeMatch.Count(c => c == '"') % 2 == 1)
                    continue;

                patterns.Add(new CodePattern
                {
                    Type = "MagicNumber",
                    Severity = "Info",
                    Message = $"Magic number '{match.Value}' should be replaced with a named constant",
                    LineNumber = i + 1,
                    LineContent = line.TrimStart(),
                    Suggestion = $"Consider: private const int SomeConstant = {match.Value};"
                });
            }
        }

        return patterns;
    }

    /// <summary>
    /// Detects methods that are too large based on line count.
    /// </summary>
    private List<CodePattern> DetectLargeMethods(TypeExtractionResult extractionResult, string[] lines)
    {
        var patterns = new List<CodePattern>();
        const int maxMethodLines = 50; // Configurable threshold
        
        if (extractionResult.Methods != null)
        {
            foreach (var method in extractionResult.Methods)
            {
                // Estimate method size (this is simplified - real implementation would use Tree-sitter)
                var methodStartLine = method.Line;
                var methodLines = EstimateMethodLength(lines, methodStartLine - 1);
                
                if (methodLines > maxMethodLines)
                {
                    patterns.Add(new CodePattern
                    {
                        Type = "LargeMethod",
                        Severity = "Warning",
                        Message = $"Method '{method.Name}' is {methodLines} lines long. Consider breaking it down.",
                        LineNumber = methodStartLine,
                        LineContent = $"Method: {method.Name}",
                        Suggestion = "Break method into smaller, focused methods with single responsibilities"
                    });
                }
            }
        }

        return patterns;
    }

    /// <summary>
    /// Estimates method length by counting lines until closing brace.
    /// </summary>
    private int EstimateMethodLength(string[] lines, int startIndex)
    {
        if (startIndex >= lines.Length) return 0;
        
        int braceCount = 0;
        int lineCount = 0;
        bool foundOpenBrace = false;
        
        for (int i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i];
            lineCount++;
            
            foreach (char c in line)
            {
                if (c == '{')
                {
                    braceCount++;
                    foundOpenBrace = true;
                }
                else if (c == '}')
                {
                    braceCount--;
                    if (foundOpenBrace && braceCount == 0)
                    {
                        return lineCount;
                    }
                }
            }
        }
        
        return lineCount; // Fallback
    }

    /// <summary>
    /// Detects unused private methods and fields (dead code).
    /// </summary>
    private List<CodePattern> DetectDeadCode(string fileContent, TypeExtractionResult extractionResult, string[] lines)
    {
        var patterns = new List<CodePattern>();

        // Remove comments and strings for accurate reference counting
        var codeOnly = RemoveCommentsAndStrings(fileContent);

        // Detect unused private methods
        if (extractionResult.Methods != null)
        {
            foreach (var method in extractionResult.Methods)
            {
                // Skip non-private methods
                if (!IsPrivateMethod(lines, method.Line - 1))
                    continue;

                // Skip constructors and special methods
                if (method.Name.StartsWith("<") || method.Name == ".ctor" || method.Name == ".cctor")
                    continue;

                // Count references to this method (excluding its declaration)
                var methodPattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(method.Name)}\s*\(";
                var matches = System.Text.RegularExpressions.Regex.Matches(codeOnly, methodPattern);

                // If only 1 match (the declaration itself), it's likely unused
                if (matches.Count <= 1)
                {
                    patterns.Add(new CodePattern
                    {
                        Type = "UnusedPrivateMethod",
                        Severity = "Warning",
                        Message = $"Private method '{method.Name}' appears to be unused",
                        LineNumber = method.Line,
                        LineContent = lines[method.Line - 1].TrimStart(),
                        Suggestion = "Remove unused method or make it public if it's intended for external use"
                    });
                }
            }
        }

        // Detect unused private fields
        var fieldPattern = @"private\s+(?:readonly\s+)?(?:static\s+)?(\w+(?:<[\w,\s<>]+>)?)\s+(_?\w+)\s*(?:=|;)";
        var fieldMatches = System.Text.RegularExpressions.Regex.Matches(fileContent, fieldPattern);

        foreach (System.Text.RegularExpressions.Match fieldMatch in fieldMatches)
        {
            var fieldName = fieldMatch.Groups[2].Value;
            var lineNumber = GetLineNumber(fileContent, fieldMatch.Index);

            // Count references to this field (excluding its declaration)
            var fieldRefPattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(fieldName)}\b";
            var refMatches = System.Text.RegularExpressions.Regex.Matches(codeOnly, fieldRefPattern);

            // If only 1 match (the declaration itself), it's unused
            if (refMatches.Count <= 1)
            {
                patterns.Add(new CodePattern
                {
                    Type = "UnusedPrivateField",
                    Severity = "Warning",
                    Message = $"Private field '{fieldName}' appears to be unused",
                    LineNumber = lineNumber,
                    LineContent = lines[lineNumber - 1].TrimStart(),
                    Suggestion = "Remove unused field to reduce code clutter"
                });
            }
        }

        return patterns;
    }

    /// <summary>
    /// Checks if a method at the given line is private.
    /// </summary>
    private bool IsPrivateMethod(string[] lines, int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= lines.Length)
            return false;

        // Look at current line and few lines before for access modifier
        for (int i = Math.Max(0, lineIndex - 3); i <= lineIndex; i++)
        {
            var line = lines[i].Trim();
            if (line.Contains("private "))
                return true;
            // If we see public/protected/internal first, it's not private
            if (line.Contains("public ") || line.Contains("protected ") || line.Contains("internal "))
                return false;
        }

        // Default to private if no explicit modifier (in classes)
        return true;
    }

    /// <summary>
    /// Removes comments and string literals from code to avoid false positives.
    /// </summary>
    private string RemoveCommentsAndStrings(string code)
    {
        // Remove single-line comments
        code = System.Text.RegularExpressions.Regex.Replace(code, @"//.*$", "", System.Text.RegularExpressions.RegexOptions.Multiline);

        // Remove multi-line comments
        code = System.Text.RegularExpressions.Regex.Replace(code, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);

        // Remove string literals (basic - doesn't handle all edge cases)
        code = System.Text.RegularExpressions.Regex.Replace(code, @"""([^""\\]|\\.)*""", "\"\"");

        return code;
    }

    /// <summary>
    /// Gets the line number for a character position in the file.
    /// </summary>
    private int GetLineNumber(string content, int charPosition)
    {
        if (charPosition < 0 || charPosition >= content.Length)
            return 1;

        var substring = content.Substring(0, charPosition);
        return substring.Count(c => c == '\n') + 1;
    }

    private AIOptimizedResponse<FindPatternsResult> CreateErrorResponse(string errorMessage)
    {
        return new AIOptimizedResponse<FindPatternsResult>
        {
            Success = false,
            Error = new ErrorInfo
            {
                Code = "PATTERN_DETECTION_ERROR",
                Message = errorMessage,
                Recovery = new RecoveryInfo
                {
                    Steps = new[]
                    {
                        "Check if the file path is correct",
                        "Ensure the file exists and is accessible",
                        "Verify the file is a supported programming language"
                    }
                }
            }
        };
    }

    private AIOptimizedResponse<FindPatternsResult> CreateSuccessResponse(FindPatternsResult result)
    {
        return new AIOptimizedResponse<FindPatternsResult>
        {
            Success = true,
            Message = $"Found {result.TotalPatterns} patterns in {Path.GetFileName(result.FilePath)}",
            Data = new AIResponseData<FindPatternsResult>
            {
                Results = result,
                Count = result.TotalPatterns
            }
        };
    }
}