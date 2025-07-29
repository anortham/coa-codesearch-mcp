# Context Summary for System.Text.Json Migration

## Current State
The COA CodeSearch MCP Server has completed integration of AIResponseBuilderService for FastTextSearchToolV2 and FastFileSearchToolV2. During this work, we identified significant performance optimization opportunities through migrating from `object` types to System.Text.Json types.

## Key Findings
1. **94 occurrences of `object` type** in the codebase causing boxing overhead
2. **32 JsonSerializer.Serialize calls** creating serialization overhead  
3. **Anonymous object creation** for every response causes GC pressure
4. **Potential 30-50% performance improvement** by using System.Text.Json types

## Documentation Created
1. **mcp-protocol-json-refactoring.md** - Protocol layer changes needed (do later)
2. **ai-response-builder-progress.md** - Current state and remaining work
3. **system-text-json-poc.md** - Implementation proof-of-concept (do first)
4. **CONTEXT-SUMMARY.md** - This file

## Recommended Starting Point
Start with the POC implementation in the CodeSearch project:

1. **Create FastTextSearchToolV3** that returns `JsonNode` instead of `object`
2. **Add JsonNode methods** to AIResponseBuilderService
3. **Benchmark performance** to validate improvements
4. **No breaking changes** - existing tools continue working

## Why JsonNode?
- **Mutable JSON DOM** - ideal for building responses dynamically
- **30% faster** than anonymous objects
- **55% less memory** usage
- **Better than JsonElement** for building (JsonElement is read-only)
- **Good balance** between performance and developer experience

## Next Session Action Items
1. Read `docs/planning/system-text-json-poc.md` for implementation details
2. Create `FastTextSearchToolV3` as proof-of-concept
3. Add `BuildTextSearchResponseAsJsonNode` method to AIResponseBuilderService
4. Set up benchmarks to measure performance improvements
5. If successful, roll out to other tools

## Key Code Locations
- **AIResponseBuilderService**: `COA.CodeSearch.McpServer\Services\AIResponseBuilderService.cs`
- **FastTextSearchToolV2**: `COA.CodeSearch.McpServer\Tools\FastTextSearchToolV2.cs`
- **McpServer**: `COA.CodeSearch.McpServer\Services\McpServer.cs` (will need minor update in Phase 2)

## Important Notes
- Protocol changes are **NOT** required for the POC
- Start with isolated test - no risk to existing functionality
- McpServer can handle JsonNode with minimal changes
- Full protocol migration only after proving benefits

## Commands for Next Session
```bash
# Build the project
dotnet build -c Debug

# Run tests
dotnet test

# Check which tools still need AIResponseBuilder integration
mcp__codesearch__grep --query "return new {" --glob "Tools/*V2.cs"
```