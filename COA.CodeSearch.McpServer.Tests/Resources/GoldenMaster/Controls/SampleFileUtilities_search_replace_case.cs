using System.Text;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.TestSample;

/// <summary>
/// Sample utilities for testing editing tools with real C# code.
/// Based on FileLineUtilities but with different namespace to avoid conflicts.
/// </summary>
public static class SampleFileUtilities
{
    /// <summary>
    /// Reads file with encoding detection and consistent line splitting.
    /// </summary>
    /// <param name="filePath">Path to file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of lines array and detected encoding</returns>
    public static async Task<(String[] lines, Encoding encoding)> ReadFileWithEncodingAsync(
        String filePath, CancellationToken cancellationToken = default)
    {
        // Read raw bytes and detect encoding
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var encoding = DetectEncoding(bytes);
        
        // Convert to String and split lines consistently
        var content = encoding.GetString(bytes);
        var lines = SplitLines(content);
        
        return (lines, encoding);
    }
    
    /// <summary>
    /// Splits content into lines using consistent logic across all tools.
    /// Handles mixed line endings and removes trailing empty lines consistently.
    /// </summary>
    /// <param name="content">File content String</param>
    /// <returns>Array of lines with consistent empty line handling</returns>
    public static String[] SplitLines(String content)
    {
        if (String.IsNullOrEmpty(content))
            return Array.Empty<String>();
            
        // Split on all common line ending types
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        // CRITICAL: Consistent empty line removal logic
        // Remove trailing empty line only if it exists (artifact of String splitting)
        if (lines.Length > 0 && String.IsNullOrEmpty(lines[^1]))
        {
            // Use Array.Resize for consistent behavior across all tools
            Array.Resize(ref lines, lines.Length - 1);
        }
        
        return lines;
    }
    
    /// <summary>
    /// Detects file encoding from byte order mark (BOM) or defaults to UTF-8.
    /// </summary>
    /// <param name="bytes">File bytes</param>
    /// <returns>Detected encoding</returns>
    public static Encoding DetectEncoding(byte[] bytes)
    {
        // Check for BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(true); // UTF-8 WITH BOM (preserve original BOM)
        
        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode; // UTF-16 LE
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode; // UTF-16 BE
        }
        
