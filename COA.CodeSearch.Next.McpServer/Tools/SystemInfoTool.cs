using System.ComponentModel;
using System.Runtime.InteropServices;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Interfaces;
using COA.Mcp.Framework.Models;

namespace COA.CodeSearch.Next.McpServer.Tools;

/// <summary>
/// Tool that provides system information.
/// </summary>
public class SystemInfoTool : McpToolBase<SystemInfoParameters, SystemInfoResult>
{
    public override string Name => ToolNames.SystemInfo;
    public override string Description => "Gets system information including OS, runtime, and environment details";
    public override ToolCategory Category => ToolCategory.Utility;

    protected override async Task<SystemInfoResult> ExecuteInternalAsync(
        SystemInfoParameters parameters,
        CancellationToken cancellationToken)
    {
        var result = new SystemInfoResult
        {
            OperatingSystem = new OperatingSystemInfo
            {
                Platform = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.OSArchitecture.ToString(),
                Is64Bit = Environment.Is64BitOperatingSystem
            },
            Runtime = new RuntimeInfo
            {
                Framework = RuntimeInformation.FrameworkDescription,
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                DotNetVersion = Environment.Version.ToString()
            },
            Machine = new MachineInfo
            {
                MachineName = Environment.MachineName,
                ProcessorCount = Environment.ProcessorCount,
                UserName = Environment.UserName,
                UserDomainName = Environment.UserDomainName
            },
            Process = new ProcessInfo
            {
                CurrentDirectory = Environment.CurrentDirectory,
                ProcessId = Environment.ProcessId,
                MemoryUsageMB = GC.GetTotalMemory(false) / (1024 * 1024)
            }
        };

        if (parameters.IncludeEnvironment == true)
        {
            result.EnvironmentVariables = Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(de => de.Key.ToString()!, de => de.Value?.ToString());
        }

        return await Task.FromResult(result);
    }
}

/// <summary>
/// Parameters for the SystemInfo tool.
/// </summary>
public class SystemInfoParameters
{
    /// <summary>
    /// Include environment variables in the output
    /// </summary>
    [Description("Include environment variables in the output")]
    public bool? IncludeEnvironment { get; set; }

    /// <summary>
    /// Maximum tokens for response (for token optimization)
    /// </summary>
    [Description("Maximum tokens for response (for token optimization)")]
    public int? MaxTokens { get; set; }
}

/// <summary>
/// Result from the SystemInfo tool.
/// </summary>
public class SystemInfoResult : ToolResultBase
{
    public override string Operation => ToolNames.SystemInfo;
    
    public OperatingSystemInfo OperatingSystem { get; set; } = new();
    public RuntimeInfo Runtime { get; set; } = new();
    public MachineInfo Machine { get; set; } = new();
    public ProcessInfo Process { get; set; } = new();
    public Dictionary<string, string?>? EnvironmentVariables { get; set; }
}

public class OperatingSystemInfo
{
    public string Platform { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public bool Is64Bit { get; set; }
}

public class RuntimeInfo
{
    public string Framework { get; set; } = string.Empty;
    public string ProcessArchitecture { get; set; } = string.Empty;
    public string DotNetVersion { get; set; } = string.Empty;
}

public class MachineInfo
{
    public string MachineName { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserDomainName { get; set; } = string.Empty;
}

public class ProcessInfo
{
    public string CurrentDirectory { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public long MemoryUsageMB { get; set; }
}