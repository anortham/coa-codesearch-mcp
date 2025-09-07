using System.Text;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Shared utilities for consistent file line handling across all editing tools.
/// Prevents corruption from inconsistent line splitting and manipulation logic.
/// </summary>
public static class FileLineUtilities
{
    /// <summary>
    /// Reads file with encoding detection and consistent line splitting.
    /// </summary>
    /// <param name="filePath">Path to file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of lines array and detected encoding</returns>
    public static async Task<(string[] lines, Encoding encoding)> ReadFileWithEncodingAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        // Read raw bytes and detect encoding
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var encoding = DetectEncoding(bytes);
        
        // Convert to string and split lines consistently
        var content = encoding.GetString(bytes);
        var lines = SplitLines(content);
        
        return (lines, encoding);
    }
    
    /// <summary>
    /// Splits content into lines using consistent logic across all tools.
    /// Handles mixed line endings and removes trailing empty lines consistently.
    /// </summary>
    /// <param name="content">File content string</param>
    /// <returns>Array of lines with consistent empty line handling</returns>
    public static string[] SplitLines(string content)
    {
        if (string.IsNullOrEmpty(content))
            return Array.Empty<string>();
            
        // Split on all common line ending types
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        // CRITICAL: Consistent empty line removal logic
        // Remove trailing empty line only if it exists (artifact of string splitting)
        if (lines.Length > 0 && string.IsNullOrEmpty(lines[^1]))
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
            return Encoding.UTF8;
        
        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode; // UTF-16 LE
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode; // UTF-16 BE
        }
        
        // Default to UTF-8 without BOM
        return new UTF8Encoding(false);
    }
    
    /// <summary>
    /// Extracts indentation (leading whitespace) from a line.
    /// </summary>
    /// <param name="line">Source line</param>
    /// <returns>Leading whitespace string</returns>
    public static string ExtractIndentation(string line)
    {
        if (string.IsNullOrEmpty(line))
            return "";
            
        var match = Regex.Match(line, @"^(\s*)");
        return match.Groups[1].Value;
    }
    
    /// <summary>
    /// Applies consistent indentation to content lines.
    /// </summary>
    /// <param name="contentLines">Lines to indent</param>
    /// <param name="indentation">Indentation string</param>
    /// <returns>Indented lines</returns>
    public static string[] ApplyIndentation(string[] contentLines, string indentation)
    {
        if (string.IsNullOrEmpty(indentation) || contentLines.Length == 0)
            return contentLines;
        
        var result = new string[contentLines.Length];
        for (int i = 0; i < contentLines.Length; i++)
        {
            // Only add indentation to non-empty lines
            if (string.IsNullOrEmpty(contentLines[i]))
            {
                result[i] = contentLines[i];
            }
            else
            {
                result[i] = indentation + contentLines[i];
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Writes lines to file with original encoding, ensuring consistent line ending handling.
    /// </summary>
    /// <param name="filePath">Target file path</param>
    /// <param name="lines">Lines to write</param>
    /// <param name="encoding">File encoding to preserve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task WriteAllLinesAsync(string filePath, string[] lines, 
        Encoding encoding, CancellationToken cancellationToken = default)
    {
        await File.WriteAllLinesAsync(filePath, lines, encoding, cancellationToken);
    }
    
    /// <summary>
    /// Validates file path and resolves to absolute path.
    /// </summary>
    /// <param name="filePath">Input file path</param>
    /// <returns>Resolved absolute path</returns>
    /// <exception cref="ArgumentException">If path is invalid</exception>
    /// <exception cref="FileNotFoundException">If file doesn't exist</exception>
    public static string ValidateAndResolvePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
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