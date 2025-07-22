# COA.Directus.Mcp.Protocol - TODO

This document outlines the improvements needed before making the Protocol project publicly available as a NuGet package for .NET developers building MCP servers.

## Missing MCP Protocol Features

The Protocol project is missing several key MCP types that need to be implemented for full protocol compliance:

### 1. Resource Operations
- [ ] `ReadResourceRequest` - for resources/read
- [ ] `ReadResourceResult` - with content types
- [ ] `SubscribeResourceRequest` - for resources/subscribe
- [ ] `UnsubscribeResourceRequest`
- [ ] `ResourceUpdatedNotification` - for resource change notifications

### 2. Prompt Operations
- [ ] `GetPromptRequest` - for prompts/get with arguments
- [ ] `GetPromptResult` - with message content

### 3. Lifecycle & Session Management
- [ ] `PingRequest`/`PingResult` - for keepalive
- [ ] `InitializedNotification` - sent after initialize
- [ ] `CancelledNotification` - for request cancellation

### 4. Logging
- [ ] `LoggingLevel` enum
- [ ] `LoggingMessageNotification`
- [ ] `SetLoggingLevelRequest`

### 5. Client Features
- [ ] `ListRootsRequest`/`ListRootsResult` - for roots/list
- [ ] `Root` type with URI and name
- [ ] `CreateMessageRequest`/`CreateMessageResult` - for sampling
- [ ] `CompletionRequest`/`CompletionResult` - for argument completion

### 6. Progress Tracking
- [ ] `ProgressNotification` - with token, progress, and total

### 7. Resource Content Types
- [ ] `TextResourceContent`
- [ ] `BlobResourceContent` (base64 encoded)
- [ ] `ResourceContent` union type

### 8. Error Handling Extensions
- [ ] Additional error code constants
- [ ] Structured error data types

## Code Quality Improvements

### 1. Type Safety
- [ ] Replace `object` properties with specific types or generics
- [ ] Add generic request/response pairs for better type safety

### 2. Base Classes
- [ ] Create base classes for common request/response patterns
- [ ] Implement consistent validation patterns

### 3. Constants
- [ ] Add constants for protocol versions
- [ ] Add constants for standard error codes
- [ ] Add constants for content types

### 4. Validation
- [ ] Add built-in validation for protocol constraints
- [ ] Implement request/response validation methods

## Package Preparation

### Already Complete ✅
- XML documentation for all types
- NuGet package configuration
- Test coverage (in Server.Tests)
- Clean code structure
- Proper namespace organization

### Still Needed
- [ ] Add README.md with usage examples
- [ ] Add LICENSE file
- [ ] Create sample project showing protocol usage
- [ ] Add contribution guidelines
- [ ] Set up CI/CD for package publishing

## Recommendations

1. **Priority 1**: Implement all missing protocol types to achieve full MCP compliance
2. **Priority 2**: Improve type safety and add validation
3. **Priority 3**: Add comprehensive examples and documentation
4. **Priority 4**: Set up automated package publishing

Once these items are complete, the Protocol project will provide a complete, production-ready foundation for building MCP servers in .NET.