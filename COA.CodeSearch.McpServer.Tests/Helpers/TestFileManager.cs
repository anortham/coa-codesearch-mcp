using System.Text;
using System.Text.RegularExpressions;
using COA.CodeSearch.McpServer.Services;

namespace COA.CodeSearch.McpServer.Tests.Helpers;

/// <summary>
/// Manages real-world test files for comprehensive line editing validation.
/// Implements the copy-original-edit-diff pattern for bulletproof accuracy testing.
/// </summary>
public class TestFileManager : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly string _testDataDirectory;
    private bool _disposed = false;

    public TestFileManager()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), "CodeSearchTests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDataDirectory);
    }

    /// <summary>
    /// Creates a temporary copy of a source file for testing.
    /// Preserves original encoding and line endings.
    /// </summary>
    public async Task<TestFileInstance> CreateTestCopyAsync(string sourceFilePath, string? customName = null)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException($"Source file not found: {sourceFilePath}");

        var fileName = customName ?? $"test_{Path.GetFileName(sourceFilePath)}";
        var tempFilePath = Path.Combine(_testDataDirectory, fileName);
        
        // Read with encoding detection and preserve exactly
        var (originalLines, originalEncoding) = await FileLineUtilities.ReadFileWithEncodingAsync(sourceFilePath);
        var originalContent = await File.ReadAllTextAsync(sourceFilePath, originalEncoding);
        
        // Copy to temp location preserving everything
        await File.WriteAllTextAsync(tempFilePath, originalContent, originalEncoding);
        
        _tempFiles.Add(tempFilePath);

        return new TestFileInstance
        {
            FilePath = tempFilePath,
            OriginalContent = originalContent,
            OriginalLines = originalLines,
            OriginalEncoding = originalEncoding,
            SourceFilePath = sourceFilePath
        };
    }

    /// <summary>
    /// Creates a test file from content with specified characteristics.
    /// </summary>
    public async Task<TestFileInstance> CreateTestFileAsync(
        string content, 
        string fileName,
        Encoding? encoding = null,
        LineEndingType lineEndings = LineEndingType.System)
    {
        encoding ??= new UTF8Encoding(false); // UTF-8 without BOM by default
        
        // Normalize line endings
        content = NormalizeLineEndings(content, lineEndings);
        
        var tempFilePath = Path.Combine(_testDataDirectory, fileName);
        await File.WriteAllTextAsync(tempFilePath, content, encoding);
        
        _tempFiles.Add(tempFilePath);

        var (lines, detectedEncoding) = await FileLineUtilities.ReadFileWithEncodingAsync(tempFilePath);
        
        return new TestFileInstance
        {
            FilePath = tempFilePath,
            OriginalContent = content,
            OriginalLines = lines,
            OriginalEncoding = detectedEncoding,
            SourceFilePath = null
        };
    }

    /// <summary>
    /// Validates that a file edit operation preserved integrity.
    /// </summary>
    public async Task<FileEditValidationResult> ValidateEditAsync(TestFileInstance testFile)
    {
        var result = new FileEditValidationResult { Success = true };
        
        try
        {
            // Read current state
            var (currentLines, currentEncoding) = await FileLineUtilities.ReadFileWithEncodingAsync(testFile.FilePath);
            var currentContent = await File.ReadAllTextAsync(testFile.FilePath, currentEncoding);

            // Encoding preservation check
            if (!EncodingsMatch(testFile.OriginalEncoding, currentEncoding))
            {
                result.Success = false;
                result.Issues.Add($"Encoding changed from {testFile.OriginalEncoding.EncodingName} to {currentEncoding.EncodingName}");
            }

            // Generate detailed diff
            result.LinesDiff = GenerateLinesDiff(testFile.OriginalLines, currentLines);
            result.ContentDiff = GenerateContentDiff(testFile.OriginalContent, currentContent);
            
            // File integrity checks
            result.FileSizeBytes = new FileInfo(testFile.FilePath).Length;
            result.CurrentLineCount = currentLines.Length;
            result.OriginalLineCount = testFile.OriginalLines.Length;
            
            // Line ending consistency check
            var originalEndings = DetectLineEndingTypes(testFile.OriginalContent);
            var currentEndings = DetectLineEndingTypes(currentContent);
            
            if (!originalEndings.SetEquals(currentEndings))
            {
                result.Success = false;
                result.Issues.Add($"Line endings changed from [{string.Join(", ", originalEndings)}] to [{string.Join(", ", currentEndings)}]");
            }

            result.CurrentContent = currentContent;
            result.CurrentLines = currentLines;
            result.CurrentEncoding = currentEncoding;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Issues.Add($"Validation failed with exception: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Generates a unified diff between original and current content.
    /// </summary>
    private string GenerateContentDiff(string original, string current)
    {
        var originalLines = original.Split('\n');
        var currentLines = current.Split('\n');
        
        var diff = new StringBuilder();
        diff.AppendLine("--- Original");
        diff.AppendLine("+++ Current");
        
        // Simple diff implementation - could be enhanced with proper LCS algorithm
        var maxLines = Math.Max(originalLines.Length, currentLines.Length);
        for (int i = 0; i < maxLines; i++)
        {
            var origLine = i < originalLines.Length ? originalLines[i] : null;
            var currLine = i < currentLines.Length ? currentLines[i] : null;
            
            if (origLine != currLine)
            {
                if (origLine != null)
                    diff.AppendLine($"-{origLine}");
                if (currLine != null)
                    diff.AppendLine($"+{currLine}");
            }
        }
        
        return diff.ToString();
    }

    /// <summary>
    /// Generates line-by-line diff information.
    /// </summary>
    private List<LineDiff> GenerateLinesDiff(string[] originalLines, string[] currentLines)
    {
        var diffs = new List<LineDiff>();
        var maxLines = Math.Max(originalLines.Length, currentLines.Length);
        
        for (int i = 0; i < maxLines; i++)
        {
            var origLine = i < originalLines.Length ? originalLines[i] : null;
            var currLine = i < currentLines.Length ? currentLines[i] : null;
            
            if (origLine != currLine)
            {
                diffs.Add(new LineDiff
                {
                    LineNumber = i + 1,
                    OriginalLine = origLine,
                    CurrentLine = currLine,
                    ChangeType = GetChangeType(origLine, currLine)
                });
            }
        }
        
        return diffs;
    }

    private LineChangeType GetChangeType(string? original, string? current)
    {
        if (original == null) return LineChangeType.Added;
        if (current == null) return LineChangeType.Deleted;
        return LineChangeType.Modified;
    }

    private bool EncodingsMatch(Encoding enc1, Encoding enc2)
    {
        // Compare by code page and BOM presence
        return enc1.CodePage == enc2.CodePage && 
               GetBomBytes(enc1).SequenceEqual(GetBomBytes(enc2));
    }

    private byte[] GetBomBytes(Encoding encoding)
    {
        return encoding.GetPreamble();
    }

    private HashSet<string> DetectLineEndingTypes(string content)
    {
        var endings = new HashSet<string>();
        
        if (content.Contains("\r\n")) endings.Add("CRLF");
        if (Regex.IsMatch(content, @"(?<!\r)\n")) endings.Add("LF");
        if (Regex.IsMatch(content, @"\r(?!\n)")) endings.Add("CR");
        
        return endings;
    }

    private string NormalizeLineEndings(string content, LineEndingType type)
    {
        return type switch
        {
            LineEndingType.Windows => content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n"),
            LineEndingType.Unix => content.Replace("\r\n", "\n").Replace("\r", "\n"),
            LineEndingType.Mac => content.Replace("\r\n", "\r").Replace("\n", "\r"),
            LineEndingType.System => content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine),
            _ => content
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // Clean up temp files
            foreach (var tempFile in _tempFiles)
            {
                try
                {
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            try
            {
                if (Directory.Exists(_testDataDirectory))
                    Directory.Delete(_testDataDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }

            _disposed = true;
        }
    }
}

/// <summary>
/// Represents a test file instance with metadata.
/// </summary>
public class TestFileInstance
{
    public string FilePath { get; init; } = string.Empty;
    public string OriginalContent { get; init; } = string.Empty;
    public string[] OriginalLines { get; init; } = Array.Empty<string>();
    public Encoding OriginalEncoding { get; init; } = Encoding.UTF8;
    public string? SourceFilePath { get; init; }
}

/// <summary>
/// Results of file edit validation.
/// </summary>
public class FileEditValidationResult
{
    public bool Success { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<LineDiff> LinesDiff { get; set; } = new();
    public string ContentDiff { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public int OriginalLineCount { get; set; }
    public int CurrentLineCount { get; set; }
    public string CurrentContent { get; set; } = string.Empty;
    public string[] CurrentLines { get; set; } = Array.Empty<string>();
    public Encoding CurrentEncoding { get; set; } = Encoding.UTF8;
}

/// <summary>
/// Represents a line-level difference.
/// </summary>
public class LineDiff
{
    public int LineNumber { get; set; }
    public string? OriginalLine { get; set; }
    public string? CurrentLine { get; set; }
    public LineChangeType ChangeType { get; set; }
}

public enum LineChangeType
{
    Added,
    Deleted,
    Modified
}

public enum LineEndingType
{
    System,
    Windows, // CRLF
    Unix,    // LF  
    Mac      // CR
}