namespace COA.CodeSearch.Contracts;

/// <summary>
/// Standard error response type for consistent error handling across all tools and services.
/// Replaces anonymous error objects to maintain type safety.
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// The main error message
    /// </summary>
    public string error { get; set; } = string.Empty;
    
    /// <summary>
    /// Additional error details (e.g., exception message)
    /// </summary>
    public string? details { get; set; }
    
    /// <summary>
    /// Optional error code for categorization
    /// </summary>
    public string? code { get; set; }
    
    /// <summary>
    /// Optional suggestion for recovery or next steps
    /// </summary>
    public string? suggestion { get; set; }
}