using COA.CodeSearch.Next.McpServer.Models;

namespace COA.CodeSearch.Next.McpServer.Services;

public class ErrorRecoveryService : IErrorRecoveryService
{
    public RecoveryInfo GetIndexNotFoundRecovery(string workspacePath)
    {
        return new RecoveryInfo
        {
            Steps = new List<string>
            {
                $"Run index_workspace with workspacePath='{workspacePath}'",
                "Wait for indexing to complete (typically 10-60 seconds)",
                "Retry your search"
            },
            SuggestedActions = new List<SuggestedAction>
            {
                new SuggestedAction
                {
                    Tool = "index_workspace",
                    Params = new Dictionary<string, object> { ["workspacePath"] = workspacePath },
                    Description = "Create search index for this workspace"
                }
            }
        };
    }

    public RecoveryInfo GetDirectoryNotFoundRecovery(string path)
    {
        return new RecoveryInfo
        {
            Steps = new List<string>
            {
                $"Verify the directory path '{path}' exists",
                "Use absolute paths instead of relative paths",
                "Check for typos in the path",
                "Use LS tool to explore available directories"
            },
            SuggestedActions = new List<SuggestedAction>
            {
                new SuggestedAction
                {
                    Tool = "LS",
                    Params = new Dictionary<string, object> { ["path"] = System.IO.Path.GetDirectoryName(path) ?? "/" },
                    Description = "List contents of parent directory"
                }
            }
        };
    }

    public RecoveryInfo GetFileNotFoundRecovery(string path)
    {
        return new RecoveryInfo
        {
            Steps = new List<string>
            {
                $"Verify the file path '{path}' exists",
                "Use file_search to find the file by name",
                "Check for typos in the path",
                "Ensure you're using the correct file extension"
            },
            SuggestedActions = new List<SuggestedAction>
            {
                new SuggestedAction
                {
                    Tool = "file_search",
                    Params = new Dictionary<string, object> 
                    { 
                        ["nameQuery"] = System.IO.Path.GetFileName(path),
                        ["workspacePath"] = System.IO.Path.GetDirectoryName(path) ?? "/"
                    },
                    Description = "Search for the file by name"
                }
            }
        };
    }

    public RecoveryInfo GetValidationErrorRecovery(string fieldName, string expectedFormat)
    {
        return new RecoveryInfo
        {
            Steps = new List<string>
            {
                $"Check the '{fieldName}' parameter format",
                $"Expected format: {expectedFormat}",
                "Review the tool's parameter documentation",
                "Ensure all required parameters are provided"
            },
            SuggestedActions = new List<SuggestedAction>()
        };
    }

    public RecoveryInfo GetCircuitBreakerOpenRecovery(string operation)
    {
        return new RecoveryInfo
        {
            Steps = new List<string>
            {
                $"The {operation} circuit breaker is open due to repeated failures",
                "Wait 30 seconds for the circuit breaker to reset",
                "Check system health with system_health_check tool",
                "Review recent errors in log files"
            },
            SuggestedActions = new List<SuggestedAction>
            {
                new SuggestedAction
                {
                    Tool = "system_health_check",
                    Params = new Dictionary<string, object>(),
                    Description = "Check overall system health"
                },
                new SuggestedAction
                {
                    Tool = "log_diagnostics",
                    Params = new Dictionary<string, object> { ["action"] = "list" },
                    Description = "View recent log files for errors"
                }
            }
        };
    }
}