        // Default to UTF-8 without BOM (preserve original lack of BOM)
        return new UTF8Encoding(false);
    }
    
    /// <summary>
    /// Extracts indentation (leading whitespace) from a line.
    /// </summary>
    /// <param name="line">Source line</param>
    /// <returns>Leading whitespace String</returns>
    public static String ExtractIndentation(String line)
    {
        if (String.IsNullOrEmpty(line))
            return "";
            
        var match = Regex.Match(line, @"^(\s*)");
        return match.Groups[1].Value;
    }
    
    /// <summary>
    /// Applies consistent indentation to content lines.
    /// </summary>
    /// <param name="contentLines">Lines to indent</param>
    /// <param name="indentation">Indentation String</param>
    /// <returns>Indented lines</returns>
        public static String[] ApplyIndentation(String[] contentLines, String indentation)
        {
            if (String.IsNullOrEmpty(indentation) || contentLines.Length == 0)
                return contentLines;
            
            // Detect the minimum indentation to preserve relative structure
            var minIndentation = DetectMinimumIndentation(contentLines);
            
            var result = new String[contentLines.Length];
            for (int i = 0; i < contentLines.Length; i++)
            {
                // Only add indentation to non-empty lines
                if (String.IsNullOrEmpty(contentLines[i]))
                {
                    result[i] = contentLines[i];
                }
                else
                {
                    // Preserve relative indentation by removing only the common base indentation
                    var relativeContent = RemoveCommonIndentation(contentLines[i], minIndentation);
                    
                    // Apply the target indentation while preserving relative structure
                    result[i] = indentation + relativeContent;
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Detects the minimum indentation level across all non-empty lines.
        /// </summary>
        private static String DetectMinimumIndentation(String[] lines)
        {
            String? minIndent = null;
            
            foreach (var line in lines)
            {
                if (String.IsNullOrWhiteSpace(line))
                    continue;
                    
                var indent = ExtractIndentation(line);
                if (minIndent == null || indent.Length < minIndent.Length)
                    minIndent = indent;
            }
            
            return minIndent ?? "";
        }
        
        /// <summary>
        /// Removes the common base indentation while preserving relative indentation.
        /// </summary>
        private static String RemoveCommonIndentation(String line, String commonIndent)
        {
            if (String.IsNullOrEmpty(commonIndent) || String.IsNullOrEmpty(line))
                return line;
                
            if (line.StartsWith(commonIndent))
                return line.Substring(commonIndent.Length);
                
            return line;
        }
        
        /// <summary>
    /// Writes lines to file with original encoding, ensuring consistent line ending handling.
    /// </summary>
    /// <param name="filePath">Target file path</param>
    /// <param name="lines">Lines to write</param>
    /// <param name="encoding">File encoding to preserve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task WriteAllLinesAsync(String filePath, String[] lines, 
        Encoding encoding, CancellationToken cancellationToken = default)
    {
        await File.WriteAllLinesAsync(filePath, lines, encoding, cancellationToken);
    }
    
    /// <summary>
    /// Writes lines to file preserving original line endings by detecting them from the original content.
    /// This prevents the system default line endings from being imposed and avoids double newlines.
    /// </summary>
    /// <param name="filePath">Target file path</param>
    /// <param name="lines">Lines to write</param>
    /// <param name="encoding">File encoding to preserve</param>
    /// <param name="originalContent">Original file content to detect line endings from</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task WriteAllLinesPreservingEndingsAsync(String filePath, String[] lines, 
        Encoding encoding, String originalContent, CancellationToken cancellationToken = default)
    {
        // Handle empty lines array
        if (lines == null || lines.Length == 0)
        {
            await File.WriteAllTextAsync(filePath, "", encoding, cancellationToken);
            return;
        }
        
        // Detect line endings from original content
        var lineEnding = DetectLineEnding(originalContent);
        
        // Preserve whether the original file ended with a newline
        bool originalEndsWithNewline = !String.IsNullOrEmpty(originalContent) && 
            (originalContent.EndsWith("\n") || originalContent.EndsWith("\r\n") || originalContent.EndsWith("\r"));
        
        // Build content carefully to avoid double newlines
        var contentBuilder = new StringBuilder();
        
        // Add all lines except the last one with line endings
        for (int i = 0; i < lines.Length - 1; i++)
        {
            contentBuilder.Append(lines[i]);
            contentBuilder.Append(lineEnding);
        }
        
        // Add the last line
        contentBuilder.Append(lines[^1]);
        
        // Only add final newline if original file had one
        if (originalEndsWithNewline)
        {
            contentBuilder.Append(lineEnding);
        }
        
        // Write as raw bytes to preserve exact encoding and line endings (prevents BOM issues)
        var contentBytes = encoding.GetBytes(contentBuilder.ToString());
        await File.WriteAllBytesAsync(filePath, contentBytes, cancellationToken);
    }
    
    /// <summary>
    /// Detects the primary line ending used in content.
    /// </summary>
    /// <param name="content">File content to analyze</param>
    /// <returns>Detected line ending</returns>
    private static String DetectLineEnding(String content)
    {
        if (String.IsNullOrEmpty(content))
            return Environment.NewLine;
            
        // Count occurrences of different line endings
        var crlfCount = content.Split(new[] { "\r\n" }, StringSplitOptions.None).Length - 1;
        var lfCount = content.Split(new[] { "\n" }, StringSplitOptions.None).Length - 1 - crlfCount; // Subtract CRLF occurrences
        var crCount = content.Split(new[] { "\r" }, StringSplitOptions.None).Length - 1 - crlfCount; // Subtract CRLF occurrences
        
        // Return most common line ending
        if (crlfCount > lfCount && crlfCount > crCount)
            return "\r\n";
        if (crCount > lfCount)
            return "\r";
        if (lfCount > 0)
            return "\n";
            
        // Default to system line ending if no line endings found
        return Environment.NewLine;
    }
    
    /// <summary>
    /// Detects the appropriate indentation for inserting content at a specific position.
    /// Uses consistency analysis to choose between target line and surrounding context.
    /// </summary>
    /// <param name="lines">All file lines</param>
    /// <param name="targetLineIndex">0-based index of the line where content will be inserted</param>
    /// <param name="includeTargetLine">Whether to include the target line in analysis (true for insertion, false for replacement)</param>
    /// <returns>Detected indentation String (tabs or spaces)</returns>
    public static String DetectIndentationForInsertion(String[] lines, int targetLineIndex, bool includeTargetLine = true)
    {
        if (lines == null || lines.Length == 0 || targetLineIndex < 0)
            return "";

        // Analyze indentation patterns in the surrounding context
        var stats = AnalyzeIndentationConsistency(lines, targetLineIndex, contextRadius: 3, includeTargetLine);
        
        // Get target line indentation for comparison
        String targetIndentation = "";
        if (targetLineIndex < lines.Length && !String.IsNullOrWhiteSpace(lines[targetLineIndex]))
        {
            targetIndentation = ExtractIndentation(lines[targetLineIndex]);
        }

        // PRIORITY 1: Very strong surrounding consistency (80%+) with multiple examples always wins
        // This ensures consistent style in well-structured files, even for replacements
        // Require at least 2 indented lines for meaningful consistency
        if (stats.SurroundingConsistency >= 0.8f && stats.TotalLines >= 2 && !String.IsNullOrEmpty(stats.SurroundingIndentation))
        {
            return stats.SurroundingIndentation;
        }
        
        // PRIORITY 2: For replacement operations, use target line indentation when surrounding consistency is weak
        // This maintains existing style when there's no clear surrounding pattern
        if (!includeTargetLine && !String.IsNullOrEmpty(targetIndentation))
        {
            return targetIndentation;
        }
        
        // PRIORITY 3: Use target line's indentation if available (for insertion operations)
        if (!String.IsNullOrEmpty(targetIndentation))
        {
            return targetIndentation;
        }
        
        // PRIORITY 4: Medium surrounding consistency (60%+) as fallback
        if (stats.SurroundingConsistency >= 0.6f && !String.IsNullOrEmpty(stats.SurroundingIndentation))
        {
            return stats.SurroundingIndentation;
        }
        
        // FALLBACK: Most common indentation in file, but prefer tabs if tie
        if (stats.TabCount >= stats.SpaceCount && stats.TabCount > 0)
            return "\t";
        else if (stats.SpaceCount > 0)
            return "    ";
        else
            return "";
    }
    
    /// <summary>
    /// Analyzes indentation consistency in the vicinity of a target line.
    /// </summary>
    /// <param name="lines">All file lines</param>
    /// <param name="targetLineIndex">0-based target line index</param>
    /// <param name="contextRadius">Number of lines to analyze around target</param>
    /// <param name="includeTargetLine">Whether to include the target line in analysis</param>
    /// <returns>Indentation statistics</returns>
    private static IndentationStats AnalyzeIndentationConsistency(String[] lines, int targetLineIndex, int contextRadius, bool includeTargetLine)
    {
        var tabCount = 0;
        var spaceCount = 0;
        var totalIndentedLines = 0;
        String mostCommonIndentation = "";
        var indentationCounts = new Dictionary<String, int>();
        
        // Analyze lines in the vicinity (target line inclusion controlled by parameter)
        var startIndex = Math.Max(0, targetLineIndex - contextRadius);
        var endIndex = Math.Min(lines.Length - 1, targetLineIndex + contextRadius);
        
        for (int i = startIndex; i <= endIndex; i++)
        {
            // Skip target line if not including it (for replacement operations)
            if (!includeTargetLine && i == targetLineIndex) continue;
            
            var line = lines[i];
            if (String.IsNullOrWhiteSpace(line)) continue;
            
            var indentation = ExtractIndentation(line);
            if (String.IsNullOrEmpty(indentation)) continue;
            
            totalIndentedLines++;
            
            // Count tabs vs spaces
            if (indentation.Contains('\t'))
                tabCount++;
            else if (indentation.Contains(' '))
                spaceCount++;
                
            // Track specific indentation patterns
            indentationCounts[indentation] = indentationCounts.GetValueOrDefault(indentation, 0) + 1;
        }
        
        // Determine surrounding context consistency
        var surroundingIndentation = "";
        var surroundingConsistency = 0f;
        
        if (totalIndentedLines > 0)
        {
            // Use the most common specific indentation pattern
            var mostCommon = indentationCounts.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
            if (mostCommon.Key != null)
            {
                surroundingIndentation = mostCommon.Key;
                surroundingConsistency = (float)mostCommon.Value / totalIndentedLines;
            }
        }
        
        // Determine most common indentation style in the file
        if (tabCount > spaceCount && tabCount > 0)
            mostCommonIndentation = "\t";
        else if (spaceCount > 0)
            mostCommonIndentation = "    "; // Default to 4 spaces
        
        return new IndentationStats
        {
            SurroundingIndentation = surroundingIndentation,
            SurroundingConsistency = surroundingConsistency,
            MostCommonIndentation = mostCommonIndentation,
            TabCount = tabCount,
            SpaceCount = spaceCount,
            TotalLines = totalIndentedLines
        };
    }
    
    /// <summary>
    /// Statistics about indentation patterns in a code section.
    /// </summary>
    private record IndentationStats
    {
        public String SurroundingIndentation { get; init; } = "";
        public float SurroundingConsistency { get; init; }
        public String MostCommonIndentation { get; init; } = "";
        public int TabCount { get; init; }
        public int SpaceCount { get; init; }
        public int TotalLines { get; init; }
    }

    /// <summary>
    /// Validates file path and resolves to absolute path.
    /// </summary>
    /// <param name="filePath">Input file path</param>
    /// <returns>Resolved absolute path</returns>
    /// <exception cref="ArgumentException">If path is invalid</exception>
    /// <exception cref="FileNotFoundException">If file doesn't exist</exception>
    public static String ValidateAndResolvePath(String filePath)
    {
        if (String.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty");
        }

        // Resolve to absolute path
        var resolvedPath = Path.IsPathRooted(filePath) ? filePath : Path.GetFullPath(filePath);
        
        // Verify file exists
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"File not found: {resolvedPath}");
        }
        
        return resolvedPath;
    }
}