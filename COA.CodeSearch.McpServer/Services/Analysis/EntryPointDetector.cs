using COA.CodeSearch.McpServer.Models;

namespace COA.CodeSearch.McpServer.Services.Analysis;

/// <summary>
/// Detects entry points in code to stop call tracing at logical boundaries
/// </summary>
public static class EntryPointDetector
{
    /// <summary>
    /// Common entry point method names
    /// </summary>
    private static readonly HashSet<string> EntryPointMethodNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Application entry points
        "Main", "main", "Program.Main", "Main(", "main(",
        
        // ASP.NET Core entry points
        "Configure", "ConfigureServices", "Startup", "CreateHostBuilder", "CreateHost",
        "UseStartup", "Run", "RunAsync", "Start", "StartAsync",
        
        // Web API controller actions
        "OnGet", "OnPost", "OnPut", "OnDelete", "OnPatch",
        "Get", "Post", "Put", "Delete", "Patch", "Head", "Options",
        "Index", "Create", "Edit", "Details", "Remove",
        
        // Job and task entry points
        "Execute", "ExecuteAsync", "Run", "RunAsync", "Start", "StartAsync",
        "Process", "ProcessAsync", "Handle", "HandleAsync",
        
        // Event handlers
        "Handle", "HandleAsync", "OnClick", "OnLoad", "OnInitialized",
        "OnStart", "OnStop", "OnError", "OnComplete",
        
        // Test methods
        "Test", "TestAsync", "Setup", "SetupAsync", "TearDown", "TearDownAsync",
        "BeforeEach", "AfterEach", "BeforeAll", "AfterAll",
        
        // Framework callbacks
        "Application_Start", "Application_End", "Session_Start", "Session_End",
        "Page_Load", "Page_Init", "Page_PreRender",
        
        // Constructors and initializers
        "Initialize", "InitializeAsync", "Init", "InitAsync",
        "Constructor", "ctor", ".ctor", "static",
        
        // Mobile/Desktop app entry points
        "OnCreate", "OnDestroy", "OnResume", "OnPause",
        "ViewDidLoad", "ViewWillAppear", "ViewDidAppear",
        "Application", "AppDelegate",
        
