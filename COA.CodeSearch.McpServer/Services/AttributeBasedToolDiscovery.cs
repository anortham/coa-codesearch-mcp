using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using COA.CodeSearch.McpServer.Attributes;
using COA.CodeSearch.McpServer.Constants;
using COA.Mcp.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services
{
    /// <summary>
    /// Discovers and registers MCP tools based on attributes, mirroring the official SDK's behavior.
    /// This allows gradual migration from manual registration to attribute-based discovery.
    /// </summary>
    public class AttributeBasedToolDiscovery
    {
        private readonly ILogger<AttributeBasedToolDiscovery> _logger;
        private readonly IServiceProvider _serviceProvider;

        public AttributeBasedToolDiscovery(
            ILogger<AttributeBasedToolDiscovery> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Discovers all tools marked with MCP attributes and registers them with the tool registry.
        /// </summary>
        public void DiscoverAndRegisterTools(ToolRegistry toolRegistry, Assembly? assembly = null)
        {
            assembly ??= Assembly.GetExecutingAssembly();
            _logger.LogInformation("Starting attribute-based tool discovery in assembly: {AssemblyName}", assembly.GetName().Name);

            var toolTypes = assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
                .ToList();

            _logger.LogInformation("Found {Count} classes marked with [McpServerToolType]", toolTypes.Count);

            foreach (var toolType in toolTypes)
            {
                try
                {
                    RegisterToolsFromType(toolRegistry, toolType);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to register tools from type {TypeName}", toolType.Name);
                }
            }
        }

        private void RegisterToolsFromType(ToolRegistry toolRegistry, Type toolType)
        {
            // Get all methods marked with [McpServerTool]
            var toolMethods = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
                .ToList();

            if (toolMethods.Count == 0)
            {
                _logger.LogWarning("Class {TypeName} is marked with [McpServerToolType] but has no methods marked with [McpServerTool]", 
                    toolType.Name);
                return;
            }

            // Get or create instance of the tool class if needed
            object? toolInstance = null;
            if (toolMethods.Any(m => !m.IsStatic))
            {
                toolInstance = _serviceProvider.GetService(toolType);
                if (toolInstance == null)
                {
                    _logger.LogError("Could not resolve service for type {TypeName}. Make sure it's registered in DI.", toolType.Name);
                    return;
                }
            }

            foreach (var method in toolMethods)
            {
                RegisterToolMethod(toolRegistry, toolType, method, toolInstance);
            }
        }

        private void RegisterToolMethod(ToolRegistry toolRegistry, Type toolType, MethodInfo method, object? toolInstance)
        {
            var toolAttribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
            var descriptionAttribute = method.GetCustomAttribute<DescriptionAttribute>();

            // Determine tool name - use attribute name or fall back to method name
            var toolName = toolAttribute.Name ?? ConvertMethodNameToToolName(method.Name);
            
            // Get description from attribute or generate a default
            var description = descriptionAttribute?.Description ?? $"Executes {method.Name}";

            _logger.LogDebug("Registering tool: {ToolName} from {TypeName}.{MethodName}", 
                toolName, toolType.Name, method.Name);

            // Build JSON schema from method parameters
            var inputSchema = BuildInputSchema(method);

            // Create handler that invokes the method
            var handler = CreateHandler(method, toolInstance);

            // Skip if already registered (allows manual registration to take precedence)
            if (toolRegistry.IsToolRegistered(toolName))
            {
                _logger.LogDebug("Tool '{ToolName}' already registered, skipping attribute-based registration", toolName);
                return;
            }

            // Register with existing ToolRegistry
            toolRegistry.RegisterTool(toolName, description, inputSchema, handler);

            _logger.LogInformation("Registered tool '{ToolName}' from {TypeName}.{MethodName}", 
                toolName, toolType.Name, method.Name);
        }

        private object BuildInputSchema(MethodInfo method)
        {
            var parameters = method.GetParameters()
                .Where(p => !IsSpecialParameter(p))
                .ToList();

            if (parameters.Count == 0)
            {
                // No parameters - simple schema
                return new
                {
                    type = "object",
                    properties = new { }
                };
            }

            if (parameters.Count == 1 && IsComplexType(parameters[0].ParameterType))
            {
                // Single complex parameter - use its properties as schema
                return BuildSchemaFromType(parameters[0].ParameterType);
            }

            // Multiple parameters or simple types - create schema from parameters
            var properties = new Dictionary<string, object>();
            var required = new List<string>();

            foreach (var param in parameters)
            {
                var paramSchema = BuildParameterSchema(param);
                properties[param.Name!] = paramSchema;

                if (!param.HasDefaultValue && param.ParameterType.IsValueType 
                    && Nullable.GetUnderlyingType(param.ParameterType) == null)
                {
                    required.Add(param.Name!);
                }
            }

            var schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties
            };

            if (required.Count > 0)
            {
                schema["required"] = required;
            }

            return schema;
        }

        private object BuildSchemaFromType(Type type)
        {
            var properties = new Dictionary<string, object>();
            var required = new List<string>();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propSchema = BuildPropertySchema(prop);
                properties[ConvertPropertyNameToJsonName(prop.Name)] = propSchema;

                // Check if property is required (no default value, not nullable)
                if (prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) == null)
                {
                    // You might want to check for Required attribute here
                    required.Add(ConvertPropertyNameToJsonName(prop.Name));
                }
            }

            var schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties
            };

            if (required.Count > 0)
            {
                schema["required"] = required;
            }

            return schema;
        }

        private object BuildParameterSchema(ParameterInfo parameter)
        {
            var schema = new Dictionary<string, object>();
            var paramType = parameter.ParameterType;

            // Handle nullable types
            var underlyingType = Nullable.GetUnderlyingType(paramType) ?? paramType;

            // Map C# types to JSON schema types
            if (underlyingType == typeof(string))
            {
                schema["type"] = "string";
            }
            else if (underlyingType == typeof(int) || underlyingType == typeof(long))
            {
                schema["type"] = "integer";
            }
            else if (underlyingType == typeof(float) || underlyingType == typeof(double) || underlyingType == typeof(decimal))
            {
                schema["type"] = "number";
            }
            else if (underlyingType == typeof(bool))
            {
                schema["type"] = "boolean";
            }
            else if (underlyingType.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(underlyingType))
            {
                schema["type"] = "array";
                // Could add items schema here if needed
            }
            else if (underlyingType.IsClass || underlyingType.IsInterface)
            {
                schema["type"] = "object";
                // Could recursively build nested schema here
            }

            // Add description if available
            var descAttr = parameter.GetCustomAttribute<DescriptionAttribute>();
            if (descAttr != null)
            {
                schema["description"] = descAttr.Description;
            }

            // Add default value if specified
            if (parameter.HasDefaultValue && parameter.DefaultValue != null)
            {
                schema["default"] = parameter.DefaultValue;
            }

            return schema;
        }

        private object BuildPropertySchema(PropertyInfo property)
        {
            var schema = new Dictionary<string, object>();
            var propType = property.PropertyType;

            // Handle nullable types
            var underlyingType = Nullable.GetUnderlyingType(propType) ?? propType;

            // Map C# types to JSON schema types
            if (underlyingType == typeof(string))
            {
                schema["type"] = "string";
            }
            else if (underlyingType == typeof(int) || underlyingType == typeof(long))
            {
                schema["type"] = "integer";
            }
            else if (underlyingType == typeof(float) || underlyingType == typeof(double) || underlyingType == typeof(decimal))
            {
                schema["type"] = "number";
            }
            else if (underlyingType == typeof(bool))
            {
                schema["type"] = "boolean";
            }
            else if (underlyingType.IsArray)
            {
                schema["type"] = "array";
                var elementType = underlyingType.GetElementType();
                if (elementType == typeof(string))
                {
                    schema["items"] = new { type = "string" };
                }
                // Add more array item types as needed
            }
            else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(underlyingType) && underlyingType != typeof(string))
            {
                schema["type"] = "array";
            }
            else if (underlyingType.IsClass || underlyingType.IsInterface)
            {
                schema["type"] = "object";
            }

            // Add description if available
            var descAttr = property.GetCustomAttribute<DescriptionAttribute>();
            if (descAttr != null)
            {
                schema["description"] = descAttr.Description;
            }

            return schema;
        }

        private Func<JsonElement?, CancellationToken, Task<CallToolResult>> CreateHandler(
            MethodInfo method, object? toolInstance)
        {
            return async (args, ct) =>
            {
                try
                {
                    // Parse and prepare parameters
                    var methodParams = method.GetParameters();
                    var invokeArgs = new object?[methodParams.Length];

                    // Handle different parameter patterns
                    if (methodParams.Length == 0)
                    {
                        // No parameters - just invoke
                    }
                    else if (methodParams.Length == 1 && methodParams[0].ParameterType == typeof(CancellationToken))
                    {
                        // Only CancellationToken parameter
                        invokeArgs[0] = ct;
                    }
                    else if (methodParams.Length == 1 && IsComplexType(methodParams[0].ParameterType))
                    {
                        // Single complex parameter - deserialize entire args to it
                        if (args.HasValue)
                        {
                            var json = args.Value.GetRawText();
                            invokeArgs[0] = JsonSerializer.Deserialize(json, methodParams[0].ParameterType,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                    }
                    else
                    {
                        // Multiple parameters - map from JSON properties
                        if (args.HasValue)
                        {
                            for (int i = 0; i < methodParams.Length; i++)
                            {
                                var param = methodParams[i];
                                if (param.ParameterType == typeof(CancellationToken))
                                {
                                    invokeArgs[i] = ct;
                                }
                                else if (args.Value.TryGetProperty(param.Name!, out var propValue))
                                {
                                    var json = propValue.GetRawText();
                                    invokeArgs[i] = JsonSerializer.Deserialize(json, param.ParameterType,
                                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                }
                                else if (param.HasDefaultValue)
                                {
                                    invokeArgs[i] = param.DefaultValue;
                                }
                            }
                        }
                    }

                    // Invoke the method
                    var task = method.Invoke(toolInstance, invokeArgs) as Task;
                    if (task == null)
                    {
                        throw new InvalidOperationException($"Method {method.Name} must return a Task");
                    }

                    await task;

                    // Extract result
                    object? result = null;
                    if (task.GetType().IsGenericType)
                    {
                        var resultProperty = task.GetType().GetProperty("Result");
                        result = resultProperty?.GetValue(task);
                    }

                    // Convert result to CallToolResult
                    return ConvertToCallToolResult(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing tool method {MethodName}", method.Name);
                    return new CallToolResult
                    {
                        IsError = true,
                        Content = new List<ToolContent>
                        {
                            new ToolContent
                            {
                                Type = "text",
                                Text = $"Error: {ex.Message}"
                            }
                        }
                    };
                }
            };
        }

        private CallToolResult ConvertToCallToolResult(object? result)
        {
            if (result == null)
            {
                return new CallToolResult
                {
                    Content = new List<ToolContent>
                    {
                        new ToolContent
                        {
                            Type = "text",
                            Text = "null"
                        }
                    }
                };
            }

            if (result is CallToolResult toolResult)
            {
                return toolResult;
            }

            // Serialize result to JSON
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return new CallToolResult
            {
                Content = new List<ToolContent>
                {
                    new ToolContent
                    {
                        Type = "text",
                        Text = json
                    }
                }
            };
        }

        private bool IsSpecialParameter(ParameterInfo parameter)
        {
            // Parameters that shouldn't be included in the schema
            return parameter.ParameterType == typeof(CancellationToken) ||
                   parameter.ParameterType == typeof(IServiceProvider) ||
                   parameter.GetCustomAttribute<FromServicesAttribute>() != null;
        }

        private bool IsComplexType(Type type)
        {
            return type.IsClass && 
                   type != typeof(string) && 
                   !type.IsArray &&
                   !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
        }

        private string ConvertMethodNameToToolName(string methodName)
        {
            // Convert from PascalCase to snake_case
            // e.g., "GetVersion" -> "get_version"
            var result = string.Concat(methodName.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + x : x.ToString()));
            return result.ToLowerInvariant();
        }

        private string ConvertPropertyNameToJsonName(string propertyName)
        {
            // Convert from PascalCase to camelCase for JSON
            if (string.IsNullOrEmpty(propertyName))
                return propertyName;

            return char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
        }
    }

    /// <summary>
    /// Marks a parameter as coming from dependency injection services.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class FromServicesAttribute : Attribute { }
}