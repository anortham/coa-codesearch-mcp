using System.Text;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.Synonym;
using Lucene.Net.Analysis.En;
using Lucene.Net.Analysis.Util;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Custom Lucene analyzer for memory search optimization with domain-specific synonyms,
/// stemming, and per-field analysis configuration. Replaces QueryExpansionService with
/// native Lucene synonym processing for better performance and consistency.
/// </summary>
public class MemoryAnalyzer : Analyzer
{
    private readonly ILogger<MemoryAnalyzer> _logger;
    private readonly SynonymMap _synonymMap;
    private readonly CharArraySet _stopWords;
    
    // Lucene version for consistency
    private static readonly LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;
    
    public MemoryAnalyzer(ILogger<MemoryAnalyzer> logger)
    {
        _logger = logger;
        _synonymMap = BuildSynonymMap();
        _stopWords = BuildStopWords();
        
        _logger.LogInformation("MemoryAnalyzer initialized with {SynonymCount} synonym mappings", 
            GetSynonymCount());
    }
    
    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
    {
        // Base tokenization
        var tokenizer = new StandardTokenizer(LUCENE_VERSION, reader);
        TokenStream tokenStream = tokenizer;
        
        // Lowercase filter
        tokenStream = new LowerCaseFilter(LUCENE_VERSION, tokenStream);
        
        // Stop word filter (configurable per field)
        if (ShouldUseStopWords(fieldName))
        {
            tokenStream = new StopFilter(LUCENE_VERSION, tokenStream, _stopWords);
        }
        
        // Synonym expansion (core feature replacing QueryExpansionService)
        if (ShouldUseSynonyms(fieldName))
        {
            tokenStream = new SynonymFilter(tokenStream, _synonymMap, true);
        }
        
        // Stemming for better recall (especially for code terms)
        if (ShouldUseStemming(fieldName))
        {
            tokenStream = new PorterStemFilter(tokenStream);
        }
        
        return new TokenStreamComponents(tokenizer, tokenStream);
    }
    
    /// <summary>
    /// Build synonym map from domain-specific expansions migrated from QueryExpansionService
    /// </summary>
    private SynonymMap BuildSynonymMap()
    {
        var builder = new SynonymMap.Builder(true); // dedup=true for efficiency
        
        try
        {
            // Authentication & Security synonyms
            AddSynonymGroup(builder, "auth", new[] { "authentication", "login", "signin", "jwt", "oauth", "security", "authorize", "credential" });
            AddSynonymGroup(builder, "login", new[] { "authentication", "signin", "auth", "credential", "session" });
            AddSynonymGroup(builder, "jwt", new[] { "token", "auth", "authentication", "bearer", "security" });
            AddSynonymGroup(builder, "oauth", new[] { "auth", "authentication", "security", "authorization", "token" });
            AddSynonymGroup(builder, "security", new[] { "auth", "authentication", "authorization", "permission", "access" });
            
            // Database & Data synonyms
            AddSynonymGroup(builder, "db", new[] { "database", "sql", "entity", "repository", "data", "table", "query" });
            AddSynonymGroup(builder, "database", new[] { "db", "sql", "entity", "repository", "data", "storage" });
            AddSynonymGroup(builder, "sql", new[] { "database", "query", "table", "entity", "data" });
            AddSynonymGroup(builder, "entity", new[] { "model", "data", "database", "table", "object" });
            AddSynonymGroup(builder, "repository", new[] { "data", "database", "storage", "persistence" });
            
            // API & Web synonyms
            AddSynonymGroup(builder, "api", new[] { "endpoint", "controller", "service", "http", "rest", "web" });
            AddSynonymGroup(builder, "endpoint", new[] { "api", "controller", "route", "http", "service" });
            AddSynonymGroup(builder, "controller", new[] { "api", "endpoint", "action", "mvc", "web" });
            AddSynonymGroup(builder, "service", new[] { "api", "business", "logic", "provider", "manager" });
            AddSynonymGroup(builder, "http", new[] { "web", "api", "request", "response", "client" });
            AddSynonymGroup(builder, "rest", new[] { "api", "http", "web", "service", "endpoint" });
            
            // Configuration & Settings synonyms
            AddSynonymGroup(builder, "config", new[] { "configuration", "settings", "options", "parameters", "environment" });
            AddSynonymGroup(builder, "configuration", new[] { "config", "settings", "options", "appsettings" });
            AddSynonymGroup(builder, "settings", new[] { "config", "configuration", "options", "preferences" });
            AddSynonymGroup(builder, "environment", new[] { "env", "config", "settings", "deployment" });
            
            // Testing synonyms
            AddSynonymGroup(builder, "test", new[] { "testing", "unit", "integration", "spec", "mock" });
            AddSynonymGroup(builder, "mock", new[] { "test", "fake", "stub", "testing" });
            AddSynonymGroup(builder, "unit", new[] { "test", "testing", "spec" });
            
            // Error Handling synonyms
            AddSynonymGroup(builder, "error", new[] { "exception", "bug", "issue", "problem", "failure" });
            AddSynonymGroup(builder, "exception", new[] { "error", "bug", "failure", "catch" });
            AddSynonymGroup(builder, "bug", new[] { "error", "issue", "defect", "problem" });
            
            // Performance synonyms
            AddSynonymGroup(builder, "performance", new[] { "speed", "optimization", "cache", "memory", "cpu" });
            AddSynonymGroup(builder, "cache", new[] { "memory", "performance", "storage", "temporary" });
            AddSynonymGroup(builder, "memory", new[] { "performance", "cache", "allocation", "leak" });
            
            // Logging & Monitoring synonyms
            AddSynonymGroup(builder, "log", new[] { "logging", "trace", "debug", "monitor" });
            AddSynonymGroup(builder, "logging", new[] { "log", "trace", "debug", "audit" });
            AddSynonymGroup(builder, "monitor", new[] { "logging", "metric", "health", "status" });
            
            var synonymMap = builder.Build();
            _logger.LogDebug("Built synonym map with {GroupCount} synonym groups", GetGroupCount());
            
            return synonymMap;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build synonym map, falling back to empty map");
            return new SynonymMap.Builder(true).Build(); // Empty map as fallback
        }
    }
    
