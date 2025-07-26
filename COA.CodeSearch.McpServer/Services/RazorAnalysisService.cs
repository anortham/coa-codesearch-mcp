using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Main implementation of IRazorAnalysisService
/// Coordinates between LSP client, virtual document manager, and position mapper
/// </summary>
public class RazorAnalysisService : IRazorAnalysisService
{
    private readonly ILogger<RazorAnalysisService> _logger;
    private readonly RazorLspClient _lspClient;
    private readonly RazorVirtualDocumentManager _virtualDocumentManager;
    private readonly RazorPositionMapper _positionMapper;
    private readonly HashSet<string> _openDocuments = new();
    private readonly object _lock = new();
    private bool _disposed;

    public RazorAnalysisService(
        ILogger<RazorAnalysisService> logger,
        RazorLspClient lspClient,
        RazorVirtualDocumentManager virtualDocumentManager,
        RazorPositionMapper positionMapper)
    {
        _logger = logger;
        _lspClient = lspClient;
        _virtualDocumentManager = virtualDocumentManager;
        _positionMapper = positionMapper;
    }

    /// <summary>
    /// Gets whether the Razor Language Server is available and running
    /// </summary>
    public bool IsAvailable => _lspClient.IsAvailable;

    /// <summary>
    /// Initializes the Razor Language Server connection
    /// </summary>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return false;
        }

        try
        {
            _logger.LogInformation("Initializing Razor Analysis Service...");
            
            var initialized = await _lspClient.InitializeAsync(cancellationToken);
            if (initialized)
            {
                _logger.LogInformation("Razor Analysis Service initialized successfully");
            }
            else
            {
                _logger.LogWarning("Failed to initialize Razor Analysis Service");
            }
            
            return initialized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing Razor Analysis Service");
            return false;
        }
    }

    /// <summary>
    /// Navigate to definition in Blazor files (.razor)
    /// </summary>
    public async Task<Location?> GetDefinitionAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        if (_disposed || !IsAvailable)
        {
            return null;
        }

        try
        {
            if (!IsRazorFile(filePath))
            {
                _logger.LogWarning("GetDefinitionAsync called on non-Razor file: {Path}", filePath);
                return null;
            }

            // Ensure document is open in LSP server
            await EnsureDocumentOpenAsync(filePath, cancellationToken);

            // Check language context
            var context = await _positionMapper.GetLanguageContextAsync(filePath, line, column);
            if (!context.SupportsGoToDefinition)
            {
                _logger.LogDebug("Position {Line}:{Column} in {Path} does not support go-to-definition (not in C# code)", 
                    line, column, filePath);
                return null;
            }

            // Send LSP request
            var lspResponse = await _lspClient.GetDefinitionAsync(filePath, line, column, cancellationToken);
            if (lspResponse == null)
            {
                return null;
            }

            // Handle both single location and array responses
            var locations = await ParseLspDefinitionResponseAsync(lspResponse, filePath);
            return locations.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting definition for {Path} at {Line}:{Column}", filePath, line, column);
            return null;
        }
    }

    /// <summary>
    /// Find all references in Blazor files (.razor)
    /// </summary>
    public async Task<Location[]> FindReferencesAsync(string filePath, int line, int column, bool includeDeclaration = true, CancellationToken cancellationToken = default)
    {
        if (_disposed || !IsAvailable)
        {
            return Array.Empty<Location>();
        }

        try
        {
            if (!IsRazorFile(filePath))
            {
                _logger.LogWarning("FindReferencesAsync called on non-Razor file: {Path}", filePath);
                return Array.Empty<Location>();
            }

            // Ensure document is open in LSP server
            await EnsureDocumentOpenAsync(filePath, cancellationToken);

            // Check language context
            var context = await _positionMapper.GetLanguageContextAsync(filePath, line, column);
            if (!context.SupportsFindReferences)
            {
                _logger.LogDebug("Position {Line}:{Column} in {Path} does not support find references (not in C# code)", 
                    line, column, filePath);
                return Array.Empty<Location>();
            }

            // Send LSP request
            var lspResponse = await _lspClient.FindReferencesAsync(filePath, line, column, includeDeclaration, cancellationToken);
            if (lspResponse == null)
            {
                return Array.Empty<Location>();
            }

            // Parse response
            var locations = await ParseLspReferencesResponseAsync(lspResponse, filePath);
            return locations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding references for {Path} at {Line}:{Column}", filePath, line, column);
            return Array.Empty<Location>();
        }
    }

    /// <summary>
    /// Get hover information in Blazor files (.razor)
    /// </summary>
    public async Task<string?> GetHoverInfoAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return null;
        }

        try
        {
            if (!IsRazorFile(filePath))
            {
                _logger.LogWarning("GetHoverInfoAsync called on non-Razor file: {Path}", filePath);
                return null;
            }

            // Use embedded analyzer when in embedded mode or LSP server not available
            if (!IsAvailable || _lspClient.IsEmbeddedMode)
            {
                _logger.LogTrace("Using embedded Razor analyzer for hover info: {FilePath} at {Line}:{Column}", filePath, line, column);
                var embeddedResult = await _lspClient.GetEmbeddedHoverInfoAsync(filePath, line, column);
                if (embeddedResult != null)
                {
                    return embeddedResult.ToJsonString();
                }
                return null;
            }

            // Ensure document is open in LSP server
            await EnsureDocumentOpenAsync(filePath, cancellationToken);

            // Send LSP request
            var lspResponse = await _lspClient.GetHoverInfoAsync(filePath, line, column, cancellationToken);
            if (lspResponse == null)
            {
                return null;
            }

            // Extract hover content
            return ParseLspHoverResponse(lspResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting hover info for {Path} at {Line}:{Column}", filePath, line, column);
            return null;
        }
    }

    /// <summary>
    /// Rename symbol in Blazor files (.razor)
    /// </summary>
    public async Task<object?> RenameSymbolAsync(string filePath, int line, int column, string newName, bool preview = true, CancellationToken cancellationToken = default)
    {
        if (_disposed || !IsAvailable)
        {
            return null;
        }

        try
        {
            if (!IsRazorFile(filePath))
            {
                _logger.LogWarning("RenameSymbolAsync called on non-Razor file: {Path}", filePath);
                return null;
            }

            // Ensure document is open in LSP server
            await EnsureDocumentOpenAsync(filePath, cancellationToken);

            // Check language context
            var context = await _positionMapper.GetLanguageContextAsync(filePath, line, column);
            if (!context.SupportsRename)
            {
                _logger.LogDebug("Position {Line}:{Column} in {Path} does not support rename (not in C# code)", 
                    line, column, filePath);
                return null;
            }

            // Send LSP request
            var lspResponse = await _lspClient.RenameSymbolAsync(filePath, line, column, newName, cancellationToken);
            if (lspResponse == null)
            {
                return null;
            }

            // For now, return the raw LSP response
            // In the future, we could convert this to a more structured format
            return lspResponse.ToJsonString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renaming symbol for {Path} at {Line}:{Column}", filePath, line, column);
            return null;
        }
    }

    /// <summary>
    /// Get document symbols (outline) for Blazor files (.razor)
    /// </summary>
    public async Task<object[]> GetDocumentSymbolsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (_disposed || !IsAvailable)
        {
            return Array.Empty<object>();
        }

        try
        {
            if (!IsRazorFile(filePath))
            {
                _logger.LogWarning("GetDocumentSymbolsAsync called on non-Razor file: {Path}", filePath);
                return Array.Empty<object>();
            }

            // Ensure document is open in LSP server
            await EnsureDocumentOpenAsync(filePath, cancellationToken);

            // Send LSP request
            var lspResponse = await _lspClient.GetDocumentSymbolsAsync(filePath, cancellationToken);
            if (lspResponse == null)
            {
                return Array.Empty<object>();
            }

            // Parse symbols response
            return ParseLspDocumentSymbolsResponse(lspResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document symbols for {Path}", filePath);
            return Array.Empty<object>();
        }
    }

    /// <summary>
    /// Get diagnostics (errors, warnings) for Blazor files (.razor)
    /// </summary>
    public async Task<object[]> GetDiagnosticsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (_disposed || !IsAvailable)
        {
            return Array.Empty<object>();
        }

        try
        {
            if (!IsRazorFile(filePath))
            {
                _logger.LogWarning("GetDiagnosticsAsync called on non-Razor file: {Path}", filePath);
                return Array.Empty<object>();
            }

            // Use embedded analyzer when in embedded mode or LSP server not available
            if (!IsAvailable || _lspClient.IsEmbeddedMode)
            {
                _logger.LogTrace("Using embedded Razor analyzer for diagnostics: {FilePath}", filePath);
                var embeddedResult = await _lspClient.GetEmbeddedDiagnosticsAsync(filePath, cancellationToken);
                if (embeddedResult != null && embeddedResult.GetValueKind() == System.Text.Json.JsonValueKind.Array)
                {
                    var resultArray = embeddedResult.AsArray();
                    return resultArray.Select(item => (object)item.ToJsonString()).ToArray();
                }
                return Array.Empty<object>();
            }

            // For diagnostics, we rely on the virtual document manager
            // to get Razor compilation diagnostics
            var virtualDoc = await _virtualDocumentManager.GetVirtualDocumentAsync(filePath);
            if (virtualDoc == null)
            {
                return Array.Empty<object>();
            }

            var diagnostics = new List<object>();
            
            // Extract diagnostics from Razor code document
            var csharpDoc = RazorCodeDocumentExtensions.GetCSharpDocument(virtualDoc.CodeDocument);
            foreach (var diagnostic in csharpDoc.Diagnostics)
            {
                diagnostics.Add(new
                {
                    severity = MapRazorSeverityToLsp(diagnostic.Severity),
                    message = diagnostic.GetMessage(),
                    source = "razor",
                    range = new
                    {
                        start = new { line = diagnostic.Span.LineIndex, character = diagnostic.Span.CharacterIndex },
                        end = new { line = diagnostic.Span.LineIndex, character = diagnostic.Span.CharacterIndex + diagnostic.Span.Length }
                    }
                });
            }

            return diagnostics.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting diagnostics for {Path}", filePath);
            return Array.Empty<object>();
        }
    }

    /// <summary>
    /// Get code actions available at a specific location in Blazor files (.razor)
    /// </summary>
    public async Task<object[]> GetCodeActionsAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        if (_disposed || !IsAvailable)
        {
            return Array.Empty<object>();
        }

        try
        {
            if (!IsRazorFile(filePath))
            {
                _logger.LogWarning("GetCodeActionsAsync called on non-Razor file: {Path}", filePath);
                return Array.Empty<object>();
            }

            // Ensure document is open in LSP server
            await EnsureDocumentOpenAsync(filePath, cancellationToken);

            // Send LSP request
            var lspResponse = await _lspClient.GetCodeActionsAsync(filePath, line, column, cancellationToken);
            if (lspResponse == null)
            {
                return Array.Empty<object>();
            }

            // Parse code actions response
            return ParseLspCodeActionsResponse(lspResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting code actions for {Path} at {Line}:{Column}", filePath, line, column);
            return Array.Empty<object>();
        }
    }

    /// <summary>
    /// Get completion items at a specific location in Blazor files (.razor)
    /// </summary>
    public async Task<object[]> GetCompletionAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        if (_disposed || !IsAvailable)
        {
            return Array.Empty<object>();
        }

        try
        {
            if (!IsRazorFile(filePath))
            {
                _logger.LogWarning("GetCompletionAsync called on non-Razor file: {Path}", filePath);
                return Array.Empty<object>();
            }

            // Ensure document is open in LSP server
            await EnsureDocumentOpenAsync(filePath, cancellationToken);

            // Send LSP request
            var lspResponse = await _lspClient.GetCompletionAsync(filePath, line, column, cancellationToken);
            if (lspResponse == null)
            {
                return Array.Empty<object>();
            }

            // Parse completion response
            return ParseLspCompletionResponse(lspResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting completion for {Path} at {Line}:{Column}", filePath, line, column);
            return Array.Empty<object>();
        }
    }

    /// <summary>
    /// Get signature help at a specific location in Blazor files (.razor)
    /// </summary>
    public async Task<object?> GetSignatureHelpAsync(string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        if (_disposed || !IsAvailable)
        {
            return null;
        }

        try
        {
            if (!IsRazorFile(filePath))
            {
                _logger.LogWarning("GetSignatureHelpAsync called on non-Razor file: {Path}", filePath);
                return null;
            }

            // Ensure document is open in LSP server
            await EnsureDocumentOpenAsync(filePath, cancellationToken);

            // Send LSP request
            var lspResponse = await _lspClient.GetSignatureHelpAsync(filePath, line, column, cancellationToken);
            if (lspResponse == null)
            {
                return null;
            }

            // Parse signature help response
            return ParseLspSignatureHelpResponse(lspResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting signature help for {Path} at {Line}:{Column}", filePath, line, column);
            return null;
        }
    }

    /// <summary>
    /// Forces a refresh of diagnostics for a specific file
    /// </summary>
    public async Task RefreshDiagnosticsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (_disposed || !IsAvailable)
        {
            return;
        }

        try
        {
            if (!IsRazorFile(filePath))
            {
                _logger.LogWarning("RefreshDiagnosticsAsync called on non-Razor file: {Path}", filePath);
                return;
            }

            // Ensure document is open in LSP server
            await EnsureDocumentOpenAsync(filePath, cancellationToken);

            // Request diagnostics refresh
            await _lspClient.RequestDiagnosticsAsync(filePath, cancellationToken);
            
            _logger.LogDebug("Requested diagnostics refresh for {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing diagnostics for {Path}", filePath);
        }
    }

    private async Task EnsureDocumentOpenAsync(string filePath, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_openDocuments.Contains(filePath))
            {
                return;
            }
        }

        try
        {
            if (File.Exists(filePath))
            {
                var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                await _lspClient.OpenDocumentAsync(filePath, content, cancellationToken);
                
                lock (_lock)
                {
                    _openDocuments.Add(filePath);
                }
                
                _logger.LogDebug("Opened document in LSP server: {Path}", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open document in LSP server: {Path}", filePath);
        }
    }

    private async Task<Location[]> ParseLspDefinitionResponseAsync(JsonNode response, string originalFilePath)
    {
        try
        {
            if (response is JsonArray array)
            {
                var locations = new List<Location>();
                foreach (var item in array)
                {
                    var location = await _positionMapper.ConvertLspLocationToRoslynAsync(item, originalFilePath);
                    if (location != null)
                    {
                        locations.Add(location);
                    }
                }
                return locations.ToArray();
            }
            else
            {
                // Single location response
                var location = await _positionMapper.ConvertLspLocationToRoslynAsync(response, originalFilePath);
                return location != null ? new[] { location } : Array.Empty<Location>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing LSP definition response");
            return Array.Empty<Location>();
        }
    }

    private async Task<Location[]> ParseLspReferencesResponseAsync(JsonNode response, string originalFilePath)
    {
        try
        {
            if (response is JsonArray array)
            {
                var locations = new List<Location>();
                foreach (var item in array)
                {
                    var location = await _positionMapper.ConvertLspLocationToRoslynAsync(item, originalFilePath);
                    if (location != null)
                    {
                        locations.Add(location);
                    }
                }
                return locations.ToArray();
            }

            return Array.Empty<Location>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing LSP references response");
            return Array.Empty<Location>();
        }
    }

    private string? ParseLspHoverResponse(JsonNode response)
    {
        try
        {
            var contents = response["contents"];
            if (contents == null)
            {
                return null;
            }

            // Handle different hover content formats
            if (contents is JsonArray contentsArray && contentsArray.Count > 0)
            {
                var first = contentsArray[0];
                if (first is JsonObject obj && obj["value"] != null)
                {
                    return obj["value"]?.GetValue<string>();
                }
                else if (first is JsonValue value)
                {
                    return value.GetValue<string>();
                }
            }
            else if (contents is JsonObject contentsObj)
            {
                if (contentsObj["value"] != null)
                {
                    return contentsObj["value"]?.GetValue<string>();
                }
            }
            else if (contents is JsonValue contentsValue)
            {
                return contentsValue.GetValue<string>();
            }

            return contents.ToJsonString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing LSP hover response");
            return null;
        }
    }

    private object[] ParseLspDocumentSymbolsResponse(JsonNode response)
    {
        try
        {
            if (response is JsonArray array)
            {
                return array.Select(item => (object)item.ToJsonString()).ToArray();
            }

            return new[] { response.ToJsonString() };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing LSP document symbols response");
            return Array.Empty<object>();
        }
    }

    private object[] ParseLspCodeActionsResponse(JsonNode response)
    {
        try
        {
            if (response is JsonArray array)
            {
                return array.Select(item => (object)item.ToJsonString()).ToArray();
            }

            return new[] { response.ToJsonString() };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing LSP code actions response");
            return Array.Empty<object>();
        }
    }

    private object[] ParseLspCompletionResponse(JsonNode response)
    {
        try
        {
            // LSP completion response can be CompletionList or CompletionItem[]
            if (response is JsonObject obj && obj["items"] != null)
            {
                // CompletionList format
                var items = obj["items"]?.AsArray();
                return items?.Select(item => (object)item.ToJsonString()).ToArray() ?? Array.Empty<object>();
            }
            else if (response is JsonArray array)
            {
                // CompletionItem[] format
                return array.Select(item => (object)item.ToJsonString()).ToArray();
            }

            return new[] { response.ToJsonString() };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing LSP completion response");
            return Array.Empty<object>();
        }
    }

    private object? ParseLspSignatureHelpResponse(JsonNode response)
    {
        try
        {
            return response.ToJsonString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing LSP signature help response");
            return null;
        }
    }

    private bool IsRazorFile(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".razor", StringComparison.OrdinalIgnoreCase);
    }

    private string MapRazorSeverityToLsp(RazorDiagnosticSeverity severity)
    {
        return severity switch
        {
            RazorDiagnosticSeverity.Error => "error",
            RazorDiagnosticSeverity.Warning => "warning",
            _ => "hint"
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            // Close all open documents
            var documentsToClose = new List<string>();
            lock (_lock)
            {
                documentsToClose.AddRange(_openDocuments);
                _openDocuments.Clear();
            }

            foreach (var filePath in documentsToClose)
            {
                try
                {
                    _lspClient.CloseDocumentAsync(filePath).Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error closing document {Path} during disposal", filePath);
                }
            }

            // Clear virtual document cache
            _virtualDocumentManager.ClearCache();

            // Dispose LSP client
            _lspClient.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during RazorAnalysisService disposal");
        }

        _logger.LogInformation("RazorAnalysisService disposed");
    }
}