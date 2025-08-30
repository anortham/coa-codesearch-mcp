using COA.CodeSearch.McpServer.Services.TypeExtraction;

namespace COA.CodeSearch.McpServer.Models;

public class StoredTypeInfo
{
    public List<TypeInfo> Types { get; set; } = new();
    public List<MethodInfo> Methods { get; set; } = new();
    public string? Language { get; set; }
}