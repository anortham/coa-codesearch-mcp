using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace COA.CodeSearch.McpServer.Tests.Helpers;

/// <summary>
/// Provides precise before/after validation for file edit operations.
/// Performs line-by-line comparison, encoding validation, and metadata preservation checks.
/// Essential for bulletproof accuracy testing of line editing tools.
/// </summary>
public class DiffValidator
{
    /// <summary>
    /// Validates that a file edit operation produced exactly the expected changes
    /// with no unintended side effects.
    /// </summary>
    public static DiffValidationResult ValidateEdit(
        string originalFilePath,
        string modifiedFilePath,
        EditExpectation expectation)
    {
        var result = new DiffValidationResult
        {
            OriginalFile = originalFilePath,
            ModifiedFile = modifiedFilePath,
            Expectation = expectation
        };

        try
        {
            // Read both files with encoding detection
            var originalData = ReadFileWithMetadata(originalFilePath);
            var modifiedData = ReadFileWithMetadata(modifiedFilePath);

            // Validate encoding preservation
            ValidateEncodingPreservation(originalData, modifiedData, result);

            // Validate line ending preservation
            ValidateLineEndingPreservation(originalData, modifiedData, result);

            // Perform line-by-line diff analysis
            PerformLineDiff(originalData.Lines, modifiedData.Lines, expectation, result);

            // Calculate metrics
            CalculateMetrics(originalData.Lines, modifiedData.Lines, result);

            result.IsValid = result.Violations.Count == 0;
        }
        catch (Exception ex)
        {
            result.Violations.Add(new DiffViolation
            {
                Type = DiffViolationType.ValidationError,
                Message = $"Validation failed with exception: {ex.Message}",
                Context = ex.ToString()
            });
            result.IsValid = false;
        }

        return result;
    }

    /// <summary>
    /// Creates a detailed textual diff report for debugging purposes.
    /// </summary>
    public static string GenerateDiffReport(DiffValidationResult result)
    {
        var report = new StringBuilder();
        
        report.AppendLine("=== DIFF VALIDATION REPORT ===");
        report.AppendLine($"Original: {Path.GetFileName(result.OriginalFile)}");
        report.AppendLine($"Modified: {Path.GetFileName(result.ModifiedFile)}");
        report.AppendLine($"Status: {(result.IsValid ? "✅ VALID" : "❌ INVALID")}");
        report.AppendLine();

        if (result.Metrics != null)
        {
            report.AppendLine("=== METRICS ===");
            report.AppendLine($"Original lines: {result.Metrics.OriginalLineCount}");
            report.AppendLine($"Modified lines: {result.Metrics.ModifiedLineCount}");
            report.AppendLine($"Lines added: +{result.Metrics.LinesAdded}");
            report.AppendLine($"Lines removed: -{result.Metrics.LinesRemoved}");
            report.AppendLine($"Lines changed: ~{result.Metrics.LinesChanged}");
            report.AppendLine($"Net change: {result.Metrics.NetLineChange:+#;-#;0}");
            report.AppendLine();
        }

        if (result.Violations.Any())
        {
            report.AppendLine("=== VIOLATIONS ===");
            foreach (var violation in result.Violations)
            {
                var icon = violation.Type switch
                {
                    DiffViolationType.UnexpectedChange => "🔄",
                    DiffViolationType.MissingExpectedChange => "❓",
                    DiffViolationType.EncodingMismatch => "🔤",
                    DiffViolationType.LineEndingMismatch => "⏎",
                    DiffViolationType.ValidationError => "💥",
                    _ => "⚠️"
                };
                
                report.AppendLine($"{icon} {violation.Type}: {violation.Message}");
                if (!string.IsNullOrEmpty(violation.Context))
                {
                    report.AppendLine($"   Context: {violation.Context}");
                }
            }
            report.AppendLine();
        }

        if (result.ChangeSummary.Any())
        {
            report.AppendLine("=== CHANGE SUMMARY ===");
            foreach (var change in result.ChangeSummary)
            {
                var changeIcon = change.Type switch
                {
                    ChangeType.Addition => "+",
                    ChangeType.Deletion => "-",
                    ChangeType.Modification => "~",
                    _ => "?"
                };
                report.AppendLine($"{changeIcon} Line {change.LineNumber}: {change.Description}");
            }
        }

        return report.ToString();
    }

    private static FileData ReadFileWithMetadata(string filePath)
    {
        // Read raw bytes first for encoding detection
        var bytes = File.ReadAllBytes(filePath);
        var encoding = DetectEncoding(bytes);
        var content = encoding.GetString(bytes);
        
        return new FileData
        {
            FilePath = filePath,
            Content = content,
            Lines = SplitLines(content),
            Encoding = encoding,
            LineEnding = DetectLineEnding(content),
            SizeBytes = bytes.Length
        };
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        // Check for BOM markers
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;
        
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode; // UTF-16 LE
            
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode; // UTF-16 BE

        // Default to UTF-8 without BOM
        return new UTF8Encoding(false);
    }

