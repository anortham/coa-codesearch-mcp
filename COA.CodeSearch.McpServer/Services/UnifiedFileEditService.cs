using DiffMatchPatch;
using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Unified file editing service that leverages DiffMatchPatch for reliable, concurrent file operations.
/// Replaces custom pattern matching with battle-tested algorithms.
/// </summary>
public class UnifiedFileEditService
{
    private readonly diff_match_patch _dmp;
    private readonly ILogger<UnifiedFileEditService> _logger;
    
    // File-level synchronization to prevent concurrent edits
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
    private static readonly SemaphoreSlim _lockCreationSemaphore = new(1, 1);

    public UnifiedFileEditService(ILogger<UnifiedFileEditService> logger)
    {
        _logger = logger;
        
        // Configure DiffMatchPatch for optimal performance
        _dmp = new diff_match_patch();
        _dmp.Diff_Timeout = 2.0f; // 2 seconds max for diff operations
        _dmp.Diff_EditCost = 4;   // Balanced cost for edit operations
        _dmp.Match_Threshold = 0.6f; // Good balance between precision and flexibility
        _dmp.Match_Distance = 1000;  // Reasonable search distance for matches
    }

    /// <summary>
    /// Applies search and replace operation using DiffMatchPatch for reliable multi-line and multi-occurrence replacement
    /// </summary>
    public async Task<FileEditResult> ApplySearchReplaceAsync(
        string filePath, 
        string searchPattern, 
        string replacement,
        EditOptions options,
        CancellationToken cancellationToken = default)
    {
        // Normalize file path for consistent locking
        var normalizedPath = Path.GetFullPath(filePath);
        
        // Get per-file lock
        var fileLock = await GetFileLockAsync(normalizedPath);
        await fileLock.WaitAsync(cancellationToken);
        
        try
        {
            _logger.LogDebug("Starting search/replace for {FilePath}: '{SearchPattern}' -> '{Replacement}'", 
                normalizedPath, searchPattern, replacement);

            // Read file content while preserving original encoding AND line endings
            var originalBytes = await File.ReadAllBytesAsync(normalizedPath, cancellationToken);
            var encoding = FileLineUtilities.DetectEncoding(originalBytes);
            var originalContent = encoding.GetString(originalBytes); // Preserves original line endings
            
            // Apply the replacement using DiffMatchPatch
            var modifiedContent = ApplyPatternReplacement(originalContent, searchPattern, replacement, options);
            
            // If no changes, return early
            if (originalContent == modifiedContent)
            {
                return new FileEditResult
                {
                    Success = true,
                    FilePath = normalizedPath,
                    ChangesMade = false,
                    OriginalContent = originalContent,
                    ModifiedContent = originalContent,
                    Diffs = new List<Diff>(),
                    Summary = "No changes needed - pattern not found"
                };
            }

            // Generate diffs for the changes
            var diffs = _dmp.diff_main(originalContent, modifiedContent);
            _dmp.diff_cleanupSemantic(diffs); // Clean up for better human readability

            var result = new FileEditResult
            {
                Success = true,
                FilePath = normalizedPath,
                ChangesMade = true,
                OriginalContent = originalContent,
                ModifiedContent = modifiedContent,
                Diffs = diffs.ToList(),
                Summary = GenerateChangeSummary(diffs.ToList())
            };

            // Apply changes to file if not in preview mode
            if (!options.PreviewMode)
            {
                await FileLineUtilities.WriteAllLinesPreservingEndingsAsync(
                    normalizedPath, 
                    FileLineUtilities.SplitLines(modifiedContent),
                    encoding,
                    originalContent,
                    cancellationToken);

                _logger.LogInformation("Applied {ChangeCount} changes to {FilePath}", 
                    CountChanges(diffs.ToList()), normalizedPath);
            }
            else
            {
                _logger.LogDebug("Preview mode - no changes written to {FilePath}", normalizedPath);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply search/replace to {FilePath}", normalizedPath);
            return new FileEditResult
            {
                Success = false,
                FilePath = normalizedPath,
                ChangesMade = false,
                ErrorMessage = ex.Message,
                Summary = $"Error: {ex.Message}"
            };
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <summary>
    /// Applies pattern replacement using DiffMatchPatch for various matching modes
    /// </summary>
    private string ApplyPatternReplacement(string content, string searchPattern, string replacement, EditOptions options)
    {
        switch (options.MatchMode?.ToLowerInvariant())
        {
            case "regex":
                return ApplyRegexReplacement(content, searchPattern, replacement, options.CaseSensitive);
            
            case "literal":
            case "exact":
                return ApplyLiteralReplacement(content, searchPattern, replacement, options.CaseSensitive);
            
            case "fuzzy":
            case "semantic":
                return ApplyFuzzyReplacement(content, searchPattern, replacement, options);
            
            default:
                // Default to literal replacement for safety
                return ApplyLiteralReplacement(content, searchPattern, replacement, options.CaseSensitive);
        }
    }

    private string ApplyLiteralReplacement(string content, string searchPattern, string replacement, bool caseSensitive)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        
        // Simple case: direct string replacement (handles all occurrences automatically)
        return content.Replace(searchPattern, replacement, comparison);
    }

    private string ApplyRegexReplacement(string content, string searchPattern, string replacement, bool caseSensitive)
    {
        var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        return Regex.Replace(content, searchPattern, replacement, options);
    }

    private string ApplyFuzzyReplacement(string content, string searchPattern, string replacement, EditOptions options)
    {
        // Configure DMP with user-specified fuzzy parameters
        var originalThreshold = _dmp.Match_Threshold;
        var originalDistance = _dmp.Match_Distance;

        try
        {
            _dmp.Match_Threshold = options.FuzzyThreshold;
            _dmp.Match_Distance = options.FuzzyDistance;

            // Find ALL fuzzy matches in the content
            var matchPositions = new List<int>();
            var searchStart = 0;
            var lastMatchPos = -1;

            while (searchStart < content.Length)
            {
                var matchPos = _dmp.match_main(content, searchPattern, searchStart);

                if (matchPos == -1)
                    break; // No more matches

                // Prevent infinite loop - if we found the same position, advance by 1
                if (matchPos == lastMatchPos)
                {
                    searchStart++;
                    continue;
                }

                matchPositions.Add(matchPos);
                lastMatchPos = matchPos;

                // Move search position forward to find next match
                // Advance by pattern length OR at least 1 character to ensure progress
                searchStart = matchPos + Math.Max(1, searchPattern.Length);
            }

            if (matchPositions.Count == 0)
                return content; // No matches found

            // Apply replacements from END to START to preserve positions
            var result = new StringBuilder(content);
            foreach (var pos in matchPositions.OrderByDescending(p => p))
            {
                result.Remove(pos, searchPattern.Length);
                result.Insert(pos, replacement);
            }

            return result.ToString();
        }
        finally
        {
            // Restore original DMP settings
            _dmp.Match_Threshold = originalThreshold;
            _dmp.Match_Distance = originalDistance;
        }
    }

    /// <summary>
    /// Gets or creates a semaphore for the specified file path to ensure thread-safe operations
    /// </summary>
    private async Task<SemaphoreSlim> GetFileLockAsync(string normalizedPath)
    {
        if (_fileLocks.TryGetValue(normalizedPath, out var existingLock))
            return existingLock;

        await _lockCreationSemaphore.WaitAsync();
        try
        {
            // Double-check pattern - another thread might have created it
            if (_fileLocks.TryGetValue(normalizedPath, out existingLock))
                return existingLock;

            var newLock = new SemaphoreSlim(1, 1);
            _fileLocks[normalizedPath] = newLock;
            return newLock;
        }
        finally
        {
            _lockCreationSemaphore.Release();
        }
    }

    /// <summary>
    /// Generates a human-readable summary of the changes made
    /// </summary>
    private string GenerateChangeSummary(List<Diff> diffs)
    {
        var insertCount = diffs.Count(d => d.operation == Operation.INSERT);
        var deleteCount = diffs.Count(d => d.operation == Operation.DELETE);
        var equalCount = diffs.Count(d => d.operation == Operation.EQUAL);

        var changes = new List<string>();
        
        if (deleteCount > 0)
            changes.Add($"{deleteCount} deletion{(deleteCount == 1 ? "" : "s")}");
            
        if (insertCount > 0)
            changes.Add($"{insertCount} insertion{(insertCount == 1 ? "" : "s")}");

        return changes.Any() 
            ? string.Join(", ", changes)
            : "No changes";
    }

    /// <summary>
    /// Counts the actual number of character changes (not diff blocks)
    /// </summary>
    private int CountChanges(List<Diff> diffs)
    {
        return diffs
            .Where(d => d.operation != Operation.EQUAL)
            .Sum(d => d.text?.Length ?? 0);
    }

    /// <summary>
    /// Clean up file locks periodically to prevent memory leaks
    /// </summary>
    public void CleanupUnusedLocks()
    {
        var keysToRemove = new List<string>();
        
        foreach (var kvp in _fileLocks)
        {
            if (kvp.Value.CurrentCount == 1) // Not currently locked
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            if (_fileLocks.TryRemove(key, out var removedLock))
            {
                removedLock.Dispose();
            }
        }

        if (keysToRemove.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} unused file locks", keysToRemove.Count);
        }
    }

    /// <summary>
    /// Inserts content at the specified line number with proper concurrency protection
    /// </summary>
    public async Task<FileEditResult> InsertAtLineAsync(
        string filePath,
        int lineNumber,
        string content,
        bool preserveIndentation = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        var fileLock = await GetFileLockAsync(normalizedPath);
        await fileLock.WaitAsync(cancellationToken);

        try
        {
            _logger.LogDebug("Inserting content at line {LineNumber} in {FilePath}", lineNumber, normalizedPath);

            // Read file content with proper encoding preservation
            var (lines, encoding) = await FileLineUtilities.ReadFileWithEncodingAsync(normalizedPath, cancellationToken);
            var originalContent = string.Join(Environment.NewLine, lines);

            // Validate line number
            if (lineNumber < 1 || lineNumber > lines.Length + 1)
            {
            return new FileEditResult
            {
            Success = false,
            ErrorMessage = $"Line number {lineNumber} exceeds file length. File has {lines.Length} lines. Valid range: 1-{lines.Length + 1}",
            FilePath = normalizedPath
            };
            }

            // Handle indentation
            string finalContent = content;
            string detectedIndentation = "none";
            if (preserveIndentation && lines.Length > 0)
            {
                var indentation = FileLineUtilities.DetectIndentationForInsertion(lines, lineNumber - 1);
                detectedIndentation = $"'{indentation}'";  // Format as expected by tests
                var contentLines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                
                // Smart indentation: only apply base indentation to lines that need it
                finalContent = string.Join(Environment.NewLine, contentLines.Select(line => 
                {
                    if (string.IsNullOrWhiteSpace(line))
                        return line;
                        
                    // If the line already starts with the target indentation or more, don't add more
                    if (!string.IsNullOrEmpty(indentation) && line.StartsWith(indentation))
                        return line;
                        
                    // If the line has any leading whitespace, it might be pre-indented content
                    // In this case, we should be more careful about applying indentation
                    var lineIndentation = FileLineUtilities.ExtractIndentation(line);
                    if (!string.IsNullOrEmpty(lineIndentation))
                    {
                        // The line is already indented. We need to decide if we should apply base indentation.
                        // If the existing indentation is significant (4+ spaces or contains tabs), 
                        // assume it's intentional and preserve it as-is
                        if (lineIndentation.Length >= 4 || lineIndentation.Contains('\t'))
                            return line;
                    }
                    
                    // Apply base indentation to unindented or minimally indented lines
                    return indentation + line;
                }));
            }

            // Insert the content
            var newLines = lines.ToList();
            var insertionLines = finalContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            newLines.InsertRange(lineNumber - 1, insertionLines);

            var modifiedContent = string.Join(Environment.NewLine, newLines);

            // Generate diffs for the changes
            var diffs = _dmp.diff_main(originalContent, modifiedContent);
            _dmp.diff_cleanupSemantic(diffs);

            var result = new FileEditResult
            {
            Success = true,
            FilePath = normalizedPath,
            ChangesMade = true,
            OriginalContent = originalContent,
            ModifiedContent = modifiedContent,
            Diffs = diffs.ToList(),
            DetectedIndentation = detectedIndentation,
            Summary = $"Inserted {insertionLines.Length} line{(insertionLines.Length == 1 ? "" : "s")} at line {lineNumber}"
            };

            // Write changes to file
            await FileLineUtilities.WriteAllLinesPreservingEndingsAsync(
                normalizedPath,
                newLines.ToArray(),
                encoding,
                originalContent,
                cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert content at line {LineNumber} in {FilePath}", lineNumber, normalizedPath);
            return new FileEditResult
            {
                Success = false,
                ErrorMessage = $"Failed to insert content: {ex.Message}",
                FilePath = normalizedPath
            };
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <summary>
    /// Replaces a range of lines with new content using proper concurrency protection
    /// </summary>
    public async Task<FileEditResult> ReplaceLinesAsync(
        string filePath,
        int startLine,
        int? endLine,
        string newContent,
        bool preserveIndentation = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        var fileLock = await GetFileLockAsync(normalizedPath);
        await fileLock.WaitAsync(cancellationToken);

        try
        {
            _logger.LogDebug("Replacing lines {StartLine}-{EndLine} in {FilePath}", startLine, endLine ?? startLine, normalizedPath);

            // Read file content with proper encoding preservation
            var (lines, encoding) = await FileLineUtilities.ReadFileWithEncodingAsync(normalizedPath, cancellationToken);
            var originalContent = string.Join(Environment.NewLine, lines);

            var actualEndLine = endLine ?? startLine;

            // Validate line range
            if (startLine < 1 || startLine > lines.Length)
            {
            return new FileEditResult
            {
            Success = false,
            ErrorMessage = $"StartLine {startLine} is out of range. File has {lines.Length} lines.",
            FilePath = normalizedPath
            };
            }
            
            if (actualEndLine < 1 || actualEndLine > lines.Length)
            {
            return new FileEditResult
            {
            Success = false,
            ErrorMessage = $"EndLine {actualEndLine} is out of range. File has {lines.Length} lines.",
            FilePath = normalizedPath
            };
            }
            
            if (startLine > actualEndLine)
            {
            return new FileEditResult
            {
            Success = false,
            ErrorMessage = $"Invalid line range {startLine}-{actualEndLine}. File has {lines.Length} lines.",
            FilePath = normalizedPath
            };
            }

            // Handle indentation for new content
            string finalContent = newContent;
            if (preserveIndentation && lines.Length > 0 && !string.IsNullOrWhiteSpace(newContent))
            {
            var contentLines = newContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            
            // Only apply indentation if the content doesn't already have leading whitespace
            var needsIndentation = contentLines.Any(line => !string.IsNullOrWhiteSpace(line) && !char.IsWhiteSpace(line[0]));
            
            if (needsIndentation)
            {
                var referenceLineIndex = Math.Min(startLine - 1, lines.Length - 1);
                var indentation = FileLineUtilities.DetectIndentationForInsertion(lines, referenceLineIndex);
                finalContent = string.Join(Environment.NewLine, contentLines.Select(line => 
                string.IsNullOrWhiteSpace(line) ? line : indentation + line.TrimStart()));
            }
            }

            // Replace the lines
            var newLines = lines.ToList();
            var replacementLines = string.IsNullOrEmpty(finalContent) 
            ? new string[0] 
            : finalContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            
            // Capture the original content that will be replaced
            var linesToRemove = actualEndLine - startLine + 1;
            var deletedContent = string.Join(Environment.NewLine, lines.Skip(startLine - 1).Take(linesToRemove));
            
            newLines.RemoveRange(startLine - 1, linesToRemove);
            newLines.InsertRange(startLine - 1, replacementLines);

            var modifiedContent = string.Join(Environment.NewLine, newLines);

            // Generate diffs for the changes
            var diffs = _dmp.diff_main(originalContent, modifiedContent);
            _dmp.diff_cleanupSemantic(diffs);
            var result = new FileEditResult
            {
            Success = true,
            FilePath = normalizedPath,
            ChangesMade = true,
            OriginalContent = originalContent,
            ModifiedContent = modifiedContent,
            Diffs = diffs.ToList(),
            DeletedContent = deletedContent,  // Store deleted content for recovery
            Summary = $"Replaced {linesToRemove} line{(linesToRemove == 1 ? "" : "s")} with {replacementLines.Length} line{(replacementLines.Length == 1 ? "" : "s")}"
            };

            // Write changes to file
            await FileLineUtilities.WriteAllLinesPreservingEndingsAsync(
                normalizedPath,
                newLines.ToArray(),
                encoding,
                originalContent,
                cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to replace lines {StartLine}-{EndLine} in {FilePath}", startLine, endLine, normalizedPath);
            return new FileEditResult
            {
                Success = false,
                ErrorMessage = $"Failed to replace lines: {ex.Message}",
                FilePath = normalizedPath
            };
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <summary>
    /// Deletes a range of lines using proper concurrency protection
    /// </summary>
    public async Task<FileEditResult> DeleteLinesAsync(
        string filePath,
        int startLine,
        int? endLine,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        var fileLock = await GetFileLockAsync(normalizedPath);
        await fileLock.WaitAsync(cancellationToken);

        try
        {
            _logger.LogDebug("Deleting lines {StartLine}-{EndLine} in {FilePath}", startLine, endLine ?? startLine, normalizedPath);

            // Read file content with proper encoding preservation
            var (lines, encoding) = await FileLineUtilities.ReadFileWithEncodingAsync(normalizedPath, cancellationToken);
            var originalContent = string.Join(Environment.NewLine, lines);

            var actualEndLine = endLine ?? startLine;

            // Validate line range
            if (startLine < 1 || startLine > lines.Length)
            {
            return new FileEditResult
            {
            Success = false,
            ErrorMessage = $"StartLine {startLine} is out of range. File has {lines.Length} lines.",
            FilePath = normalizedPath
            };
            }
            
            if (actualEndLine < 1 || actualEndLine > lines.Length)
            {
            return new FileEditResult
            {
            Success = false,
            ErrorMessage = $"EndLine {actualEndLine} is out of range. File has {lines.Length} lines.",
            FilePath = normalizedPath
            };
            }
            
            if (startLine > actualEndLine)
            {
            return new FileEditResult
            {
            Success = false,
            ErrorMessage = $"Invalid line range {startLine}-{actualEndLine}. File has {lines.Length} lines.",
            FilePath = normalizedPath
            };
            }

            // Delete the lines
            var newLines = lines.ToList();
            var linesToDelete = actualEndLine - startLine + 1;
            var deletedContent = string.Join(Environment.NewLine, lines.Skip(startLine - 1).Take(linesToDelete));
            
            newLines.RemoveRange(startLine - 1, linesToDelete);

            var modifiedContent = string.Join(Environment.NewLine, newLines);

            // Generate diffs for the changes
            var diffs = _dmp.diff_main(originalContent, modifiedContent);
            _dmp.diff_cleanupSemantic(diffs);

            var result = new FileEditResult
            {
                Success = true,
                FilePath = normalizedPath,
                ChangesMade = true,
                OriginalContent = originalContent,
                ModifiedContent = modifiedContent,
                Diffs = diffs.ToList(),
                Summary = $"Deleted {linesToDelete} line{(linesToDelete == 1 ? "" : "s")} (lines {startLine}-{actualEndLine})",
                DeletedContent = deletedContent  // Store deleted content for recovery
            };

            // Write changes to file
            await FileLineUtilities.WriteAllLinesPreservingEndingsAsync(
                normalizedPath,
                newLines.ToArray(),
                encoding,
                originalContent,
                cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete lines {StartLine}-{EndLine} in {FilePath}", startLine, endLine, normalizedPath);
            return new FileEditResult
            {
                Success = false,
                ErrorMessage = $"Failed to delete lines: {ex.Message}",
                FilePath = normalizedPath
            };
        }
        finally
        {
            fileLock.Release();
        }
    }
}