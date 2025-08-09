# COA MCP Framework Migration Guide for CodeSearch

## Overview

This guide provides step-by-step instructions for migrating COA CodeSearch MCP to use the COA MCP Framework 1.4.2, while simultaneously removing all memory-related functionality.

## Prerequisites

- [ ] COA MCP Framework 1.4.2 NuGet package available
- [ ] ProjectKnowledge MCP server implemented and tested
- [ ] All memories exported from current CodeSearch
- [ ] Backup of current CodeSearch codebase
- [ ] .NET 8.0 SDK installed

## Phase 1: Project Setup

### 1.1 Create New Project Structure

```bash
# Create new project
dotnet new console -n COA.CodeSearch.McpServer -f net8.0
cd COA.CodeSearch.McpServer

# Add Framework package
dotnet add package COA.Mcp.Framework --version 1.4.2

# Add Lucene packages
dotnet add package Lucene.Net --version 4.8.0-beta00017
dotnet add package Lucene.Net.Analysis.Common --version 4.8.0-beta00017
dotnet add package Lucene.Net.QueryParser --version 4.8.0-beta00017
dotnet add package Lucene.Net.Highlighter --version 4.8.0-beta00017
```

### 1.2 Update Project File

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>false</SelfContained>
    <PublishReadyToRun>true</PublishReadyToRun>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="COA.Mcp.Framework" Version="1.4.2" />
    <PackageReference Include="Lucene.Net" Version="4.8.0-beta00017" />
    <PackageReference Include="Lucene.Net.Analysis.Common" Version="4.8.0-beta00017" />
    <PackageReference Include="Lucene.Net.QueryParser" Version="4.8.0-beta00017" />
    <PackageReference Include="Lucene.Net.Highlighter" Version="4.8.0-beta00017" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

## Phase 2: Core Service Migration

### 2.1 Migrate LuceneIndexService

```csharp
// Services/Search/LuceneIndexService.cs
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services.Search;

public class LuceneIndexService : IDisposable
{
    private readonly ILogger<LuceneIndexService> _logger;
    private readonly string _indexPath;
    private FSDirectory? _directory;
    private IndexWriter? _writer;
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;
    private readonly CodeAnalyzer _analyzer;
    
    public LuceneIndexService(
        ILogger<LuceneIndexService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _indexPath = configuration["CodeSearch:Index:Path"] ?? ".codesearch/index";
        _analyzer = new CodeAnalyzer(); // Custom analyzer for code
        
        InitializeIndex();
    }
    
    private void InitializeIndex()
    {
        _directory = FSDirectory.Open(_indexPath);
        
        var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, _analyzer)
        {
            OpenMode = OpenMode.CREATE_OR_APPEND,
            RAMBufferSizeMB = 256
        };
        
        _writer = new IndexWriter(_directory, config);
        _writer.Commit(); // Ensure index exists
        
        RefreshSearcher();
    }
    
    public async Task<List<SearchResult>> SearchTextAsync(
        string query, 
        string workspacePath,
        int maxResults = 50)
    {
        var results = new List<SearchResult>();
        
        try
        {
            var parser = new QueryParser(LuceneVersion.LUCENE_48, "content", _analyzer);
            var luceneQuery = parser.Parse(query);
            
            var filter = new TermQuery(new Term("workspace", workspacePath));
            var booleanQuery = new BooleanQuery
            {
                { luceneQuery, Occur.MUST },
                { filter, Occur.MUST }
            };
            
            var topDocs = _searcher.Search(booleanQuery, maxResults);
            
            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = _searcher.Doc(scoreDoc.Doc);
                results.Add(new SearchResult
                {
                    FilePath = doc.Get("path"),
                    Content = doc.Get("content"),
                    Score = scoreDoc.Score,
                    LineNumber = int.Parse(doc.Get("lineNumber") ?? "0")
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query: {Query}", query);
            throw;
        }
        
        return results;
    }
    
    public async Task IndexWorkspaceAsync(string workspacePath, IProgress<int>? progress = null)
    {
        var files = Directory.GetFiles(workspacePath, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".git") && !f.Contains("node_modules"))
            .ToList();
        
        var indexed = 0;
        foreach (var file in files)
        {
            await IndexFileAsync(file, workspacePath);
            indexed++;
            progress?.Report((indexed * 100) / files.Count);
        }
        
        _writer.Commit();
        RefreshSearcher();
        
        _logger.LogInformation("Indexed {Count} files in {Path}", files.Count, workspacePath);
    }
    
    private async Task IndexFileAsync(string filePath, string workspace)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            
            var doc = new Document
            {
                new StringField("path", filePath, Field.Store.YES),
                new TextField("content", content, Field.Store.YES),
                new StringField("workspace", workspace, Field.Store.YES),
                new StringField("extension", Path.GetExtension(filePath), Field.Store.YES),
                new Int64Field("modified", new FileInfo(filePath).LastWriteTimeUtc.Ticks, Field.Store.YES)
            };
            
            _writer.UpdateDocument(new Term("path", filePath), doc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to index file: {Path}", filePath);
        }
    }
    
    private void RefreshSearcher()
    {
        _reader?.Dispose();
        _reader = DirectoryReader.Open(_directory);
        _searcher = new IndexSearcher(_reader);
    }
    
    public void Dispose()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _directory?.Dispose();
    }
}
```

