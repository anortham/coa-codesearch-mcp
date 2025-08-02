using COA.CodeSearch.McpServer.Models;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Minimal interface for memory service operations used by MemoryLifecycleService
/// </summary>
public interface IMemoryService
{
    Task<FlexibleMemorySearchResult> SearchMemoriesAsync(FlexibleMemorySearchRequest request);
    Task<bool> UpdateMemoryAsync(MemoryUpdateRequest request);
    Task<bool> StoreMemoryAsync(FlexibleMemoryEntry memory);
    Task<bool> DeleteMemoryAsync(string memoryId);
}