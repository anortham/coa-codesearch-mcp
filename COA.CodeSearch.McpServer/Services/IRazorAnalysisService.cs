using Microsoft.CodeAnalysis;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service interface for analyzing Blazor (.razor) files using the Razor Language Server
/// Provides LSP-like functionality for Blazor development
/// </summary>
public interface IRazorAnalysisService : IDisposable
{
    /// <summary>
    /// Gets whether the Razor Language Server is available and running
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Initializes the Razor Language Server connection
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if initialization succeeded</returns>
    Task<bool> InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Navigate to definition in Blazor files (.razor)
    /// </summary>
    /// <param name="filePath">Path to .razor file</param>
    /// <param name="line">Line number (1-based)</param>
    /// <param name="column">Column number (1-based)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Definition location or null if not found</returns>
    Task<Location?> GetDefinitionAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all references in Blazor files (.razor)
    /// </summary>
    /// <param name="filePath">Path to .razor file</param>
    /// <param name="line">Line number (1-based)</param>
    /// <param name="column">Column number (1-based)</param>
    /// <param name="includeDeclaration">Include declaration in results</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of reference locations</returns>
    Task<Location[]> FindReferencesAsync(string filePath, int line, int column, bool includeDeclaration = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get hover information in Blazor files (.razor)
    /// </summary>
    /// <param name="filePath">Path to .razor file</param>
    /// <param name="line">Line number (1-based)</param>
    /// <param name="column">Column number (1-based)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hover information or null if not available</returns>
    Task<string?> GetHoverInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rename symbol in Blazor files (.razor)
    /// </summary>
    /// <param name="filePath">Path to .razor file</param>
    /// <param name="line">Line number (1-based)</param>
    /// <param name="column">Column number (1-based)</param>
    /// <param name="newName">New name for the symbol</param>
    /// <param name="preview">Preview changes without applying them</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Workspace edit information or null if rename not possible</returns>
    Task<object?> RenameSymbolAsync(string filePath, int line, int column, string newName, bool preview = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get document symbols (outline) for Blazor files (.razor)
    /// </summary>
    /// <param name="filePath">Path to .razor file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of document symbols</returns>
    Task<object[]> GetDocumentSymbolsAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get diagnostics (errors, warnings) for Blazor files (.razor)
    /// </summary>
    /// <param name="filePath">Path to .razor file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of diagnostics</returns>
    Task<object[]> GetDiagnosticsAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get code actions available at a specific location in Blazor files (.razor)
    /// </summary>
    /// <param name="filePath">Path to .razor file</param>
    /// <param name="line">Line number (1-based)</param>
    /// <param name="column">Column number (1-based)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of available code actions</returns>
    Task<object[]> GetCodeActionsAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get completion items at a specific location in Blazor files (.razor)
    /// </summary>
    /// <param name="filePath">Path to .razor file</param>
    /// <param name="line">Line number (1-based)</param>
    /// <param name="column">Column number (1-based)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of completion items</returns>
    Task<object[]> GetCompletionAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get signature help at a specific location in Blazor files (.razor)
    /// </summary>
    /// <param name="filePath">Path to .razor file</param>
    /// <param name="line">Line number (1-based)</param>
    /// <param name="column">Column number (1-based)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Signature help information</returns>
    Task<object?> GetSignatureHelpAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces a refresh of diagnostics for a specific file
    /// </summary>
    /// <param name="filePath">Path to .razor file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task</returns>
    Task RefreshDiagnosticsAsync(string filePath, CancellationToken cancellationToken = default);
}