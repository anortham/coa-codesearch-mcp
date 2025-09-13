using System.Diagnostics;

namespace DebugMcpClient;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Debug MCP Client - Starting CodeSearch Server for debugging");
        
        try
        {
            // Path to the CodeSearch server executable
            var serverPath = Path.Combine(Directory.GetCurrentDirectory(), 
                "..", "COA.CodeSearch.McpServer", "bin", "Debug", "net9.0", "COA.CodeSearch.McpServer.exe");
            
            Console.WriteLine($"Server path: {serverPath}");
            
            if (!File.Exists(serverPath))
            {
                Console.WriteLine("ERROR: Server executable not found. Please build the project first.");
                return;
            }
            
            // Start the server process exactly like Claude Code would - no arguments, STDIO mode
            Console.WriteLine("Starting server process in STDIO mode (like Claude Code)...");
            var startInfo = new ProcessStartInfo
            {
                FileName = serverPath,
                Arguments = "", // No arguments - STDIO mode like Claude Code
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false // Keep window visible for debugging
            };
            
            using var serverProcess = Process.Start(startInfo);
            
            if (serverProcess == null)
            {
                Console.WriteLine("Failed to start server process");
                return;
            }
            
            Console.WriteLine($"Server started with PID: {serverProcess.Id}");
            Console.WriteLine("ATTACH DEBUGGER TO THE SERVER PROCESS NOW IF NEEDED!");
            Console.WriteLine("The server will now run through its startup sequence...");
            Console.WriteLine("Press any key to send MCP initialize message...");
            Console.ReadKey();
            
            Console.WriteLine("Sending MCP initialize message (like Claude Code would)...");
            
            // Send MCP initialize message exactly like Claude Code would
            var initMessage = """
                {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2024-11-05", "capabilities": {"roots": {"listChanged": true}, "sampling": {}}, "clientInfo": {"name": "claude-code", "version": "1.0.0"}}}
                
                """;
            
            await serverProcess.StandardInput.WriteAsync(initMessage);
            await serverProcess.StandardInput.FlushAsync();
            
            Console.WriteLine("Initialize message sent. Reading response...");
            
            // Read the response (with timeout)
            var responseTask = serverProcess.StandardOutput.ReadLineAsync();
            var timeoutTask = Task.Delay(10000); // 10 second timeout
            
            var completedTask = await Task.WhenAny(responseTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                Console.WriteLine("TIMEOUT: Server did not respond within 10 seconds");
                Console.WriteLine("This confirms the server is hanging during startup");
            }
            else
            {
                var response = await responseTask;
                Console.WriteLine($"Server responded: {response}");
            }
            
            Console.WriteLine("Press any key to kill server...");
            Console.ReadKey();
            
            if (!serverProcess.HasExited)
            {
                serverProcess.Kill();
            }
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}
