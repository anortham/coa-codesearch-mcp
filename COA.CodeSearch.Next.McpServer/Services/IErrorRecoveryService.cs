using COA.CodeSearch.Next.McpServer.Models;

namespace COA.CodeSearch.Next.McpServer.Services;

/// <summary>
/// Provides standardized error recovery guidance for common error scenarios
/// </summary>
public interface IErrorRecoveryService
{
    RecoveryInfo GetIndexNotFoundRecovery(string workspacePath);
    RecoveryInfo GetDirectoryNotFoundRecovery(string path);
    RecoveryInfo GetFileNotFoundRecovery(string path);
    RecoveryInfo GetValidationErrorRecovery(string fieldName, string expectedFormat);
    RecoveryInfo GetCircuitBreakerOpenRecovery(string operation);
}