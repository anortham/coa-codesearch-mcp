using System;

namespace COA.CodeSearch.McpServer.Attributes
{
    /// <summary>
    /// Marks a class as containing MCP server tools.
    /// This attribute mirrors the official ModelContextProtocol SDK's attribute name exactly,
    /// allowing for easy migration by just changing the namespace import.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class McpServerToolTypeAttribute : Attribute 
    { 
    }

    /// <summary>
    /// Marks a method as an MCP server tool that can be invoked by clients.
    /// This attribute mirrors the official ModelContextProtocol SDK's attribute name exactly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class McpServerToolAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets the name of the tool as it will be exposed to MCP clients.
        /// If not specified, the method name will be used.
        /// </summary>
        public string? Name { get; set; }
    }

    /// <summary>
    /// Provides a human-readable description for tools and parameters.
    /// This uses the standard .NET DescriptionAttribute name that the SDK also recognizes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter | AttributeTargets.Property)]
    public class DescriptionAttribute : Attribute
    {
        /// <summary>
        /// Gets the description text.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Initializes a new instance of the DescriptionAttribute class.
        /// </summary>
        /// <param name="description">The description text.</param>
        public DescriptionAttribute(string description)
        {
            Description = description;
        }
    }

    /// <summary>
    /// Marks a method as an MCP server prompt.
    /// This attribute mirrors the official ModelContextProtocol SDK's attribute name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class McpServerPromptAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets the name of the prompt as it will be exposed to MCP clients.
        /// If not specified, the method name will be used.
        /// </summary>
        public string? Name { get; set; }
    }
}