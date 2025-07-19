# 🚀 TypeScript Language Support Implementation Plan

*Generated on 2025-01-19 - Roadmap for adding TypeScript support to COA CodeSearch MCP*

## 🎯 Goal
Add TypeScript support to enable unified code intelligence across C# backend and TypeScript/Vue frontend codebases, perfect for hospital web applications.

## 📋 Implementation Phases

### Phase 1: TypeScript Language Service Integration (Week 1-2)

#### 1.1 Core Service Architecture
```csharp
// New service: Services/TypeScriptAnalysisService.cs
public class TypeScriptAnalysisService : IDisposable
{
    // Wraps Microsoft's TypeScript Language Service
    // Manages tsconfig.json projects
    // Provides semantic analysis for .ts, .tsx, .vue files
}
```

**Key Dependencies** (All FREE):
- TypeScript Compiler API (via Node.js interop)
- Optional: OmniSharp's TypeScript support
- Alternative: ts-morph .NET bindings

#### 1.2 Project Detection
- Detect `tsconfig.json` files
- Support for Vue Single File Components (.vue)
- Handle JavaScript files with TypeScript checking

### Phase 2: Unified MCP Tools (Week 3-4)

#### 2.1 Cross-Language Navigation
```csharp
[MCP Tool] find_references_cross_language
- Find C# DTO usage in TypeScript
- Find TypeScript interface usage in C#
- API contract validation
```

#### 2.2 Enhanced Tools
- **go_to_definition**: Jump between C# controllers and TS clients
- **find_references**: Track interface usage across languages
- **rename_symbol**: Rename DTOs in both C# and TypeScript

#### 2.3 New TypeScript-Specific Tools
- **extract_vue_component**: Analyze Vue components
- **validate_api_contracts**: Ensure C# DTOs match TS interfaces
- **find_unused_imports**: TypeScript-specific cleanup

### Phase 3: Hospital-Specific Features (Week 5-6)

#### 3.1 Healthcare API Patterns
```typescript
// Detect and validate common patterns:
interface PatientDto {
    medicalRecordNumber: string;  // Auto-validate HIPAA compliance
    dateOfBirth: Date;           // Check date handling
    medications: MedicationDto[]; // Ensure proper array handling
}
```

#### 3.2 Security Validations
- HIPAA field detection in TypeScript
- Authentication token handling
- Secure API endpoint validation

#### 3.3 Vue + Blazor Integration
- Shared component detection
- State management patterns
- Cross-framework navigation

## 🏗️ Technical Architecture

### Service Integration Pattern
```
┌─────────────────────────┬─────────────────────────┐
│   C# Analysis           │  TypeScript Analysis    │
│   (Roslyn)              │  (TS Language Service)  │
├─────────────────────────┼─────────────────────────┤
│ CodeAnalysisService     │ TypeScriptService       │
│ • MSBuildWorkspace      │ • TSConfig parsing      │
│ • Semantic models       │ • Type checking         │
│ • Symbol analysis       │ • Module resolution     │
└─────────────────────────┴─────────────────────────┘
                 │                    │
                 └────────┬───────────┘
                          │
                   Unified MCP API
```

### Lucene Index Extension
```
Current: .cs, .razor, .cshtml, .json, .xml
Add: .ts, .tsx, .js, .jsx, .vue, .d.ts
```

### Memory System Integration
- Store TS patterns: `remember_pattern "Vue Composition API for patient forms"`
- Track decisions: `remember_decision "Use TypeScript strict mode for HIPAA compliance"`
- Document mappings: `remember_pattern "PatientDto maps between C# and TypeScript"`

## 💰 Cost Analysis

**All Components FREE**:
- TypeScript Language Service: $0 (Microsoft OSS)
- Node.js interop: $0 (built into .NET)
- Vue parser: $0 (OSS libraries)
- No cloud services required

## 🎯 Success Metrics

1. **Navigation Speed**: <100ms for cross-language jumps
2. **Accuracy**: 100% accurate DTO↔Interface mapping
3. **Memory Usage**: <100MB additional for TS service
4. **Developer Velocity**: 50% faster full-stack navigation

## 📝 Implementation Checklist

### Week 1-2: Foundation
- [ ] Create TypeScriptAnalysisService
- [ ] Add Node.js interop for TS compiler
- [ ] Implement tsconfig.json detection
- [ ] Add .ts/.vue to Lucene indexing

### Week 3-4: Tools
- [ ] Implement cross-language find_references
- [ ] Add TypeScript go_to_definition
- [ ] Create API contract validation
- [ ] Test with hospital Vue components

### Week 5-6: Polish
- [ ] Add HIPAA compliance checks
- [ ] Implement Vue SFC support
- [ ] Performance optimization
- [ ] Documentation and examples

## 🏥 Hospital-Specific Value

1. **Unified Codebase Intelligence**: Navigate seamlessly between C# APIs and Vue frontends
2. **HIPAA Compliance**: Automatic detection of sensitive data patterns
3. **Reduced Errors**: Catch API contract mismatches before runtime
4. **Team Productivity**: Junior developers can understand full stack quickly

## 🚀 Quick Start (After Implementation)

```bash
# Index a full-stack hospital project
coa-codesearch-mcp index_workspace /path/to/hospital-app

# Find all references to PatientDto across C# and TypeScript
coa-codesearch-mcp find_references_cross_language PatientDto

# Validate API contracts
coa-codesearch-mcp validate_api_contracts /api/controllers /src/api-client
```

## 📌 Notes for Next Session

1. **Memory System Ready**: Use `recall_context "TypeScript implementation"` to load this plan
2. **Architecture Decision**: We chose Language Service over simple text parsing for semantic accuracy
3. **Priority**: Hospital needs TypeScript + Vue support for patient portal project
4. **Key Pattern**: Unified tools that understand both languages, not separate tools

---
*This plan is stored in both the project directory and Claude's memory system for persistence across sessions*