### 2.2 Migrate CodeAnalyzer

```csharp
// Services/Search/CodeAnalyzer.cs
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Analysis.Util;

namespace COA.CodeSearch.McpServer.Services.Search;

/// <summary>
/// Custom Lucene analyzer that preserves programming language patterns
/// </summary>
public class CodeAnalyzer : Analyzer
{
    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
    {
        // Custom tokenizer that preserves code patterns
        var tokenizer = new CodeTokenizer(reader);
        
        // Token filters
        TokenStream stream = tokenizer;
        stream = new LowerCaseFilter(LuceneVersion.LUCENE_48, stream);
        stream = new CodePatternFilter(stream); // Custom filter for code patterns
        
        return new TokenStreamComponents(tokenizer, stream);
    }
}

public class CodeTokenizer : CharTokenizer
{
    public CodeTokenizer(TextReader reader) : base(LuceneVersion.LUCENE_48, reader)
    {
    }
    
    protected override bool IsTokenChar(int c)
    {
        // Keep alphanumeric, underscore, and certain code symbols together
        return char.IsLetterOrDigit((char)c) || c == '_' || c == ':' || c == '.';
    }
}

public class CodePatternFilter : TokenFilter
{
    private readonly ICharTermAttribute _termAttr;
    
    public CodePatternFilter(TokenStream input) : base(input)
    {
        _termAttr = AddAttribute<ICharTermAttribute>();
    }
    
    public override bool IncrementToken()
    {
        if (!m_input.IncrementToken())
            return false;
        
        var term = _termAttr.ToString();
        
        // Preserve patterns like ": ITool", "[Fact]", "Task<string>"
        if (term.StartsWith(":") || term.StartsWith("[") || term.Contains("<"))
        {
            // Keep as-is for code pattern matching
        }
        
        return true;
    }
}
```

## Phase 3: Tool Migration

### 3.1 Convert Tools to Framework Pattern

```csharp
// Tools/TextSearchTool.cs
using COA.Mcp.Framework.Attributes;
using COA.Mcp.Framework.Interfaces;
using COA.CodeSearch.McpServer.Services.Search;
using System.ComponentModel;

namespace COA.CodeSearch.McpServer.Tools;

[McpServerToolType]
public class TextSearchTool
{
    private readonly LuceneIndexService _luceneService;
    private readonly ILogger<TextSearchTool> _logger;
    
    public TextSearchTool(
        LuceneIndexService luceneService,
        ILogger<TextSearchTool> logger)
    {
        _luceneService = luceneService;
        _logger = logger;
    }
    
    [McpServerTool(Name = "text_search")]
    [Description("Search file contents for text patterns with code-aware analysis")]
    public async Task<TextSearchResult> ExecuteAsync(TextSearchParams parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters.Query))
        {
            throw new ArgumentException("Query is required");
        }
        
        if (string.IsNullOrWhiteSpace(parameters.WorkspacePath))
        {
            throw new ArgumentException("WorkspacePath is required");
        }
        
        _logger.LogInformation("Searching for '{Query}' in {Path}", 
            parameters.Query, parameters.WorkspacePath);
        
        var results = await _luceneService.SearchTextAsync(
            parameters.Query,
            parameters.WorkspacePath,
            parameters.MaxResults ?? 50
        );
        
        // Format for AI consumption
        return new TextSearchResult
        {
            Success = true,
            Query = parameters.Query,
            Results = results.Select(r => new TextMatch
            {
                FilePath = r.FilePath,
                LineNumber = r.LineNumber,
                Content = r.Content,
                Score = r.Score
            }).ToList(),
            TotalMatches = results.Count,
            SearchType = parameters.SearchType ?? "standard"
        };
    }
}

public class TextSearchParams
{
    [Description("Search query supporting wildcards (*), fuzzy (~), and phrases (\"exact match\")")]
    public string Query { get; set; } = string.Empty;
    
    [Description("Directory path to search in")]
    public string WorkspacePath { get; set; } = string.Empty;
    
    [Description("Maximum number of results (default: 50)")]
    public int? MaxResults { get; set; }
    
    [Description("Search type: standard, literal, wildcard, fuzzy, regex")]
    public string? SearchType { get; set; }
    
    [Description("Include context lines around matches")]
    public int? ContextLines { get; set; }
}

public class TextSearchResult
{
    public bool Success { get; set; }
    public string Query { get; set; } = string.Empty;
    public List<TextMatch> Results { get; set; } = new();
    public int TotalMatches { get; set; }
    public string SearchType { get; set; } = string.Empty;
}

public class TextMatch
{
    public string FilePath { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public float Score { get; set; }
}
```