    /// <summary>
    /// Add bidirectional synonym mappings (key -> synonyms and synonyms -> key)
    /// </summary>
    private void AddSynonymGroup(SynonymMap.Builder builder, string key, string[] synonyms)
    {
        try
        {
            // Convert to CharsRef for Lucene
            var keyChars = new CharsRef(key);
            
            // Add key -> synonyms mappings
            foreach (var synonym in synonyms)
            {
                var synonymChars = new CharsRef(synonym);
                builder.Add(keyChars, synonymChars, true);
            }
            
            // Add reverse mappings (synonyms -> key)
            foreach (var synonym in synonyms)
            {
                var synonymChars = new CharsRef(synonym);
                builder.Add(synonymChars, keyChars, true);
                
                // Add synonym -> other synonyms mappings
                foreach (var otherSynonym in synonyms)
                {
                    if (synonym != otherSynonym)
                    {
                        var otherSynonymChars = new CharsRef(otherSynonym);
                        builder.Add(synonymChars, otherSynonymChars, true);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add synonym group for key: {Key}", key);
        }
    }
    
    /// <summary>
    /// Build stop words set optimized for code and technical content
    /// </summary>
    private CharArraySet BuildStopWords()
    {
        // Start with standard English stop words but remove some useful for code
        var stopWords = new CharArraySet(LUCENE_VERSION, 50, true);
        
        // Common English stop words that are less useful in technical search
        var technicalStopWords = new[]
        {
            "a", "an", "the", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by",
            "this", "that", "these", "those", "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did", "will", "would", "could", "should",
            "can", "may", "might", "must", "shall"
        };
        
        foreach (var word in technicalStopWords)
        {
            stopWords.Add(word);
        }
        
        return stopWords;
    }
    
    /// <summary>
    /// Determine if stop word filtering should be applied to this field
    /// </summary>
    private bool ShouldUseStopWords(string fieldName)
    {
        // Apply stop words to content fields but not to type/category fields
        return fieldName switch
        {
            "content" => true,
            "_all" => true,
            "description" => true,
            "notes" => true,
            "type" => false,
            "memoryType" => false,
            "category" => false,
            _ => true // Default to using stop words
        };
    }
    
    /// <summary>
    /// Determine if synonym expansion should be applied to this field
    /// </summary>
    private bool ShouldUseSynonyms(string fieldName)
    {
        // Apply synonyms to all searchable content
        return fieldName switch
        {
            "content" => true,
            "_all" => true,
            "description" => true,
            "type" => true,
            "memoryType" => true,
            "category" => true,
            "notes" => true,
            _ => true // Default to using synonyms
        };
    }
    
    /// <summary>
    /// Determine if stemming should be applied to this field
    /// </summary>
    private bool ShouldUseStemming(string fieldName)
    {
        // Apply stemming to content but be careful with exact match fields
        return fieldName switch
        {
            "content" => true,
            "_all" => true,
            "description" => true,
            "notes" => true,
            "type" => false, // Exact match preferred for types
            "memoryType" => false,
            "category" => false,
            _ => false // Conservative default for unknown fields
        };
    }
    
    /// <summary>
    /// Get estimated synonym count for logging (approximation)
    /// </summary>
    private int GetSynonymCount()
    {
        // Rough estimate: 13 groups * average 6 terms * bidirectional mappings
        return 13 * 6 * 2;
    }
    
    /// <summary>
    /// Get synonym group count for logging
    /// </summary>
    private int GetGroupCount()
    {
        return 13; // Auth, DB, API, Config, Test, Error, Performance, Logging = 13 groups
    }
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logger.LogDebug("MemoryAnalyzer disposed");
        }
        base.Dispose(disposing);
    }
}