using COA.CodeSearch.McpServer.Constants;
using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Provides tool discovery resources for AI agents to understand available tools,
/// their relationships, and usage analytics. This enables intelligent tool selection
/// and workflow optimization.
/// </summary>
public class ToolDiscoveryResourceProvider : IResourceProvider
{
    private readonly ILogger<ToolDiscoveryResourceProvider> _logger;
    private readonly ToolRegistry _toolRegistry;
    private readonly JsonSerializerOptions _jsonOptions;

    public string Scheme => "codesearch-tools";
    public string Name => "Tool Discovery";
    public string Description => "Discover available tools, their relationships, and usage patterns";

    public ToolDiscoveryResourceProvider(
        ILogger<ToolDiscoveryResourceProvider> logger,
        ToolRegistry toolRegistry)
    {
        _logger = logger;
        _toolRegistry = toolRegistry;
        _jsonOptions = new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<List<Resource>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Keep async for interface compliance
        
        return new List<Resource>
        {
            new Resource
            {
                Uri = "codesearch-tools://catalog/all",
                Name = "Tool Catalog",
                Description = "Complete catalog of available tools with descriptions and categories",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-tools://relationships/graph",
                Name = "Tool Relationships",
                Description = "Tool relationships and dependency graph for workflow planning",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-tools://categories/search",
                Name = "Search Tools",
                Description = "Tools for searching and discovering content",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-tools://categories/memory",
                Name = "Memory Tools", 
                Description = "Tools for storing and retrieving project knowledge",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-tools://categories/analysis",
                Name = "Analysis Tools",
                Description = "Tools for analyzing code patterns and architecture",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-tools://workflows/common",
                Name = "Common Workflows",
                Description = "Pre-defined tool sequences for common development tasks",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-tools://ai-optimized/phase3",
                Name = "AI-Optimized Tools",
                Description = "Phase 3 AI-optimized tools for advanced workflows",
                MimeType = "application/json"
            }
        };
    }

