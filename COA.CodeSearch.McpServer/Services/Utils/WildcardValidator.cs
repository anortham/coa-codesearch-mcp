using System;

namespace COA.CodeSearch.McpServer.Services.Utils;

/// <summary>
/// Utility class for validating wildcard queries to prevent Lucene parsing errors
/// </summary>
public static class WildcardValidator
{
    /// <summary>
    /// Check if a query contains invalid wildcard patterns that would cause Lucene parsing errors
    /// </summary>
    /// <param name="query">The query string to validate</param>
    /// <returns>True if the query contains invalid wildcard patterns, false otherwise</returns>
    public static bool IsInvalidWildcardQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false; // Let other validation handle empty queries
        }

        var trimmedQuery = query.Trim();

        // Leading wildcards are not allowed in standard Lucene queries
        if (trimmedQuery.StartsWith("*") || trimmedQuery.StartsWith("?"))
        {
            return true;
        }
        
        // Pure wildcard queries are not useful for highlighting
        if (trimmedQuery == "*" || trimmedQuery == "?" || 
            string.IsNullOrWhiteSpace(trimmedQuery.Replace("*", "").Replace("?", "")))
        {
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Sanitize a query by removing or fixing common wildcard issues
    /// </summary>
    /// <param name="query">The query to sanitize</param>
    /// <returns>A sanitized version of the query, or null if the query cannot be fixed</returns>
    public static string? SanitizeWildcardQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return query;
        }

        var trimmedQuery = query.Trim();

        // Remove leading wildcards by trimming them
        while (trimmedQuery.StartsWith("*") || trimmedQuery.StartsWith("?"))
        {
            trimmedQuery = trimmedQuery.Substring(1).Trim();
        }

        // If nothing remains after removing wildcards, return null
        if (string.IsNullOrWhiteSpace(trimmedQuery) || 
            string.IsNullOrWhiteSpace(trimmedQuery.Replace("*", "").Replace("?", "")))
        {
            return null;
        }

        return trimmedQuery;
    }

    /// <summary>
    /// Check if a query has safe wildcard patterns (trailing wildcards are generally okay)
    /// </summary>
    /// <param name="query">The query to check</param>
    /// <returns>True if the query has safe wildcard usage</returns>
    public static bool HasSafeWildcardUsage(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var trimmedQuery = query.Trim();

        // Empty query is safe
        if (string.IsNullOrEmpty(trimmedQuery))
        {
            return true;
        }

        // Leading wildcards are unsafe
        if (trimmedQuery.StartsWith("*") || trimmedQuery.StartsWith("?"))
        {
            return false;
        }

        // Pure wildcards are unsafe
        if (trimmedQuery == "*" || trimmedQuery == "?" ||
            string.IsNullOrWhiteSpace(trimmedQuery.Replace("*", "").Replace("?", "")))
        {
            return false;
        }

        // Trailing wildcards and embedded wildcards are generally safe
        return true;
    }
}