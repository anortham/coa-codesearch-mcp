namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Interface for TypeScript analysis service
/// </summary>
public interface ITypeScriptAnalysisService
{
    /// <summary>
    /// Gets whether the TypeScript server is available and ready to process requests
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Get rename information for a symbol at a given position
    /// </summary>
    Task<object?> GetRenameInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find the nearest tsconfig.json by searching upward from a file path
    /// </summary>
    Task<string?> FindNearestTypeScriptProjectAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get definition location for a symbol at a given position
    /// </summary>
    Task<TypeScriptLocation?> GetDefinitionAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all references to a symbol
    /// </summary>
    Task<List<TypeScriptLocation>> FindReferencesAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get quick info (hover information) for a symbol at a given position
    /// </summary>
    Task<object?> GetQuickInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
}