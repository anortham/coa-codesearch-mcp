using COA.CodeSearch.McpServer.Tools;

namespace COA.CodeSearch.McpServer.Tests.Tools;

// Test tools for unit testing
public class TestNavigationTool : ITool
{
    public string ToolName => "test_navigation";
    public string Description => "Test navigation tool";
    public ToolCategory Category => ToolCategory.Navigation;
}

public class TestSearchTool : ITool
{
    public string ToolName => "test_search";
    public string Description => "Test search tool";
    public ToolCategory Category => ToolCategory.Search;
}

public class TestMemoryTool : ITool
{
    public string ToolName => "test_memory";
    public string Description => "Test memory tool";
    public ToolCategory Category => ToolCategory.Memory;
}

public class TestAnalysisTool : ITool
{
    public string ToolName => "test_analysis";
    public string Description => "Test analysis tool";
    public ToolCategory Category => ToolCategory.Analysis;
}

public class TestInfrastructureTool : ITool
{
    public string ToolName => "test_infrastructure";
    public string Description => "Test infrastructure tool";
    public ToolCategory Category => ToolCategory.Infrastructure;
}

// Multiple tools for each category to ensure proper testing
public class TestNavigationTool2 : ITool
{
    public string ToolName => "test_navigation_2";
    public string Description => "Second test navigation tool";
    public ToolCategory Category => ToolCategory.Navigation;
}

public class TestSearchTool2 : ITool
{
    public string ToolName => "test_search_2";
    public string Description => "Second test search tool";
    public ToolCategory Category => ToolCategory.Search;
}

public class TestMemoryTool2 : ITool
{
    public string ToolName => "test_memory_2";
    public string Description => "Second test memory tool";
    public ToolCategory Category => ToolCategory.Memory;
}

public class TestAnalysisTool2 : ITool
{
    public string ToolName => "test_analysis_2";
    public string Description => "Second test analysis tool";
    public ToolCategory Category => ToolCategory.Analysis;
}

public class TestInfrastructureTool2 : ITool
{
    public string ToolName => "test_infrastructure_2";
    public string Description => "Second test infrastructure tool";
    public ToolCategory Category => ToolCategory.Infrastructure;
}

// One more to ensure we have more than 10 tools total
public class TestNavigationTool3 : ITool
{
    public string ToolName => "test_navigation_3";
    public string Description => "Third test navigation tool";
    public ToolCategory Category => ToolCategory.Navigation;
}