        // Microservice entry points
        "Invoke", "InvokeAsync", "ProcessMessage", "ProcessMessageAsync",
        "HandleRequest", "HandleRequestAsync"
    };

    /// <summary>
    /// HTTP method attributes that indicate controller actions
    /// </summary>
    private static readonly HashSet<string> HttpMethodAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch", "HttpHead", "HttpOptions",
        "Get", "Post", "Put", "Delete", "Patch", "Head", "Options",
        "Route", "ActionName"
    };

    /// <summary>
    /// Test framework attributes
    /// </summary>
    private static readonly HashSet<string> TestAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Test", "TestMethod", "Fact", "Theory", "TestCase", "TestCaseSource",
        "SetUp", "TearDown", "OneTimeSetUp", "OneTimeTearDown",
        "Before", "After", "BeforeClass", "AfterClass",
        "BeforeEach", "AfterEach", "BeforeAll", "AfterAll"
    };

    /// <summary>
    /// Framework entry point attributes
    /// </summary>
    private static readonly HashSet<string> EntryPointAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "STAThread", "MTAThread", "DllImport", "ComVisible",
        "EntryPoint", "WebMethod", "Page", "Handler",
        "Command", "CommandHandler", "EventHandler"
    };

    /// <summary>
    /// Determines if a method is an entry point
    /// </summary>
    /// <param name="method">Method information to check</param>
    /// <returns>True if the method is an entry point</returns>
    public static bool IsEntryPoint(MethodInfo method)
    {
        if (method == null) return false;

        // Check if already marked as entry point
        if (method.IsEntryPoint) return true;

        // Check method name patterns
        if (IsEntryPointMethodName(method.Name)) return true;

        // Check for HTTP method attributes (controller actions)
        if (HasHttpMethodAttribute(method.Attributes)) return true;

        // Check for test attributes
        if (HasTestAttribute(method.Attributes)) return true;

        // Check for other entry point attributes
        if (HasEntryPointAttribute(method.Attributes)) return true;

        // Check for Main method pattern
        if (IsMainMethod(method)) return true;

        // Check for event handler pattern
        if (IsEventHandler(method)) return true;

        // Check for constructor of startup/program classes
        if (IsStartupConstructor(method)) return true;

        return false;
    }

    /// <summary>
    /// Determines if a method name indicates an entry point
    /// </summary>
    private static bool IsEntryPointMethodName(string methodName)
    {
        if (string.IsNullOrEmpty(methodName)) return false;

        // Direct match
        if (EntryPointMethodNames.Contains(methodName)) return true;

        // Pattern matching for common variations
        var lowerName = methodName.ToLowerInvariant();

        // Event handler patterns
        if (lowerName.EndsWith("_click") || 
            lowerName.EndsWith("_load") || 
            lowerName.EndsWith("_changed") ||
            lowerName.EndsWith("eventhandler") ||
            lowerName.EndsWith("handler"))
            return true;

        // Test method patterns
        if (lowerName.StartsWith("test") || 
            lowerName.EndsWith("test") ||
            lowerName.StartsWith("should") ||
            lowerName.StartsWith("when") ||
            lowerName.StartsWith("given"))
            return true;

        // Command/action patterns
        if (lowerName.EndsWith("command") || 
            lowerName.EndsWith("action") ||
            lowerName.EndsWith("controller"))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if method has HTTP method attributes
    /// </summary>
    private static bool HasHttpMethodAttribute(List<string> attributes)
    {
        return attributes.Any(attr => HttpMethodAttributes.Any(http => 
            attr.Contains(http, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Checks if method has test framework attributes
    /// </summary>
    private static bool HasTestAttribute(List<string> attributes)
    {
        return attributes.Any(attr => TestAttributes.Any(test => 
            attr.Contains(test, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Checks if method has entry point attributes
    /// </summary>
    private static bool HasEntryPointAttribute(List<string> attributes)
    {
        return attributes.Any(attr => EntryPointAttributes.Any(entry => 
            attr.Contains(entry, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Checks if method is a Main method
    /// </summary>
    private static bool IsMainMethod(MethodInfo method)
    {
        if (!string.Equals(method.Name, "Main", StringComparison.OrdinalIgnoreCase))
            return false;

        // Main methods are typically static
        return method.IsStatic || method.Modifiers.Any(m => 
            string.Equals(m, "static", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks if method is an event handler
    /// </summary>
    private static bool IsEventHandler(MethodInfo method)
    {
        // Event handlers typically have 'sender' and 'EventArgs' parameters
        if (method.Parameters.Count == 2)
        {
            var parameters = method.Parameters;
            return parameters[0].Contains("sender", StringComparison.OrdinalIgnoreCase) ||
                   parameters[1].Contains("EventArgs", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Checks if method is a constructor of a startup/program class
    /// </summary>
    private static bool IsStartupConstructor(MethodInfo method)
    {
        if (!method.IsConstructor) return false;

        var className = method.ClassName?.ToLowerInvariant();
        return className != null && (
            className.Contains("startup") ||
            className.Contains("program") ||
            className.Contains("application") ||
            className.Contains("app"));
    }

    /// <summary>
    /// Gets a description of why a method is considered an entry point
    /// </summary>
    /// <param name="method">Method to analyze</param>
    /// <returns>Reason description or null if not an entry point</returns>
    public static string? GetEntryPointReason(MethodInfo method)
    {
        if (!IsEntryPoint(method)) return null;

        if (IsMainMethod(method)) return "Application entry point (Main method)";
        if (HasHttpMethodAttribute(method.Attributes)) return "Web API endpoint";
        if (HasTestAttribute(method.Attributes)) return "Test method";
        if (IsEventHandler(method)) return "Event handler";
        if (IsStartupConstructor(method)) return "Application startup";
        if (IsEntryPointMethodName(method.Name)) return "Framework entry point";

        return "Entry point method";
    }
}