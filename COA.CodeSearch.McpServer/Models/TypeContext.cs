using COA.CodeSearch.McpServer.Services.TypeExtraction;

namespace COA.CodeSearch.McpServer.Models;

public class TypeContext
{
    public string? ContainingType { get; set; }
    public List<TypeInfo> NearbyTypes { get; set; } = new();
    public List<MethodInfo> NearbyMethods { get; set; } = new();
    public string? Language { get; set; }
}