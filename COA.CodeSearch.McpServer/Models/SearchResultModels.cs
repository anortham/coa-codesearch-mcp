using System;
using System.Collections.Generic;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// File search result for response building
/// </summary>
public class FileSearchResult
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public float Score { get; set; }
    // Alias for compatibility
    public string Path => FilePath;
}

/// <summary>
/// Text search result for response building
/// </summary>
public class TextSearchResult
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public float Score { get; set; }
    public List<TextSearchContextLine>? Context { get; set; }
}

/// <summary>
/// Context line for text search results
/// </summary>
public class TextSearchContextLine
{
    public int LineNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsMatch { get; set; }
}

/// <summary>
/// Hotspot information for text search
/// </summary>
public class TextSearchHotspot
{
    public string File { get; set; } = string.Empty;
    public int Matches { get; set; }
    public int Lines { get; set; }
}

/// <summary>
/// Directory search result
/// </summary>
public class DirectorySearchResult
{
    public string DirectoryName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public float Score { get; set; }
}

/// <summary>
/// Similar file result
/// </summary>
public class SimilarFileResult
{
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public float Score { get; set; }
    public int MatchingTerms { get; set; }
}

/// <summary>
/// Recent file result
/// </summary>
public class RecentFileResult
{
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public long FileSize { get; set; }
}

/// <summary>
/// File size analysis result
/// </summary>
public class FileSizeResult
{
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long FileSize { get; set; }
}

/// <summary>
/// File size statistics
/// </summary>
public class FileSizeStatistics
{
    public int FileCount { get; set; }
    public long TotalSize { get; set; }
    public long MinSize { get; set; }
    public long MaxSize { get; set; }
    public double AverageSize { get; set; }
    public double MedianSize { get; set; }
    public double StandardDeviation { get; set; }
    public Dictionary<string, int> SizeDistribution { get; set; } = new();
}

/// <summary>
/// Batch operation specification
/// </summary>
public class BatchOperationSpec
{
    public string Operation { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// Batch operation entry for responses
/// </summary>
public class BatchOperationEntry
{
    public int Index { get; set; }
    public bool Success { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }
    public double? Duration { get; set; }
    public string? OperationType { get; set; }
}

/// <summary>
/// Batch operation request
/// </summary>
public class BatchOperationRequest
{
    public List<BatchOperationDefinition> Operations { get; set; } = new();
    public string? WorkspacePath { get; set; }
    public string? ResponseMode { get; set; }
    public string? DefaultWorkspacePath { get; set; }
}

/// <summary>
/// Batch operation definition
/// </summary>
public class BatchOperationDefinition
{
    public string Operation { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public string? OperationType { get; set; }
}

/// <summary>
/// Batch operation result
/// </summary>
public class BatchOperationResult
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? Error { get; set; }
    public double Duration { get; set; }
    public List<BatchOperationEntry>? Operations { get; set; }
    public double TotalExecutionTime { get; set; }
}

/// <summary>
/// Settings for similarity analysis
/// </summary>
public class SimilaritySettings
{
    public int MinDocFreq { get; set; } = 2;
    public int MinTermFreq { get; set; } = 2;
    public int MinWordLength { get; set; } = 4;
    public int MaxWordLength { get; set; } = 30;
}