### 3.2 Index Workspace Tool

```csharp
// Tools/IndexWorkspaceTool.cs
[McpServerToolType]
public class IndexWorkspaceTool
{
    private readonly LuceneIndexService _luceneService;
    
    public IndexWorkspaceTool(LuceneIndexService luceneService)
    {
        _luceneService = luceneService;
    }
    
    [McpServerTool(Name = "index_workspace")]
    [Description("Index a workspace directory for searching")]
    public async Task<IndexResult> ExecuteAsync(IndexParams parameters)
    {
        var stopwatch = Stopwatch.StartNew();
        
        var progress = new Progress<int>(percent =>
        {
            // Could emit progress events if needed
        });
        
        await _luceneService.IndexWorkspaceAsync(parameters.WorkspacePath, progress);
        
        stopwatch.Stop();
        
        return new IndexResult
        {
            Success = true,
            WorkspacePath = parameters.WorkspacePath,
            IndexingTime = stopwatch.Elapsed,
            Message = $"Successfully indexed workspace in {stopwatch.Elapsed.TotalSeconds:F2} seconds"
        };
    }
}

public class IndexParams
{
    [Description("Directory path to index")]
    public string WorkspacePath { get; set; } = string.Empty;
    
    [Description("Force rebuild even if index exists")]
    public bool ForceRebuild { get; set; }
}

public class IndexResult
{
    public bool Success { get; set; }
    public string WorkspacePath { get; set; } = string.Empty;
    public TimeSpan IndexingTime { get; set; }
    public string Message { get; set; } = string.Empty;
}
```

## Phase 4: Program.cs with Framework

```csharp
// Program.cs
using COA.Mcp.Framework.Server;
using COA.CodeSearch.McpServer.Services.Search;
using COA.CodeSearch.McpServer.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Load configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // Create MCP server using Framework
        var builder = McpServer.CreateBuilder()
            .WithServerInfo("CodeSearch", "2.0.0", 
                "High-performance code search with Lucene.NET and CodeAnalyzer");

        // Configure STDIO transport (primary mode)
        builder.UseStdioTransport();

        // Register configuration
        builder.Services.AddSingleton<IConfiguration>(configuration);

        // Register search services
        builder.Services.AddSingleton<LuceneIndexService>();
        builder.Services.AddSingleton<FileIndexingService>();
        builder.Services.AddSingleton<CodeAnalyzer>();
        builder.Services.AddSingleton<PathResolutionService>();
        builder.Services.AddSingleton<QueryCacheService>();
        builder.Services.AddSingleton<ErrorHandlingService>();

        // Register response builders
        builder.Services.AddSingleton<TextSearchResponseBuilder>();
        builder.Services.AddSingleton<FileSearchResponseBuilder>();
        builder.Services.AddSingleton<AIResponseBuilderService>();

        // Register tools using Framework's attribute-based discovery
        builder.RegisterToolType<TextSearchTool>();
        builder.RegisterToolType<FileSearchTool>();
        builder.RegisterToolType<DirectorySearchTool>();
        builder.RegisterToolType<IndexWorkspaceTool>();
        builder.RegisterToolType<RecentFilesTool>();
        builder.RegisterToolType<FileSizeAnalysisTool>();
        builder.RegisterToolType<SimilarFilesTool>();
        builder.RegisterToolType<BatchOperationsTool>();

        // System tools
        builder.RegisterToolType<GetVersionTool>();
        builder.RegisterToolType<IndexHealthCheckTool>();

        // Configure logging
        builder.ConfigureLogging((context, logging) =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
            
            // Debug level for development
            if (args.Contains("--debug"))
            {
                logging.SetMinimumLevel(LogLevel.Debug);
            }
        });

        // Build and run
        var server = builder.Build();
        
        // Log startup
        var logger = server.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("CodeSearch MCP Server starting with COA Framework 1.4.2");
        
        await server.RunAsync();
    }
}
```

