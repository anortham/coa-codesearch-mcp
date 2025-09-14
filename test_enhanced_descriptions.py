#!/usr/bin/env python3
"""
Test script to verify enhanced tool descriptions are working in CodeSearch MCP server.
This script starts the MCP server and queries the tools/list endpoint to see if our
XML documentation with examples is being included in the tool schemas.
"""

import json
import subprocess
import sys
import time
from threading import Thread
import os

def run_mcp_server():
    """Run the CodeSearch MCP server in STDIO mode"""
    server_path = r"C:\source\COA CodeSearch MCP\COA.CodeSearch.McpServer\bin\Debug\net9.0\COA.CodeSearch.McpServer.exe"

    if not os.path.exists(server_path):
        print(f"Server executable not found at: {server_path}")
        return None

    try:
        process = subprocess.Popen(
            [server_path],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            cwd=r"C:\source\COA CodeSearch MCP"
        )
        return process
    except Exception as e:
        print(f"Failed to start server: {e}")
        return None

def send_mcp_request(process, request):
    """Send an MCP request and get the response"""
    try:
        # Send request
        process.stdin.write(json.dumps(request) + '\n')
        process.stdin.flush()

        # Read response
        response_line = process.stdout.readline()
        if response_line:
            return json.loads(response_line.strip())
        return None
    except Exception as e:
        print(f"Error sending request: {e}")
        return None

def test_enhanced_descriptions():
    """Test that enhanced descriptions are working"""
    print("Starting CodeSearch MCP server...")
    process = run_mcp_server()

    if not process:
        print("Failed to start server")
        return False

    try:
        # Initialize MCP connection
        init_request = {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "initialize",
            "params": {
                "protocolVersion": "2024-11-05",
                "capabilities": {
                    "tools": {}
                },
                "clientInfo": {
                    "name": "test-client",
                    "version": "1.0.0"
                }
            }
        }

        print("Initializing MCP connection...")
        init_response = send_mcp_request(process, init_request)
        if not init_response or "result" not in init_response:
            print("Failed to initialize MCP connection")
            print(f"Response: {init_response}")
            return False

        print("MCP connection initialized")

        # Request tools list
        tools_request = {
            "jsonrpc": "2.0",
            "id": 2,
            "method": "tools/list"
        }

        print("Requesting tools list...")
        tools_response = send_mcp_request(process, tools_request)
        if not tools_response or "result" not in tools_response:
            print("Failed to get tools list")
            print(f"Response: {tools_response}")
            return False

        tools = tools_response["result"]["tools"]
        print(f"Found {len(tools)} tools")

        # Find TextSearchTool and check its parameter descriptions
        text_search_tool = None
        for tool in tools:
            if tool["name"] == "text_search":
                text_search_tool = tool
                break

        if not text_search_tool:
            print("TextSearchTool not found in tools list")
            return False

        print("Found TextSearchTool, checking enhanced descriptions...")

        # Check if the query parameter has our enhanced description with examples
        input_schema = text_search_tool.get("inputSchema", {})
        properties = input_schema.get("properties", {})
        query_property = properties.get("query", {})
        query_description = query_property.get("description", "")

        print(f"\nQuery parameter description:")
        print(f"   {query_description}")

        # Check if our enhanced description with examples is present
        enhanced_indicators = [
            "supports regex, wildcards, and code patterns",
            "Examples:",
            "class UserService",
            "*.findBy*",
            "TODO|FIXME"
        ]

        found_indicators = []
        for indicator in enhanced_indicators:
            if indicator in query_description:
                found_indicators.append(indicator)

        print(f"\nEnhancement indicators found: {len(found_indicators)}/{len(enhanced_indicators)}")
        for indicator in found_indicators:
            print(f"   [OK] '{indicator}'")

        missing_indicators = [i for i in enhanced_indicators if i not in found_indicators]
        for indicator in missing_indicators:
            print(f"   [MISSING] '{indicator}'")

        # Check workspace path parameter too
        workspace_property = properties.get("workspacePath", {})
        workspace_description = workspace_property.get("description", "")

        print(f"\nWorkspacePath parameter description:")
        print(f"   {workspace_description}")

        # Success criteria: at least 3 out of 5 enhancement indicators
        success = len(found_indicators) >= 3

        if success:
            print("\nSUCCESS: Enhanced descriptions are working!")
            print("   Our XML documentation with examples is being extracted and included in MCP schema.")
        else:
            print("\nFAILURE: Enhanced descriptions not fully working")
            print("   XML documentation may not be getting extracted properly.")

        return success

    except Exception as e:
        print(f"Test failed with error: {e}")
        return False
    finally:
        if process:
            process.terminate()
            process.wait()

if __name__ == "__main__":
    print("Testing Enhanced Tool Descriptions")
    print("=" * 50)
    success = test_enhanced_descriptions()
    sys.exit(0 if success else 1)