    private static string DetectLineEnding(string content)
    {
        if (content.Contains("\r\n")) return "\r\n";
        if (content.Contains("\n")) return "\n";
        if (content.Contains("\r")) return "\r";
        return Environment.NewLine;
    }

    private static string[] SplitLines(string content)
    {
        if (string.IsNullOrEmpty(content))
            return Array.Empty<string>();

        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        // Handle files without trailing newlines correctly
        if (lines.Length > 0 && string.IsNullOrEmpty(lines[^1]))
        {
            Array.Resize(ref lines, lines.Length - 1);
        }
        
        return lines;
    }

    private static void ValidateEncodingPreservation(FileData original, FileData modified, DiffValidationResult result)
    {
        if (!original.Encoding.Equals(modified.Encoding))
        {
            result.Violations.Add(new DiffViolation
            {
                Type = DiffViolationType.EncodingMismatch,
                Message = $"Encoding changed from {original.Encoding.EncodingName} to {modified.Encoding.EncodingName}",
                Context = $"Original: {original.Encoding.WebName}, Modified: {modified.Encoding.WebName}"
            });
        }
    }

    private static void ValidateLineEndingPreservation(FileData original, FileData modified, DiffValidationResult result)
    {
        if (result.Expectation.RequireLineEndingPreservation && original.LineEnding != modified.LineEnding)
        {
            result.Violations.Add(new DiffViolation
            {
                Type = DiffViolationType.LineEndingMismatch,
                Message = $"Line endings changed from {FormatLineEnding(original.LineEnding)} to {FormatLineEnding(modified.LineEnding)}",
                Context = $"This can cause issues with version control and cross-platform compatibility"
            });
        }
    }

    private static string FormatLineEnding(string lineEnding)
    {
        return lineEnding switch
        {
            "\r\n" => "CRLF (Windows)",
            "\n" => "LF (Unix)",
            "\r" => "CR (Mac)",
            _ => $"Unknown ({lineEnding.Replace("\r", "\\r").Replace("\n", "\\n")})"
        };
    }

    private static void PerformLineDiff(string[] originalLines, string[] modifiedLines, EditExpectation expectation, DiffValidationResult result)
    {
        // Use a simple but effective diff algorithm
        var changes = ComputeChanges(originalLines, modifiedLines);
        
        foreach (var change in changes)
        {
            result.ChangeSummary.Add(change);
            
            // Validate against expectation
            if (!IsExpectedChange(change, expectation))
            {
                result.Violations.Add(new DiffViolation
                {
                    Type = DiffViolationType.UnexpectedChange,
                    Message = $"Unexpected {change.Type} at line {change.LineNumber}",
                    Context = change.Description
                });
            }
        }

        // Check for missing expected changes
        ValidateExpectedChanges(changes, expectation, result);
    }

    private static List<Change> ComputeChanges(string[] original, string[] modified)
    {
        var changes = new List<Change>();
        
        // Use DiffPlex for reliable line-by-line diffing
        var originalText = string.Join("\n", original);
        var modifiedText = string.Join("\n", modified);
        
        
        var differ = new Differ();
        var diffBuilder = new InlineDiffBuilder(differ);
        var diffResult = diffBuilder.BuildDiffModel(originalText, modifiedText);
        
        int lineNumber = 1;
        
        foreach (var line in diffResult.Lines)
        {
            switch (line.Type)
            {
                case DiffPlex.DiffBuilder.Model.ChangeType.Inserted:
                    changes.Add(new Change
                    {
                        Type = ChangeType.Addition,
                        LineNumber = lineNumber,
                        Content = line.Text,
                        Description = $"Added: {line.Text}"
                    });
                    lineNumber++;
                    break;

                case DiffPlex.DiffBuilder.Model.ChangeType.Deleted:
                    changes.Add(new Change
                    {
                        Type = ChangeType.Deletion,
                        LineNumber = lineNumber,
                        Content = line.Text,
                        Description = $"Deleted: {line.Text}"
                    });
                    // Don't increment line number for deletions
                    break;

                case DiffPlex.DiffBuilder.Model.ChangeType.Modified:
                    changes.Add(new Change
                    {
                        Type = ChangeType.Modification,
                        LineNumber = lineNumber,
                        Content = line.Text,
                        Description = $"Changed: {line.Text}"
                    });
                    lineNumber++;
                    break;
                    
                case DiffPlex.DiffBuilder.Model.ChangeType.Unchanged:
                    // No change needed
                    lineNumber++;
                    break;
            }
        }
        
        return changes;
    }


