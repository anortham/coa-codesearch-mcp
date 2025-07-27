using COA.CodeSearch.McpServer.Constants;
using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Provides type discovery resources for AI agents to understand available enum values,
/// memory types, and other parameter options. This helps reduce AI confusion by making
/// all valid values discoverable.
/// </summary>
public class TypeDiscoveryResourceProvider : IResourceProvider
{
    private readonly ILogger<TypeDiscoveryResourceProvider> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public string Scheme => "codesearch-types";
    public string Name => "Type Discovery";
    public string Description => "Discover valid parameter values, memory types, and enum options";

    public TypeDiscoveryResourceProvider(ILogger<TypeDiscoveryResourceProvider> logger)
    {
        _logger = logger;
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
                Uri = "codesearch-types://search/searchTypes",
                Name = "Search Type Options",
                Description = "Valid search type values for text_search and file_search tools",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-types://memory/types",
                Name = "Memory Types",
                Description = "Available memory types with descriptions and schemas",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-types://memory/relationships",
                Name = "Memory Relationship Types",
                Description = "Valid relationship types for linking memories",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-types://file/patterns",
                Name = "File Pattern Examples",
                Description = "Common file pattern examples for search operations",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-types://time/formats",
                Name = "Time Format Examples",
                Description = "Valid time format examples for recent_files tool",
                MimeType = "application/json"
            }
        };
    }

    public async Task<ReadResourceResult?> ReadResourceAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(uri))
            return null;

        await Task.CompletedTask; // Keep async for interface compliance

        try
        {
            var path = uri.Replace($"{Scheme}://", "").ToLowerInvariant();
            
            object? content = path switch
            {
                "search/searchtypes" => GetSearchTypes(),
                "memory/types" => GetMemoryTypes(),
                "memory/relationships" => GetRelationshipTypes(),
                "file/patterns" => GetFilePatterns(),
                "time/formats" => GetTimeFormats(),
                _ => null
            };

            if (content == null)
            {
                _logger.LogWarning("Unknown type discovery resource requested: {Uri}", uri);
                return null;
            }

            var json = JsonSerializer.Serialize(content, _jsonOptions);
            
            return new ReadResourceResult
            {
                Contents = new List<ResourceContent>
                {
                    new ResourceContent
                    {
                        Uri = uri,
                        MimeType = "application/json",
                        Text = json
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading type discovery resource: {Uri}", uri);
            return null;
        }
    }

    public bool CanHandle(string uri)
    {
        return uri?.StartsWith($"{Scheme}://", StringComparison.OrdinalIgnoreCase) == true;
    }

    private object GetSearchTypes()
    {
        return new
        {
            description = "Available search type options for text and file search operations",
            types = new[]
            {
                new
                {
                    value = "standard",
                    description = "Default search - exact substring match (case-insensitive by default)",
                    examples = new[] { "getUserName", "TODO", "class Repository" },
                    useCase = "Finding exact occurrences of terms"
                },
                new
                {
                    value = "fuzzy",
                    description = "Typo-tolerant search - finds approximate matches",
                    examples = new[] { "getUserNam~", "repositry~", "autentication~" },
                    useCase = "Finding terms when you're unsure of exact spelling"
                },
                new
                {
                    value = "wildcard",
                    description = "Pattern matching with * (any chars) and ? (single char)",
                    examples = new[] { "get*Name", "user?.cs", "*Service.cs" },
                    useCase = "Finding patterns or partial matches"
                },
                new
                {
                    value = "phrase",
                    description = "Exact phrase matching - preserves word order",
                    examples = new[] { "\"get user name\"", "\"throw new Exception\"", "\"TODO: fix this\"" },
                    useCase = "Finding exact multi-word phrases"
                },
                new
                {
                    value = "regex",
                    description = "Full regular expression support",
                    examples = new[] { "get\\w+Name", "^User.*Service$", "TODO:?\\s*\\w+" },
                    useCase = "Complex pattern matching with regex"
                }
            }
        };
    }

    private object GetMemoryTypes()
    {
        return new
        {
            description = "Available memory types for storing project knowledge",
            types = new object[]
            {
                new
                {
                    name = "TechnicalDebt",
                    description = "Track technical debt and code improvement opportunities",
                    schema = new
                    {
                        required = new[] { "severity", "effort" },
                        properties = new
                        {
                            severity = new { @enum = new[] { "low", "medium", "high", "critical" } },
                            effort = new { @enum = new[] { "minutes", "hours", "days", "weeks" } },
                            component = new { type = "string", description = "Affected component or module" },
                            impact = new { type = "string", description = "Impact on system if not addressed" }
                        }
                    },
                    examples = new[]
                    {
                        "Legacy authentication system needs OAuth2 migration",
                        "Database queries in UI layer violate separation of concerns"
                    }
                },
                new
                {
                    name = "ArchitecturalDecision",
                    description = "Document important architectural decisions and rationale",
                    schema = new
                    {
                        required = new[] { "decision", "rationale" },
                        properties = new
                        {
                            decision = new { type = "string", description = "The decision made" },
                            rationale = new { type = "string", description = "Why this decision was made" },
                            alternatives = new { type = "array", description = "Alternatives considered" },
                            consequences = new { type = "string", description = "Expected consequences" }
                        }
                    },
                    examples = new[]
                    {
                        "Use Redis for distributed caching instead of in-memory cache",
                        "Adopt Clean Architecture pattern for service layer"
                    }
                },
                new
                {
                    name = "CodePattern",
                    description = "Document recurring code patterns and conventions",
                    schema = new
                    {
                        required = new[] { "pattern", "usage" },
                        properties = new
                        {
                            pattern = new { type = "string", description = "The pattern name" },
                            usage = new { type = "string", description = "When to use this pattern" },
                            example = new { type = "string", description = "Code example" },
                            antiPattern = new { type = "string", description = "What to avoid" }
                        }
                    },
                    examples = new[]
                    {
                        "Repository pattern for data access with Unit of Work",
                        "Builder pattern for complex object construction"
                    }
                },
                new
                {
                    name = "SecurityRule",
                    description = "Security guidelines and requirements",
                    schema = new
                    {
                        required = new[] { "rule", "severity" },
                        properties = new
                        {
                            rule = new { type = "string", description = "The security rule" },
                            severity = new { @enum = new[] { "info", "warning", "error", "critical" } },
                            mitigation = new { type = "string", description = "How to address violations" },
                            owasp = new { type = "string", description = "Related OWASP category" }
                        }
                    },
                    examples = new[]
                    {
                        "All API endpoints must implement rate limiting",
                        "Passwords must be hashed with bcrypt (min 12 rounds)"
                    }
                },
                new
                {
                    name = "ProjectInsight",
                    description = "General project insights and learnings",
                    schema = new
                    {
                        required = new[] { "insight", "category" },
                        properties = new
                        {
                            insight = new { type = "string", description = "The insight or learning" },
                            category = new { @enum = new[] { "performance", "maintainability", "testing", "deployment", "monitoring" } },
                            evidence = new { type = "string", description = "Supporting evidence or metrics" }
                        }
                    },
                    examples = new[]
                    {
                        "Async operations improve API response time by 40%",
                        "Integration tests catch 80% of production issues"
                    }
                }
            }
        };
    }

    private object GetRelationshipTypes()
    {
        return new
        {
            description = "Valid relationship types for linking memories",
            types = new[]
            {
                new
                {
                    value = "relatedTo",
                    description = "General relationship between memories",
                    bidirectional = true,
                    example = "Feature A is relatedTo Feature B"
                },
                new
                {
                    value = "blockedBy",
                    description = "One item is blocked by another",
                    bidirectional = false,
                    example = "OAuth implementation blockedBy config service refactor"
                },
                new
                {
                    value = "implements",
                    description = "One item implements or realizes another",
                    bidirectional = false,
                    example = "UserService implements Repository pattern"
                },
                new
                {
                    value = "supersedes",
                    description = "One item replaces or supersedes another",
                    bidirectional = false,
                    example = "JWT auth supersedes session-based auth"
                },
                new
                {
                    value = "dependsOn",
                    description = "One item depends on another",
                    bidirectional = false,
                    example = "API gateway dependsOn authentication service"
                },
                new
                {
                    value = "parentOf",
                    description = "Hierarchical parent-child relationship",
                    bidirectional = false,
                    example = "Epic parentOf user story"
                },
                new
                {
                    value = "references",
                    description = "One item references or mentions another",
                    bidirectional = false,
                    example = "Security audit references OWASP Top 10"
                },
                new
                {
                    value = "causes",
                    description = "One item causes or leads to another",
                    bidirectional = false,
                    example = "Memory leak causes performance degradation"
                },
                new
                {
                    value = "resolves",
                    description = "One item resolves or fixes another",
                    bidirectional = false,
                    example = "Caching layer resolves database bottleneck"
                },
                new
                {
                    value = "duplicates",
                    description = "Items are duplicates of each other",
                    bidirectional = true,
                    example = "Issue #123 duplicates Issue #456"
                }
            }
        };
    }

    private object GetFilePatterns()
    {
        return new
        {
            description = "Common file pattern examples for search operations",
            patterns = new[]
            {
                new
                {
                    pattern = "*.cs",
                    description = "All C# files",
                    example = "Matches: Program.cs, UserService.cs"
                },
                new
                {
                    pattern = "**/*.ts",
                    description = "All TypeScript files in any subdirectory",
                    example = "Matches: src/app.ts, components/button/index.ts"
                },
                new
                {
                    pattern = "src/**/*.js",
                    description = "All JavaScript files under src directory",
                    example = "Matches: src/index.js, src/utils/helper.js"
                },
                new
                {
                    pattern = "*Test.cs",
                    description = "All C# test files",
                    example = "Matches: UserServiceTest.cs, RepositoryTest.cs"
                },
                new
                {
                    pattern = "!node_modules/**",
                    description = "Exclude all files in node_modules",
                    example = "Excludes entire node_modules directory"
                },
                new
                {
                    pattern = "src/**/[Tt]est*.cs",
                    description = "Test files with case-insensitive prefix",
                    example = "Matches: TestHelper.cs, testUtils.cs"
                },
                new
                {
                    pattern = "*.{cs,ts,js}",
                    description = "Multiple extensions",
                    example = "Matches: file.cs, file.ts, file.js"
                },
                new
                {
                    pattern = "docs/**/*.md",
                    description = "All markdown files in docs",
                    example = "Matches: docs/README.md, docs/api/endpoints.md"
                }
            },
            syntax = new
            {
                wildcards = new
                {
                    asterisk = "* matches any characters within a path segment",
                    doubleAsterisk = "** matches any number of path segments",
                    question = "? matches a single character",
                    brackets = "[abc] matches any character in the set",
                    braces = "{a,b,c} matches any of the comma-separated patterns",
                    exclamation = "! at the start excludes matches"
                }
            }
        };
    }

    private object GetTimeFormats()
    {
        return new
        {
            description = "Valid time format examples for recent_files tool",
            formats = new[]
            {
                new
                {
                    format = "30m",
                    description = "30 minutes",
                    useCase = "Very recent changes during active development"
                },
                new
                {
                    format = "1h",
                    description = "1 hour",
                    useCase = "Changes in the last hour"
                },
                new
                {
                    format = "24h",
                    description = "24 hours (default)",
                    useCase = "Today's work"
                },
                new
                {
                    format = "3d",
                    description = "3 days",
                    useCase = "Recent work over a long weekend"
                },
                new
                {
                    format = "7d",
                    description = "7 days (1 week)",
                    useCase = "Weekly development cycle"
                },
                new
                {
                    format = "2w",
                    description = "2 weeks",
                    useCase = "Sprint duration"
                },
                new
                {
                    format = "4w",
                    description = "4 weeks",
                    useCase = "Monthly review"
                }
            },
            pattern = new
            {
                description = "Format: number + unit",
                units = new
                {
                    m = "minutes",
                    h = "hours",
                    d = "days",
                    w = "weeks"
                },
                examples = new[] { "15m", "2h", "5d", "3w" }
            }
        };
    }
}