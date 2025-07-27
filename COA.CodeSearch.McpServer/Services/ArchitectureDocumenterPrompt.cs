using COA.Mcp.Protocol;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Prompt template for auto-generating architecture documentation from codebase analysis.
/// Creates comprehensive architectural documentation including diagrams and data flows.
/// </summary>
public class ArchitectureDocumenterPrompt : BasePromptTemplate
{
    public override string Name => "architecture-documenter";

    public override string Description => "Generate comprehensive architecture documentation from codebase analysis with diagrams and data flows";

    public override List<PromptArgument> Arguments => new()
    {
        new PromptArgument
        {
            Name = "workspace_path",
            Description = "Path to the workspace or project to document",
            Required = true
        },
        new PromptArgument
        {
            Name = "entry_points",
            Description = "Main application entry points (e.g., 'Program.cs,Startup.cs' or 'main.ts,app.module.ts')",
            Required = false
        },
        new PromptArgument
        {
            Name = "diagram_types",
            Description = "Types of diagrams: component, service, data_flow, deployment, all",
            Required = false
        },
        new PromptArgument
        {
            Name = "detail_level",
            Description = "Documentation detail level: overview, detailed, comprehensive",
            Required = false
        },
        new PromptArgument
        {
            Name = "output_format",
            Description = "Output format: markdown, mermaid, plantuml, all",
            Required = false
        }
    };

