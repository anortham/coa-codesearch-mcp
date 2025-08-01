using System.Reflection;
using Microsoft.Extensions.Logging;
using System.Linq;
using COA.CodeSearch.McpServer.Attributes;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tool to get the version and build information of the running MCP server.
/// This helps Claude understand which version is actually running vs what's being edited.
/// </summary>
[McpServerToolType]
public class GetVersionTool : ITool
{
    public string ToolName => "get_version";
    public string Description => "Get version and build information";
    public ToolCategory Category => ToolCategory.Infrastructure;
    private readonly ILogger<GetVersionTool> _logger;
    private static readonly Assembly ExecutingAssembly = Assembly.GetExecutingAssembly();
    private static readonly DateTime BuildDate = GetBuildDate();
    
    public GetVersionTool(ILogger<GetVersionTool> logger)
    {
        _logger = logger;
    }
    
    [McpServerTool(Name = "get_version")]
    [Description("Get the version and build information of the running MCP server. Shows version number, build date, runtime info, and helps identify if running code matches edited code.")]
    public Task<object> ExecuteAsync()
    {
        try
        {
            var version = ExecutingAssembly.GetName().Version?.ToString() ?? "Unknown";
            var fileVersion = ExecutingAssembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? version;
            var informationalVersion = ExecutingAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? version;
            
            // Get the location of the executing assembly
            var location = ExecutingAssembly.Location;
            var fileInfo = new FileInfo(location);
            
            // Get compilation date from assembly
            var compileDate = fileInfo.LastWriteTimeUtc;
            
            // Get runtime information
            var framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            var os = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
            var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();
            
            var result = new
            {
                success = true,
                version = new
                {
                    assembly = version,
                    file = fileVersion,
                    informational = informationalVersion,
                    semantic = ExtractSemanticVersion(informationalVersion)
                },
                build = new
                {
                    date = BuildDate.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    compiledFrom = fileInfo.DirectoryName,
                    fileModified = compileDate.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    age = GetAge(BuildDate)
                },
                runtime = new
                {
                    framework,
                    os,
                    architecture = arch,
                    serverStarted = Program.ServerStartTime.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    uptime = GetUptime()
                },
                hints = new
                {
                    isDebugBuild = IsDebugBuild(),
                    buildConfiguration = GetBuildConfiguration()
                }
            };
            
            _logger.LogInformation("Version information requested: v{Version} built on {BuildDate}", 
                version, BuildDate);
            
            return Task.FromResult<object>(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get version information");
            return Task.FromResult<object>(new
            {
                success = false,
                error = ex.Message
            });
        }
    }
    
    private static DateTime GetBuildDate()
    {
        // Try to get build date from assembly attributes first
        var buildDateAttr = ExecutingAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attr => attr.Key == "BuildDate");
        
        if (buildDateAttr != null && DateTime.TryParse(buildDateAttr.Value, out var parsedDate))
        {
            return parsedDate.ToUniversalTime();
        }
        
        // Fallback to file modification time
        try
        {
            var location = ExecutingAssembly.Location;
            return new FileInfo(location).LastWriteTimeUtc;
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }
    
    private static string ExtractSemanticVersion(string version)
    {
        // Extract semantic version (e.g., "1.3.44" from "1.3.44.0" or "1.3.44+abc123")
        var parts = version.Split(new[] { '+', '-' }, 2);
        var versionPart = parts[0];
        
        // Remove trailing .0 if present
        if (versionPart.EndsWith(".0"))
        {
            versionPart = versionPart.Substring(0, versionPart.Length - 2);
        }
        
        return versionPart;
    }
    
    private static string GetAge(DateTime buildDate)
    {
        var age = DateTime.UtcNow - buildDate;
        
        if (age.TotalDays >= 1)
        {
            var days = (int)age.TotalDays;
            return $"{days} day{(days == 1 ? "" : "s")} ago";
        }
        else if (age.TotalHours >= 1)
        {
            var hours = (int)age.TotalHours;
            return $"{hours} hour{(hours == 1 ? "" : "s")} ago";
        }
        else if (age.TotalMinutes >= 1)
        {
            var minutes = (int)age.TotalMinutes;
            return $"{minutes} minute{(minutes == 1 ? "" : "s")} ago";
        }
        else
        {
            return "just now";
        }
    }
    
    private static string GetUptime()
    {
        var uptime = DateTime.UtcNow - Program.ServerStartTime;
        
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        }
        else if (uptime.TotalHours >= 1)
        {
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        }
        else if (uptime.TotalMinutes >= 1)
        {
            return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
        }
        else
        {
            return $"{(int)uptime.TotalSeconds}s";
        }
    }
    
    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }
    
    private static string GetBuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }
}