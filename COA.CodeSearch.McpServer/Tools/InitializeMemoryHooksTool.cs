using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// MCP tool to initialize Claude memory system hooks for automatic context management
/// </summary>
public class InitializeMemoryHooksTool
{
    private readonly ILogger<InitializeMemoryHooksTool> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ClaudeMemoryService _memoryService;

    public InitializeMemoryHooksTool(ILogger<InitializeMemoryHooksTool> logger, ILoggerFactory loggerFactory, ClaudeMemoryService memoryService)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _memoryService = memoryService;
    }

    public async Task<object> InitializeMemoryHooks(
        string? projectRoot = null)
    {
        try
        {
            var hookManagerLogger = _loggerFactory.CreateLogger<MemoryHookManager>();
            var hookManager = new MemoryHookManager(hookManagerLogger, projectRoot);
            
            // Initialize the hooks
            var success = await hookManager.InitializeHooksAsync();
            if (!success)
            {
                return new 
                { 
                    success = false, 
                    error = "Failed to initialize memory hooks" 
                };
            }

            // README is now created automatically by InitializeHooksAsync

            // Store this as an architectural decision!
            await _memoryService.StoreArchitecturalDecisionAsync(
                "Implemented Claude Memory System with automatic hooks",
                "Hooks provide zero-effort memory management: " +
                "1) pre-tool-use hook loads relevant context before operations, " +
                "2) file-edit hook detects architectural patterns, " +
                "3) stop hook preserves work history. " +
                "This ensures knowledge persistence and team collaboration.",
                new[] { ".claude/hooks/pre-tool-use.*", ".claude/hooks/file-edit.*", ".claude/hooks/stop.*" },
                new[] { "architecture", "automation", "memory-system" }
            );

            var platform = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) 
                ? "Windows (PowerShell)" 
                : "Unix (Bash)";

            var response = $@"✅ **Claude Memory Hooks Initialized!**

📁 Location: `.claude/hooks/`
🖥️ Platform: {platform}

**Hooks Created:**
- 🎯 **pre-tool-use**: Auto-loads context before MCP tool execution
- 📝 **file-edit**: Detects patterns and suggests memory storage  
- 🏁 **stop**: Saves session summary after each Claude response

**What happens now:**
1. When you use tools like `find_references`, relevant memories load automatically
2. When you edit files with patterns (Repository, Service, etc.), you'll get memory suggestions
3. When your session ends, work progress is automatically saved

**Example:**
```
# You: find_references for UserService
# Hook: 🧠 Loading memories for: UserService.cs
# Hook: [Shows previous architectural decisions about UserService]
# Then: Normal find_references execution with context!
```

The memory system is now on **autopilot**! 🚁

💡 **First architectural decision stored:** The memory system documented its own creation!";

            _logger.LogInformation("Memory hooks initialized successfully at {Root}", projectRoot ?? Directory.GetCurrentDirectory());
            
            return new 
            { 
                success = true, 
                message = response 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing memory hooks");
            return new 
            { 
                success = false, 
                error = $"Error initializing hooks: {ex.Message}" 
            };
        }
    }

    public async Task<object> TestMemoryHooks(
        string hookType)
    {
        try
        {
            var hooksDir = Path.Combine(Directory.GetCurrentDirectory(), ".claude", "hooks");
            if (!Directory.Exists(hooksDir))
            {
                return new 
                { 
                    success = false, 
                    error = "Hooks directory not found. Run init_memory_hooks first." 
                };
            }

            var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);
            var extension = isWindows ? ".ps1" : ".sh";
            var hookFile = Path.Combine(hooksDir, $"{hookType}{extension}");

            if (!File.Exists(hookFile))
            {
                return new 
                { 
                    success = false, 
                    error = $"Hook file not found: {hookType}{extension}" 
                };
            }

            // Create test environment variables
            var testEnv = hookType switch
            {
                "pre-tool-use" => new Dictionary<string, string>
                {
                    ["CLAUDE_TOOL_NAME"] = "find_references",
                    ["CLAUDE_TOOL_PARAMS"] = "{\"filePath\":\"TestFile.cs\",\"line\":10,\"column\":5}"
                },
                "file-edit" => new Dictionary<string, string>
                {
                    ["CLAUDE_FILE_PATH"] = "TestRepository.cs",
                    ["CLAUDE_FILE_OPERATION"] = "edit"
                },
                "stop" => new Dictionary<string, string>(),
                _ => throw new ArgumentException($"Unknown hook type: {hookType}")
            };

            var response = $@"🧪 **Testing {hookType} hook**

Hook file: `{Path.GetFileName(hookFile)}`
Platform: {(isWindows ? "Windows" : "Unix")}

**Test Environment:**
{string.Join("\n", testEnv.Select(kv => $"- {kv.Key} = {kv.Value}"))}

**Expected Behavior:**
{GetExpectedBehavior(hookType)}

**To manually test, run:**
```{(isWindows ? "powershell" : "bash")}
{GetTestCommand(hookType, isWindows)}
```

Hook system is ready for automatic memory management! 🎯";

            return new 
            { 
                success = true, 
                message = response 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing memory hooks");
            return new 
            { 
                success = false, 
                error = $"Error testing hooks: {ex.Message}" 
            };
        }
    }

    private static string GetExpectedBehavior(string hookType) => hookType switch
    {
        "pre-tool-use" => "Should load memories related to TestFile.cs before tool execution",
        "file-edit" => "Should detect Repository pattern and suggest memory storage",
        "stop" => "Should save session summary with timestamp and modified files",
        _ => "Unknown hook type"
    };

    private static string GetTestCommand(string hookType, bool isWindows)
    {
        if (isWindows)
        {
            return hookType switch
            {
                "pre-tool-use" => "$env:CLAUDE_TOOL_NAME='find_references'; $env:CLAUDE_TOOL_PARAMS='{\"filePath\":\"Test.cs\"}'; .\\pre-tool-use.ps1",
                "file-edit" => "$env:CLAUDE_FILE_PATH='TestRepo.cs'; $env:CLAUDE_FILE_OPERATION='edit'; .\\file-edit.ps1",
                "stop" => ".\\stop.ps1",
                _ => ""
            };
        }
        else
        {
            return hookType switch
            {
                "pre-tool-use" => "CLAUDE_TOOL_NAME='find_references' CLAUDE_TOOL_PARAMS='{\"filePath\":\"Test.cs\"}' ./pre-tool-use.sh",
                "file-edit" => "CLAUDE_FILE_PATH='TestRepo.cs' CLAUDE_FILE_OPERATION='edit' ./file-edit.sh",
                "stop" => "./stop.sh",
                _ => ""
            };
        }
    }
}