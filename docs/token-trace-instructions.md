# Token Tracing Instructions

## Overview
I've added token tracing to help debug the 28k token issue with memory storage operations.

## How to Test

1. **Rebuild and reinstall the MCP server** with the new changes
2. **Start a new Claude Code session** to use the updated server
3. **Try storing a memory** using `remember_session` or any memory tool
4. **Check the log file** at: `%TEMP%\mcp-token-trace.log`

## What the Log Shows

The log will trace token usage at three levels:

1. **Tool Input Level** - Shows the size of the input parameters
2. **Tool Response Level** - Shows the size of the response object before JSON serialization
3. **MCP Protocol Level** - Shows the final serialized response size sent over the wire

## Expected vs Actual

- **Expected**: ~200-500 bytes (~50-125 tokens) for a simple memory storage confirmation
- **Actual**: If we see 100,000+ bytes, we'll know where the bloat is coming from

## Log Format

```
[2025-01-21 12:34:56.789] RememberSession input: summary=150 chars, files=3
[2025-01-21 12:34:56.790] RememberSession response object size: 245 chars
[2025-01-21 12:34:56.791] CreateSuccessResult: 280 bytes, ~70 tokens
First 500 chars: {"success":true,"message":"📝 Work session recorded..."}
[2025-01-21 12:34:56.792] MCP Response: 350 bytes, ~87 tokens
Response preview: {"jsonrpc":"2.0","id":123,"result":{"content":[{"type":"text"...
```

## Next Steps Based on Results

1. **If MCP Response is huge**: The protocol is adding overhead
2. **If CreateSuccessResult is huge**: Something in the serialization is wrong
3. **If all are small**: The issue is at the Claude Code integration layer

## Cleanup

After testing, delete the log file to avoid it growing too large:
```bash
del %TEMP%\mcp-token-trace.log
```