    public async Task<ReadResourceResult?> ReadResourceAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(uri))
            return null;

        try
        {
            var content = uri switch
            {
                "codesearch-tools://catalog/all" => await GenerateToolCatalogAsync(),
                "codesearch-tools://relationships/graph" => await GenerateToolRelationshipsAsync(),
                "codesearch-tools://categories/search" => await GenerateSearchToolsAsync(),
                "codesearch-tools://categories/memory" => await GenerateMemoryToolsAsync(), 
                "codesearch-tools://categories/analysis" => await GenerateAnalysisToolsAsync(),
                "codesearch-tools://workflows/common" => await GenerateCommonWorkflowsAsync(),
                "codesearch-tools://ai-optimized/phase3" => await GeneratePhase3ToolsAsync(),
                _ => null
            };

            if (content == null)
                return null;

            return new ReadResourceResult
            {
                Contents = new List<ResourceContent>
                {
                    new ResourceContent
                    {
                        Uri = uri,
                        MimeType = "application/json",
                        Text = content
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read tool discovery resource: {Uri}", uri);
            return null;
        }
    }

    public bool CanHandle(string uri)
    {
        return uri.StartsWith("codesearch-tools://", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> GenerateToolCatalogAsync()
    {
        await Task.CompletedTask;
        
        var catalog = new
        {
            TotalTools = GetToolCount(),
            Categories = new[]
            {
                new { Name = "Search", Count = GetSearchToolCount(), Description = "Text, file, and directory search capabilities" },
                new { Name = "Memory", Count = GetMemoryToolCount(), Description = "Knowledge storage and retrieval" },
                new { Name = "Analysis", Count = GetAnalysisToolCount(), Description = "Code analysis and pattern detection" },
                new { Name = "System", Count = GetSystemToolCount(), Description = "Health checks and diagnostics" },
                new { Name = "Workflow", Count = GetWorkflowToolCount(), Description = "Multi-tool orchestration" }
            },
            Tools = GetAllToolsMetadata(),
            LastUpdated = DateTime.UtcNow,
            Version = "Phase 3 - AI Optimized"
        };

        return JsonSerializer.Serialize(catalog, _jsonOptions);
    }

    private async Task<string> GenerateToolRelationshipsAsync()
    {
        await Task.CompletedTask;
        
        var relationships = new
        {
            Prerequisites = new[]
            {
                new { Tool = ToolNames.TextSearch, Requires = new[] { ToolNames.IndexWorkspace }, Reason = "Requires indexed workspace for searching" },
                new { Tool = ToolNames.FileSearch, Requires = new[] { ToolNames.IndexWorkspace }, Reason = "Requires indexed workspace for searching" },
                new { Tool = ToolNames.RecentFiles, Requires = new[] { ToolNames.IndexWorkspace }, Reason = "Requires indexed workspace for file analysis" },
                new { Tool = ToolNames.SimilarFiles, Requires = new[] { ToolNames.IndexWorkspace }, Reason = "Requires indexed workspace for similarity analysis" },
                new { Tool = ToolNames.DirectorySearch, Requires = new[] { ToolNames.IndexWorkspace }, Reason = "Requires indexed workspace for directory searching" },
                new { Tool = ToolNames.FileSizeAnalysis, Requires = new[] { ToolNames.IndexWorkspace }, Reason = "Requires indexed workspace for file analysis" }
            },
            Complementary = new[]
            {
                new { Primary = ToolNames.SearchAssistant, Complements = new[] { ToolNames.TextSearch, ToolNames.FileSearch }, Reason = "Orchestrates multiple search operations" },
                new { Primary = ToolNames.PatternDetector, Complements = new[] { ToolNames.TextSearch, ToolNames.FileSizeAnalysis }, Reason = "Uses search tools for pattern analysis" },
                new { Primary = ToolNames.MemoryGraphNavigator, Complements = new[] { ToolNames.SearchMemories, ToolNames.GetRelatedMemories }, Reason = "Visualizes memory relationships" },
                new { Primary = ToolNames.StoreMemory, Complements = new[] { ToolNames.LinkMemories }, Reason = "Memory storage followed by relationship creation" }
            },
            WorkflowChains = new[]
            {
                new 
                { 
                    Name = "Project Analysis",
                    Steps = new[] { ToolNames.IndexWorkspace, ToolNames.PatternDetector, ToolNames.StoreMemory },
                    Description = "Comprehensive project analysis and knowledge capture"
                },
                new 
                { 
                    Name = "Code Discovery", 
                    Steps = new[] { ToolNames.IndexWorkspace, ToolNames.SearchAssistant, ToolNames.RecallContext },
                    Description = "Intelligent code discovery with context integration"
                },
                new 
                { 
                    Name = "Knowledge Exploration",
                    Steps = new[] { ToolNames.SearchMemories, ToolNames.MemoryGraphNavigator, ToolNames.GetRelatedMemories },
                    Description = "Deep exploration of project knowledge and relationships"
                }
            },
            ConflictAvoidance = new[]
            {
                new { Tools = new[] { ToolNames.IndexWorkspace }, Reason = "Only one indexing operation should run at a time", Severity = "High" },
                new { Tools = new[] { ToolNames.BackupMemories, ToolNames.RestoreMemories }, Reason = "Avoid concurrent backup/restore operations", Severity = "Medium" }
            }
        };

        return JsonSerializer.Serialize(relationships, _jsonOptions);
    }

    private async Task<string> GenerateSearchToolsAsync()
    {
        await Task.CompletedTask;
        
        var searchTools = new
        {
            Category = "Search",
            Description = "Tools for discovering and searching content within codebases",
            Tools = new object[]
            {
                CreateToolInfo(ToolNames.IndexWorkspace, "Foundation tool - must be run first", "High", new[] { "Prerequisites" }),
                CreateToolInfo(ToolNames.TextSearch, "Primary content search capability", "High", new[] { "Content", "Patterns" }),
                CreateToolInfo(ToolNames.FileSearch, "Locate files by name patterns", "High", new[] { "Navigation", "Discovery" }),
                CreateToolInfo(ToolNames.SearchAssistant, "AI-guided multi-step search orchestration", "Very High", new[] { "AI-Optimized", "Workflow" }),
                CreateToolInfo(ToolNames.RecentFiles, "Find recently modified files", "Medium", new[] { "Recent", "Changes" }),
                CreateToolInfo(ToolNames.SimilarFiles, "Discover related content", "Medium", new[] { "Similarity", "Patterns" }),
                CreateToolInfo(ToolNames.DirectorySearch, "Navigate project structure", "Medium", new[] { "Structure", "Navigation" }),
                CreateToolInfo(ToolNames.BatchOperations, "Execute multiple searches efficiently", "High", new[] { "Performance", "Batch" })
            },
            BestPractices = new[]
            {
                "Always run index_workspace before using search tools",
                "Use search_assistant for complex, multi-step discovery tasks",
                "Combine text_search with file_search for comprehensive results",
                "Use batch_operations when you need multiple search types"
            }
        };

        return JsonSerializer.Serialize(searchTools, _jsonOptions);
    }

    private async Task<string> GenerateMemoryToolsAsync()
    {
        await Task.CompletedTask;
        
        var memoryTools = new
        {
            Category = "Memory",
            Description = "Tools for storing, retrieving, and managing project knowledge",
            Tools = new object[]
            {
                CreateToolInfo(ToolNames.StoreMemory, "Store architectural decisions and insights", "High", new[] { "Storage", "Knowledge" }),
                CreateToolInfo(ToolNames.SearchMemories, "Find relevant stored knowledge", "High", new[] { "Retrieval", "Search" }),
                CreateToolInfo(ToolNames.RecallContext, "Load context from previous sessions", "Very High", new[] { "Context", "Session" }),
                CreateToolInfo(ToolNames.MemoryGraphNavigator, "Visualize knowledge relationships", "Very High", new[] { "AI-Optimized", "Relationships" }),
                CreateToolInfo(ToolNames.GetMemory, "Retrieve specific memory by ID", "Medium", new[] { "Retrieval", "Specific" }),
                CreateToolInfo(ToolNames.UpdateMemory, "Modify existing memories", "Medium", new[] { "Modification", "Updates" }),
                CreateToolInfo(ToolNames.LinkMemories, "Create relationships between memories", "Medium", new[] { "Relationships", "Connections" }),
                CreateToolInfo(ToolNames.BackupMemories, "Export knowledge for version control", "Medium", new[] { "Backup", "Export" }),
                CreateToolInfo(ToolNames.MemoryTimeline, "View chronological memory history", "Low", new[] { "History", "Timeline" })
            },
            BestPractices = new[]
            {
                "Start sessions with recall_context to restore previous work",
                "Use memory_graph_navigator to understand knowledge structure",
                "Store important architectural decisions and patterns as memories",
                "Link related memories to build knowledge graphs",
                "Backup memories regularly for team collaboration"
            }
        };

        return JsonSerializer.Serialize(memoryTools, _jsonOptions);
    }

    private async Task<string> GenerateAnalysisToolsAsync()
    {
        await Task.CompletedTask;
        
        var analysisTools = new
        {
            Category = "Analysis", 
            Description = "Tools for analyzing code quality, patterns, and architecture",
            Tools = new object[]
            {
                CreateToolInfo(ToolNames.PatternDetector, "Detect architectural patterns and anti-patterns", "Very High", new[] { "AI-Optimized", "Quality" }),
                CreateToolInfo(ToolNames.FileSizeAnalysis, "Analyze file size distribution", "Medium", new[] { "Metrics", "Distribution" }),
                CreateToolInfo(ToolNames.IndexHealthCheck, "Check index health and performance", "Medium", new[] { "Health", "Performance" }),
                CreateToolInfo(ToolNames.SystemHealthCheck, "Comprehensive system diagnostics", "Medium", new[] { "Health", "System" })
            },
            BestPractices = new[]
            {
                "Use pattern_detector for comprehensive code quality analysis",
                "Run health checks regularly to ensure optimal performance",
                "Combine with memory tools to store analysis results",
                "Use createMemories=true in pattern_detector to capture findings"
            }
        };

        return JsonSerializer.Serialize(analysisTools, _jsonOptions);
    }

    private async Task<string> GenerateCommonWorkflowsAsync()
    {
        await Task.CompletedTask;
        
        var workflows = new
        {
            CommonWorkflows = new[]
            {
                new 
                {
                    Name = "New Project Analysis",
                    Description = "Comprehensive analysis of a new codebase",
                    Steps = new object[]
                    {
                        new { Step = 1, Tool = ToolNames.IndexWorkspace, Purpose = "Create searchable index of all files" },
                        new { Step = 2, Tool = ToolNames.PatternDetector, Purpose = "Detect patterns and potential issues", Parameters = "patternTypes: ['architecture', 'security', 'performance']" },
                        new { Step = 3, Tool = ToolNames.SearchAssistant, Purpose = "Explore key components", Parameters = "goal: 'Find main entry points and key services'" },
                        new { Step = 4, Tool = ToolNames.StoreMemory, Purpose = "Document architectural findings" }
                    },
                    EstimatedTime = "5-10 minutes",
                    OutputValue = "Comprehensive understanding of codebase architecture and potential issues"
                },
                new 
                {
                    Name = "Feature Investigation",
                    Description = "Deep dive into specific functionality or feature",
                    Steps = new object[]
                    {
                        new { Step = 1, Tool = ToolNames.RecallContext, Purpose = "Load previous knowledge about the feature" },
                        new { Step = 2, Tool = ToolNames.SearchAssistant, Purpose = "Find all related code", Parameters = "goal: 'Find all components related to [feature name]'" },
                        new { Step = 3, Tool = ToolNames.SimilarFiles, Purpose = "Discover related implementations" },
                        new { Step = 4, Tool = ToolNames.MemoryGraphNavigator, Purpose = "Map feature relationships" }
                    },
                    EstimatedTime = "3-5 minutes",
                    OutputValue = "Complete understanding of feature implementation and dependencies"
                },
                new 
                {
                    Name = "Bug Investigation",
                    Description = "Systematic approach to understanding and fixing bugs",
                    Steps = new object[]
                    {
                        new { Step = 1, Tool = ToolNames.SearchAssistant, Purpose = "Find error patterns and exception handling", Parameters = "goal: 'Find error handling and exception patterns related to [bug description]'" },
                        new { Step = 2, Tool = ToolNames.RecentFiles, Purpose = "Check recently modified files" },
                        new { Step = 3, Tool = ToolNames.PatternDetector, Purpose = "Look for anti-patterns", Parameters = "patternTypes: ['performance', 'security']" },
                        new { Step = 4, Tool = ToolNames.StoreMemory, Purpose = "Document bug findings and resolution" }
                    },
                    EstimatedTime = "2-5 minutes",
                    OutputValue = "Systematic bug analysis with documented findings"
                },
                new 
                {
                    Name = "Refactoring Preparation",
                    Description = "Analyze code before major refactoring",
                    Steps = new object[]
                    {
                        new { Step = 1, Tool = ToolNames.PatternDetector, Purpose = "Identify current patterns and anti-patterns" },
                        new { Step = 2, Tool = ToolNames.SearchAssistant, Purpose = "Find all dependencies", Parameters = "goal: 'Find all code that depends on [component to refactor]'" },
                        new { Step = 3, Tool = ToolNames.FileSizeAnalysis, Purpose = "Identify oversized components" },
                        new { Step = 4, Tool = ToolNames.StoreMemory, Purpose = "Document refactoring plan", Parameters = "memoryType: 'ArchitecturalDecision'" }
                    },
                    EstimatedTime = "5-8 minutes",
                    OutputValue = "Comprehensive refactoring plan with risk assessment"
                }
            },
            WorkflowTips = new[]
            {
                "Always start with index_workspace for new projects",
                "Use recall_context at the beginning of sessions",
                "Combine search_assistant with pattern_detector for thorough analysis",
                "Store important findings as memories for future reference",
                "Use memory_graph_navigator to understand relationships"
            },
            AntiPatterns = new[]
            {
                "❌ DON'T use Task tool for simple text searches - use text_search with contextLines instead",
                "❌ DON'T ignore summary mode insights - hotspots and actions often contain what you need",
                "❌ DON'T use Task tool for file finding - use file_search directly",
                "❌ DON'T run multiple separate searches - use batch_operations for efficiency",
                "❌ DON'T jump to full response mode immediately - summary mode provides actionable insights"
            },
            EfficientPatterns = new[]
            {
                "✅ text_search + contextLines for code snippets with surrounding context",
                "✅ batch_operations for multiple related searches in parallel", 
                "✅ Trust summary mode hotspots - they guide you to the right files",
                "✅ Use provided actions from search results for next steps",
                "✅ search_assistant for complex multi-step analysis, not simple content retrieval"
            }
        };

        return JsonSerializer.Serialize(workflows, _jsonOptions);
    }

    private async Task<string> GeneratePhase3ToolsAsync()
    {
        await Task.CompletedTask;
        
        var phase3Tools = new
        {
            Phase = "Phase 3 - AI UX Optimization",
            Description = "Advanced AI-optimized tools for intelligent development workflows",
            CompletionStatus = "Implemented ✅",
            Tools = new[]
            {
                new
                {
                    Name = ToolNames.SearchAssistant,
                    Status = "Implemented ✅",
                    Description = "Orchestrates multi-step search operations with intelligent goal analysis",
                    AiOptimizations = new[]
                    {
                        "Automatic search strategy generation based on goal analysis",
                        "Context preservation across multi-step operations", 
                        "Pattern recognition and insight generation",
                        "Suggested next actions for continued exploration"
                    },
                    UseCases = new[] { "Complex code discovery", "Pattern analysis", "Architecture understanding", "Debugging workflows" }
                },
                new
                {
                    Name = ToolNames.PatternDetector,
                    Status = "Implemented ✅", 
                    Description = "Analyzes codebase for architectural patterns, security issues, and anti-patterns",
                    AiOptimizations = new[]
                    {
                        "Intelligent pattern recognition across multiple analysis types",
                        "Confidence scoring for detected patterns",
                        "Severity-based prioritization of findings",
                        "Automatic memory creation for significant issues"
                    },
                    UseCases = new[] { "Code quality assessment", "Security audits", "Architecture reviews", "Technical debt identification" }
                },
                new
                {
                    Name = ToolNames.MemoryGraphNavigator,
                    Status = "Implemented ✅",
                    Description = "Explores memory relationships with graph visualization and clustering",
                    AiOptimizations = new[]
                    {
                        "Automatic relationship strength calculation",
                        "Intelligent clustering by themes and types",
                        "Orphan detection and graph insights",
                        "Visual graph representation for AI understanding"
                    },
                    UseCases = new[] { "Knowledge structure understanding", "Memory relationship exploration", "Knowledge gap identification" }
                }
            },
            NextPhase = new
            {
                Phase = "Phase 4 - Prompt Templates",
                Status = "Pending",
                PlannedTools = new[]
                {
                    "refactoring-assistant prompt",
                    "technical-debt-analyzer prompt", 
                    "architecture-documenter prompt",
                    "code-review-assistant prompt",
                    "test-coverage-improver prompt"
                }
            }
        };

        return JsonSerializer.Serialize(phase3Tools, _jsonOptions);
    }

    // Helper methods
    private int GetToolCount() => 25; // Approximate count based on tool registry
    private int GetSearchToolCount() => 8;
    private int GetMemoryToolCount() => 12;
    private int GetAnalysisToolCount() => 4;
    private int GetSystemToolCount() => 3;
    private int GetWorkflowToolCount() => 2;

    private object CreateToolInfo(string toolName, string description, string importance, string[] tags)
    {
        return new
        {
            Name = toolName,
            Description = description,
            Importance = importance,
            Tags = tags,
            Category = GetToolCategory(toolName)
        };
    }

    private string GetToolCategory(string toolName)
    {
        return toolName switch
        {
            var name when name.Contains("search") || name.Contains("file") || name.Contains("directory") || name.Contains("recent") || name.Contains("similar") || name.Contains("index") || name.Contains("batch") => "Search",
            var name when name.Contains("memory") || name.Contains("store") || name.Contains("recall") || name.Contains("backup") || name.Contains("restore") || name.Contains("link") || name.Contains("timeline") => "Memory", 
            var name when name.Contains("pattern") || name.Contains("analysis") || name.Contains("health") => "Analysis",
            var name when name.Contains("version") || name.Contains("log") => "System",
            var name when name.Contains("assistant") || name.Contains("navigator") => "Workflow",
            _ => "Other"
        };
    }

    private object[] GetAllToolsMetadata()
    {
        return new object[]
        {
            CreateToolInfo(ToolNames.IndexWorkspace, "Creates searchable index for workspace", "Critical", new[] { "Prerequisites", "Foundation" }),
            CreateToolInfo(ToolNames.TextSearch, "Search file contents with patterns", "High", new[] { "Search", "Content" }),
            CreateToolInfo(ToolNames.FileSearch, "Find files by name patterns", "High", new[] { "Search", "Files" }),
            CreateToolInfo(ToolNames.SearchAssistant, "AI-guided multi-step search", "Very High", new[] { "AI-Optimized", "Workflow" }),
            CreateToolInfo(ToolNames.PatternDetector, "Detect code patterns and issues", "Very High", new[] { "AI-Optimized", "Analysis" }),
            CreateToolInfo(ToolNames.MemoryGraphNavigator, "Explore memory relationships", "Very High", new[] { "AI-Optimized", "Memory" }),
            CreateToolInfo(ToolNames.StoreMemory, "Store project knowledge", "High", new[] { "Memory", "Storage" }),
            CreateToolInfo(ToolNames.SearchMemories, "Find stored knowledge", "High", new[] { "Memory", "Search" }),
            CreateToolInfo(ToolNames.RecallContext, "Load session context", "Very High", new[] { "Memory", "Context" })
        };
    }
}