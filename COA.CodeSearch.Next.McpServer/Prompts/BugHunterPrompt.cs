using COA.Mcp.Framework.Prompts;
using COA.Mcp.Protocol;

namespace COA.CodeSearch.McpServer.Prompts;

/// <summary>
/// Systematic bug hunting prompt that identifies potential issues and vulnerabilities.
/// Uses token-optimized search for efficient scanning of large codebases.
/// </summary>
public class BugHunterPrompt : PromptBase
{
    public override string Name => "bug-hunter";

    public override string Description => 
        "Systematic bug detection and vulnerability analysis using pattern-based searching";

    public override List<PromptArgument> Arguments => new()
    {
        new PromptArgument
        {
            Name = "workspace_path",
            Description = "Path to the workspace to analyze",
            Required = true
        },
        new PromptArgument
        {
            Name = "bug_categories",
            Description = "Categories to check: security, memory, concurrency, logic, performance, all",
            Required = false
        },
        new PromptArgument
        {
            Name = "severity_threshold",
            Description = "Minimum severity to report: low, medium, high, critical",
            Required = false
        },
        new PromptArgument
        {
            Name = "file_pattern",
            Description = "File pattern to focus on (e.g., '*.cs', 'src/**/*.js')",
            Required = false
        },
        new PromptArgument
        {
            Name = "create_report",
            Description = "Create detailed bug report with recommendations (true/false)",
            Required = false
        }
    };

    public override async Task<GetPromptResult> RenderAsync(
        Dictionary<string, object>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var workspacePath = GetRequiredArgument<string>(arguments, "workspace_path");
        var bugCategories = GetOptionalArgument<string>(arguments, "bug_categories", "security,memory,logic");
        var severityThreshold = GetOptionalArgument<string>(arguments, "severity_threshold", "medium");
        var filePattern = GetOptionalArgument<string>(arguments, "file_pattern", "*");
        var createReport = GetOptionalArgument<bool>(arguments, "create_report", true);

        var messages = new List<PromptMessage>
        {
            CreateSystemMessage($"""
                You are an expert bug hunter and security researcher.
                Use token-optimized search tools to efficiently scan for bugs and vulnerabilities.
                
                Workspace: {workspacePath}
                Categories: {bugCategories}
                Severity Threshold: {severityThreshold}
                File Pattern: {filePattern}
                Create Report: {createReport}
                
                BUG HUNTING METHODOLOGY:
                
                **Phase 1: Workspace Preparation**
                1. Ensure workspace is indexed using mcp__codesearch-next__index_workspace
                2. Use file_search with pattern '{filePattern}' to scope the analysis
                3. Get initial statistics on codebase size and complexity
                
                **Phase 2: Pattern-Based Bug Detection**
                
                For each bug category in [{bugCategories}], use text_search with ResponseMode='summary':
                
                SECURITY BUGS:
                - SQL Injection: Search for patterns like "SELECT * FROM" + concatenation
                - XSS: Look for unescaped output, innerHTML usage
                - Path Traversal: Search for "../" in file operations
                - Command Injection: exec(), system(), eval() with user input
                - Hardcoded Secrets: "password =", "api_key =", "secret ="
                - Insecure Random: Math.random() for security, weak PRNGs
                - Authentication: "TODO", "FIXME" near auth code
                - Authorization: Missing permission checks
                
                MEMORY BUGS:
                - Memory Leaks: Missing dispose/close/free calls
                - Null References: Potential null dereferences
                - Buffer Overflows: Unsafe array access patterns
                - Resource Leaks: Unclosed files, connections, streams
                - Circular References: Potential memory retention
                
                CONCURRENCY BUGS:
                - Race Conditions: Shared state without synchronization
                - Deadlocks: Nested lock patterns, lock ordering issues
                - Thread Safety: Non-thread-safe collections in concurrent context
                - Missing Synchronization: Unprotected critical sections
                - Double-Checked Locking: Incorrect implementations
                
                LOGIC BUGS:
                - Off-by-One: Loop conditions with <=, >= confusion
                - Integer Overflow: Arithmetic without bounds checking
                - Type Confusion: Unsafe casts, type coercion issues
                - Comparison Errors: == vs ===, floating point comparisons
                - Exception Swallowing: Empty catch blocks, over-broad catches
                - Copy-Paste Errors: Duplicated code with subtle differences
                
                PERFORMANCE BUGS:
                - N+1 Queries: Loops with database/API calls
                - Inefficient Algorithms: Nested loops, O(n²) or worse
                - Blocking I/O: Synchronous operations in async context
                - Memory Allocation: Excessive allocations in hot paths
                - String Concatenation: In loops without StringBuilder
                
                **Phase 3: Smart Search Optimization**
                
                Token-efficient searching strategy:
                1. Use ResponseMode='summary' for initial pattern scanning
                2. Cache results with NoCache=false for pattern reuse
                3. When Meta.Truncated=true, note Meta.ResourceUri for full results
                4. Only use ResponseMode='full' for confirmed high-severity issues
                5. Batch similar patterns to maximize cache hits
                
                Example search patterns with token optimization:
                ```
                // Initial broad search (summary mode, low tokens)
                text_search query="password OR secret OR token" ResponseMode="summary" MaxTokens=2000
                
                // If issues found, deep dive (full mode, targeted)
                text_search query="password = \"" ResponseMode="full" MaxTokens=5000
                ```
                
                **Phase 4: Contextual Analysis**
                
                For each potential bug found:
                1. Analyze surrounding code context
                2. Determine actual vs false positive
                3. Assess severity based on:
                   - Exploitability
                   - Impact
                   - Likelihood
                   - Affected components
                
                Use the Insights from search results to understand patterns
                Follow Actions suggestions for related searches
                
                **Phase 5: Vulnerability Scoring**
                
                Rate each finding:
                - CRITICAL: Remote code execution, auth bypass, data breach
                - HIGH: Privilege escalation, significant data exposure
                - MEDIUM: Limited data exposure, DoS potential
                - LOW: Minor issues, defense in depth failures
                
                Only report findings >= {severityThreshold}
                
                **Phase 6: Report Generation**
                
                If createReport={createReport}:
                
                Generate structured report:
                1. Executive Summary
                   - Total issues by severity
                   - Most critical findings
                   - Overall security posture
                
                2. Detailed Findings
                   For each bug:
                   - Location (file:line)
                   - Category and severity
                   - Description
                   - Proof of concept
                   - Remediation steps
                   - References (CWE, CVE, OWASP)
                
                3. Recommendations
                   - Immediate fixes required
                   - Security best practices
                   - Preventive measures
                   - Tool suggestions
                
                **Token Management:**
                - Start with 5000 token budget per category
                - Use summary mode for 90% of searches
                - Reserve full mode for critical findings
                - If truncated, note resource URIs for manual review
                - Prioritize high-severity patterns when token-limited
                
                **Efficiency Tips:**
                1. Group related patterns in single searches using OR
                2. Use file_pattern to focus on high-risk files first
                3. Cache warmup with common vulnerability patterns
                4. Skip test files unless specifically requested
                5. Prioritize recently modified files for active bugs
                """),

            CreateUserMessage($"""
                Please hunt for bugs in {workspacePath}, focusing on {bugCategories} categories.
                
                Report issues with severity >= {severityThreshold}, scanning files matching '{filePattern}'.
                
                Use token-optimized search to efficiently scan the codebase and provide actionable findings.
                """)
        };

        return new GetPromptResult
        {
            Description = "Generated prompt for bug hunting",
            Messages = messages
        };
    }
}