    private static bool IsExpectedChange(Change change, EditExpectation expectation)
    {
        // This is where we validate against the specific edit expectation
        // For now, implement basic validation - can be enhanced based on needs
        
        if (expectation.AllowedOperations?.Contains(change.Type) == false)
            return false;

        if (expectation.TargetLineRange != null)
        {
            var (start, end) = expectation.TargetLineRange.Value;
            if (change.LineNumber < start || change.LineNumber > end)
                return false;
        }

        return true;
    }

    private static void ValidateExpectedChanges(List<Change> actualChanges, EditExpectation expectation, DiffValidationResult result)
    {
        if (expectation.RequiredChanges != null)
        {
            foreach (var requiredChange in expectation.RequiredChanges)
            {
                var found = actualChanges.Any(c => 
                    c.Type == requiredChange.Type && 
                    c.LineNumber == requiredChange.LineNumber);
                
                if (!found)
                {
                    result.Violations.Add(new DiffViolation
                    {
                        Type = DiffViolationType.MissingExpectedChange,
                        Message = $"Expected {requiredChange.Type} at line {requiredChange.LineNumber} was not found",
                        Context = requiredChange.Description
                    });
                }
            }
        }
    }

    private static void CalculateMetrics(string[] originalLines, string[] modifiedLines, DiffValidationResult result)
    {
        result.Metrics = new DiffMetrics
        {
            OriginalLineCount = originalLines.Length,
            ModifiedLineCount = modifiedLines.Length,
            NetLineChange = modifiedLines.Length - originalLines.Length
        };

        // Calculate added/removed/changed line counts
        var changes = result.ChangeSummary;
        result.Metrics.LinesAdded = changes.Count(c => c.Type == ChangeType.Addition);
        result.Metrics.LinesRemoved = changes.Count(c => c.Type == ChangeType.Deletion);
        result.Metrics.LinesChanged = changes.Count(c => c.Type == ChangeType.Modification);
    }
}

/// <summary>
/// Represents the expected outcome of a file edit operation for validation.
/// </summary>
public class EditExpectation
{
    /// <summary>
    /// Types of operations that are allowed for this edit.
    /// </summary>
    public HashSet<ChangeType>? AllowedOperations { get; set; }

    /// <summary>
    /// Line range where changes are expected (inclusive).
    /// </summary>
    public (int Start, int End)? TargetLineRange { get; set; }

    /// <summary>
    /// Specific changes that must be present in the result.
    /// </summary>
    public List<Change>? RequiredChanges { get; set; }

    /// <summary>
    /// Whether encoding must be preserved exactly.
    /// </summary>
    public bool RequireEncodingPreservation { get; set; } = true;

    /// <summary>
    /// Whether line endings must be preserved exactly.
    /// </summary>
    public bool RequireLineEndingPreservation { get; set; } = true;
}

/// <summary>
/// Results of diff validation with detailed violation information.
/// </summary>
public class DiffValidationResult
{
    public string OriginalFile { get; set; } = "";
    public string ModifiedFile { get; set; } = "";
    public EditExpectation? Expectation { get; set; }
    public bool IsValid { get; set; }
    public List<DiffViolation> Violations { get; set; } = new();
    public List<Change> ChangeSummary { get; set; } = new();
    public DiffMetrics? Metrics { get; set; }
}

/// <summary>
/// Represents a violation of diff validation expectations.
/// </summary>
public class DiffViolation
{
    public DiffViolationType Type { get; set; }
    public string Message { get; set; } = "";
    public string Context { get; set; } = "";
}

/// <summary>
/// Types of diff validation violations.
/// </summary>
public enum DiffViolationType
{
    UnexpectedChange,
    MissingExpectedChange,
    EncodingMismatch,
    LineEndingMismatch,
    ValidationError
}

/// <summary>
/// Represents a change detected in the diff analysis.
/// </summary>
public class Change
{
    public ChangeType Type { get; set; }
    public int LineNumber { get; set; }
    public string Content { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>
/// Types of changes that can be detected.
/// </summary>
public enum ChangeType
{
    Addition,
    Deletion,
    Modification
}

/// <summary>
/// Metrics calculated from diff analysis.
/// </summary>
public class DiffMetrics
{
    public int OriginalLineCount { get; set; }
    public int ModifiedLineCount { get; set; }
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }
    public int LinesChanged { get; set; }
    public int NetLineChange { get; set; }
}

/// <summary>
/// Internal file data structure for diff analysis.
/// </summary>
internal class FileData
{
    public string FilePath { get; set; } = "";
    public string Content { get; set; } = "";
    public string[] Lines { get; set; } = Array.Empty<string>();
    public Encoding Encoding { get; set; } = Encoding.UTF8;
    public string LineEnding { get; set; } = Environment.NewLine;
    public long SizeBytes { get; set; }
}