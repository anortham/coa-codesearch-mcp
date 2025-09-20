#!/usr/bin/env bun

/**
 * Tree-sitter parsing service for COA CodeSearch MCP
 * Communicates via stdin/stdout JSON protocol
 */

import { stdin, stdout } from 'process';
import { ExtractionRequest, ServiceResponse, ErrorResponse } from './types';
import { TreeSitterParser } from './parser';

const VERSION = '1.1.0-signature-fix';
const parser = new TreeSitterParser();

// Initialize parser on startup
console.error(`Tree-sitter service ${VERSION} starting up...`);
await parser.initialize();
console.error(`Tree-sitter service ${VERSION} ready`);

// Helper to send JSON response
function sendResponse(response: ServiceResponse) {
  stdout.write(JSON.stringify(response) + '\n');
}

// Helper to send error
function sendError(error: string) {
  const response: ErrorResponse = {
    success: false,
    error
  };
  sendResponse(response);
}

// Process incoming requests line by line
async function processRequest(line: string) {
  try {
    const request: ExtractionRequest = JSON.parse(line);

    switch (request.action) {
      case 'extract': {
        if (!request.content || !request.language) {
          sendError('Missing required parameters: content and language');
          return;
        }

        const result = await parser.extractTypes(
          request.content,
          request.language,
          request.filePath
        );
        sendResponse(result);
        break;
      }

      case 'health': {
        sendResponse({
          status: 'healthy',
          version: VERSION,
          supportedLanguages: parser.getSupportedLanguages()
        });
        break;
      }

      case 'supported-languages': {
        sendResponse({
          status: 'healthy',
          version: VERSION,
          supportedLanguages: parser.getSupportedLanguages()
        });
        break;
      }

      default:
        sendError(`Unknown action: ${(request as any).action}`);
    }
  } catch (error) {
    sendError(`Request processing error: ${error instanceof Error ? error.message : String(error)}`);
  }
}

// Read from stdin line by line
console.error('Tree-sitter service started, waiting for requests...');

for await (const line of console) {
  await processRequest(line);
}

// Handle process termination gracefully
process.on('SIGTERM', () => {
  console.error('Tree-sitter service shutting down...');
  process.exit(0);
});

process.on('SIGINT', () => {
  console.error('Tree-sitter service interrupted...');
  process.exit(0);
});