## Phase 5: Configuration

### 5.1 New appsettings.json

```json
{
  "CodeSearch": {
    "Index": {
      "Path": ".codesearch/index",
      "MaxFieldLength": 100000,
      "RamBufferSizeMB": 256,
      "AutoCommitIntervalSeconds": 30
    },
    "Search": {
      "DefaultMaxResults": 50,
      "AbsoluteMaxResults": 500,
      "CacheEnabled": true,
      "CacheDurationMinutes": 10,
      "TimeoutSeconds": 30
    },
    "FileIndexing": {
      "MaxFileSizeMB": 10,
      "ExcludePatterns": [
        "*.min.js",
        "*.min.css",
        "*.map",
        "*.dll",
        "*.exe",
        "*.pdb"
      ],
      "ExcludeFolders": [
        ".git",
        ".vs",
        "node_modules",
        "bin",
        "obj",
        "packages",
        ".nuget"
      ]
    },
    "CodeAnalyzer": {
      "Enabled": true,
      "PreservePatterns": true,
      "Languages": ["csharp", "javascript", "typescript", "python", "java", "go"]
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "COA": "Debug"
    }
  }
}
```

## Phase 6: Testing Migration

### 6.1 Create Test Project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.1" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
    <PackageReference Include="Moq" Version="4.20.69" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\COA.CodeSearch.McpServer\COA.CodeSearch.McpServer.csproj" />
  </ItemGroup>
</Project>
```

### 6.2 Test Core Functionality

```csharp
// Tests/TextSearchToolTests.cs
public class TextSearchToolTests
{
    [Fact]
    public async Task TextSearch_FindsContent()
    {
        // Arrange
        var luceneService = new Mock<LuceneIndexService>();
        luceneService.Setup(x => x.SearchTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<SearchResult>
            {
                new() { FilePath = "test.cs", Content = "public class Test", LineNumber = 1 }
            });
        
        var tool = new TextSearchTool(luceneService.Object, NullLogger<TextSearchTool>.Instance);
        
        // Act
        var result = await tool.ExecuteAsync(new TextSearchParams
        {
            Query = "public class",
            WorkspacePath = "C:/test"
        });
        
        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Results);
        Assert.Equal("test.cs", result.Results[0].FilePath);
    }
}
```

## Phase 7: Deployment

### 7.1 Build and Publish

```bash
# Build
dotnet build -c Release

# Run tests
dotnet test

# Publish as single file
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true

# Or publish as framework-dependent
dotnet publish -c Release -o ./publish
```

### 7.2 Update MCP Configuration

```json
{
  "mcpServers": {
    "codesearch": {
      "command": "dotnet",
      "args": ["C:/source/COA CodeSearch MCP/publish/COA.CodeSearch.McpServer.dll", "stdio"],
      "env": {
        "CODESEARCH_DEBUG": "false"
      }
    }
  }
}
```

## Phase 8: Verification Checklist

- [ ] All search tools working
- [ ] CodeAnalyzer preserving patterns
- [ ] Index creation successful
- [ ] Search results formatted for AI
- [ ] No memory-related errors
- [ ] Performance equal or better
- [ ] All tests passing
- [ ] Documentation updated

## Troubleshooting

### Issue: Missing Framework Features

If you encounter missing Framework features:

```csharp
// Check Framework version
var version = typeof(McpServer).Assembly.GetName().Version;
Console.WriteLine($"Framework version: {version}");
```

### Issue: Lucene Compatibility

Ensure all Lucene packages are same version:

```xml
<PackageReference Include="Lucene.Net" Version="4.8.0-beta00017" />
<PackageReference Include="Lucene.Net.Analysis.Common" Version="4.8.0-beta00017" />
<!-- All must be 4.8.0-beta00017 -->
```

### Issue: Tool Registration

Framework uses attribute-based discovery:

```csharp
[McpServerToolType] // Required on class
public class MyTool
{
    [McpServerTool(Name = "my_tool")] // Required on method
    public async Task<object> ExecuteAsync(MyParams parameters)
```

## Conclusion

This migration guide provides a complete path from the current complex CodeSearch to a simplified, Framework-based implementation. The result will be a cleaner, more maintainable codebase focused solely on search excellence.