    public override async Task<GetPromptResult> RenderAsync(Dictionary<string, object>? arguments = null, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Satisfy async requirement
        
        var workspacePath = GetRequiredArgument<string>(arguments, "workspace_path");
        var entryPoints = GetOptionalArgument<string>(arguments, "entry_points", "auto-detect");
        var diagramTypes = GetOptionalArgument<string>(arguments, "diagram_types", "component,service,data_flow");
        var detailLevel = GetOptionalArgument<string>(arguments, "detail_level", "detailed");
        var outputFormat = GetOptionalArgument<string>(arguments, "output_format", "markdown");

        var messages = new List<PromptMessage>
        {
            CreateSystemMessage(SubstituteVariables("""
                You are an expert software architect specializing in reverse engineering and documenting system architecture.
                Generate comprehensive architecture documentation for: {{workspace_path}}
                
                Entry Points: {{entry_points}}
                Diagram Types: {{diagram_types}}
                Detail Level: {{detail_level}}
                Output Format: {{output_format}}
                
                Follow this systematic architecture documentation workflow:
                
                **Phase 1: System Discovery & Entry Point Analysis**
                1. Use search_assistant to discover system structure:
                   - Find main entry points (Program.cs, Startup.cs, main.ts, etc.)
                   - Identify configuration files
                   - Map project/module structure
                
                2. Use file_search and directory_search to understand organization:
                   - Find key directories (Controllers, Services, Models, etc.)
                   - Identify architectural layers
                   - Locate configuration and deployment files
                
                **Phase 2: Component & Service Mapping**
                1. Use pattern_detector to identify architectural patterns:
                   - Dependency Injection patterns
                   - Service layer patterns
                   - Data access patterns
                   - Communication patterns
                
                2. Use search_assistant to map service dependencies:
                   - Find service registrations
                   - Map interface implementations
                   - Identify cross-cutting concerns
                
                3. Create ArchitecturalDecision memories for key patterns found
                
                **Phase 3: Data Flow Analysis**
                1. Trace request/data flows through the system:
                   - API endpoints to business logic
                   - Data persistence patterns
                   - External service integrations
                
                2. Use text_search to find:
                   - Database schemas and models
                   - API contracts and DTOs
                   - Message/event patterns
                
                **Phase 4: External Dependencies & Integrations**
                1. Identify external dependencies:
                   - Package.json, *.csproj, requirements.txt analysis
                   - Database connections
                   - External API integrations
                   - Third-party services
                
                **Phase 5: Documentation Generation**
                1. Create comprehensive architecture overview:
                   - System purpose and scope
                   - Key architectural decisions
                   - Technology stack
                   - Deployment architecture
                
                2. Generate component diagrams (Mermaid format):
                   ```mermaid
                   graph TD
                       A[Client] --> B[API Gateway]
                       B --> C[Service Layer]
                       C --> D[Data Layer]
                   ```
                
                3. Create service interaction diagrams:
                   - Service-to-service communication
                   - Database interactions
                   - External service calls
                
                4. Document data flows:
                   - Request/response patterns
                   - Event flows
                   - Data transformation points
                
                **Phase 6: Best Practices & Recommendations**
                1. Identify architectural strengths and areas for improvement
                2. Document discovered patterns and anti-patterns
                3. Provide recommendations for architectural evolution
                
                **Documentation Sections to Generate:**
                
                **System Overview**
                - Purpose and scope
                - Key features and capabilities
                - Target users and use cases
                
                **Architecture Overview**
                - High-level architecture diagram
                - Architectural patterns used
                - Technology stack
                - Key design decisions
                
                **Component Architecture**
                - Major components and their responsibilities
                - Component interaction diagrams
                - Dependency relationships
                
                **Service Architecture** (if applicable)
                - Service boundaries and responsibilities
                - Inter-service communication patterns
                - Service discovery and configuration
                
                **Data Architecture**
                - Data models and schemas
                - Data flow diagrams
                - Persistence patterns
                - Caching strategies
                
                **Security Architecture**
                - Authentication and authorization patterns
                - Security boundaries
                - Data protection measures
                
                **Deployment Architecture**
                - Deployment topology
                - Infrastructure requirements
                - Scaling considerations
                
                **Integration Points**
                - External dependencies
                - API specifications
                - Event/message patterns
                
                **Quality Attributes**
                - Performance characteristics
                - Scalability patterns
                - Reliability measures
                - Maintainability aspects
                
                Store all findings as ArchitecturalDecision memories with proper relationships.
                """, arguments)),

            CreateUserMessage(SubstituteVariables("""
                I need comprehensive architecture documentation for: {{workspace_path}}
                {{#if entry_points}}Entry points to analyze: {{entry_points}}{{/if}}
                Diagram types needed: {{diagram_types}}
                Detail level: {{detail_level}}
                Output format: {{output_format}}
                
                Please analyze the codebase and generate complete architecture documentation. Start by discovering the overall system structure and identifying the main architectural patterns.
                
                What's the first step to understand this system's architecture?
                """, arguments))
        };

        return new GetPromptResult
        {
            Description = $"Architecture documentation generator for {workspacePath}",
            Messages = messages
        };
    }

    /// <summary>
    /// Simple template variable substitution supporting {{variable}} and {{#if variable}} blocks.
    /// </summary>
    private string SubstituteVariables(string template, Dictionary<string, object>? arguments = null)
    {
        if (string.IsNullOrEmpty(template) || arguments == null)
        {
            return template;
        }

        var result = base.SubstituteVariables(template, arguments);

        // Handle simple conditional blocks {{#if variable}}content{{/if}}
        while (true)
        {
            var ifStart = result.IndexOf("{{#if ");
            if (ifStart == -1) break;

            var ifVarEnd = result.IndexOf("}}", ifStart);
            if (ifVarEnd == -1) break;

            var varName = result.Substring(ifStart + 6, ifVarEnd - ifStart - 6).Trim();
            var contentStart = ifVarEnd + 2;
            var ifEnd = result.IndexOf("{{/if}}", contentStart);
            if (ifEnd == -1) break;

            var content = result.Substring(contentStart, ifEnd - contentStart);
            var replacement = "";

            if (arguments.TryGetValue(varName, out var value) && 
                value != null && 
                !string.IsNullOrEmpty(value.ToString()))
            {
                replacement = content;
            }

            result = result.Substring(0, ifStart) + replacement + result.Substring(ifEnd + 7);
        }

        return result;
    }
}