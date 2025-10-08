#!/usr/bin/env bun
/**
 * Tusk User Prompt Submit Hook
 *
 * Captures important user prompts and technical decisions to prevent loss during compaction.
 * Automatically detects and saves checkpoints for significant discussions and decisions.
 *
 * Cross-platform compatible for Windows, macOS, and Linux.
 */

import { spawnSync } from "bun";
import { logHookActivity, logSuccess, logError, logSkip, findTuskCli } from "./hook-logger.ts";

function detectImportantPrompt(text: string): boolean {
  const importantPatterns = [
    /\b(implement|build|create|develop|design|architecture)\b/i,
    /\b(bug|fix|issue|problem|error|debug)\b/i,
    /\b(plan|strategy|approach|solution|algorithm)\b/i,
    /\b(refactor|optimize|improve|enhance|update)\b/i,
    /\b(review|analyze|investigate|research)\b/i,
    /\b(deploy|release|production|staging)\b/i,
    /\b(test|testing|tests|spec|specification)\b/i,
    /\b(api|database|security|performance)\b/i,
  ];

  return importantPatterns.some(pattern => pattern.test(text));
}

function extractKeyContent(text: string): string {
  // Remove common prefixes
  let cleaned = text.replace(/^(please|can you|could you|help me|i need to|let's)\s*/i, '');

  // Get first sentence or meaningful chunk
  const sentences = cleaned.split(/[.!?]\s+/);
  if (sentences.length > 0) {
    const firstSentence = sentences[0].trim();
    if (firstSentence.length > 10) {
      if (firstSentence.length > 80) {
        return firstSentence.substring(0, 77) + "...";
      }
      return firstSentence;
    }
  }

  // Fallback to first 80 chars
  if (cleaned.length > 80) {
    return cleaned.substring(0, 77) + "...";
  }
  return cleaned;
}

async function main() {
  let claudeCodeSessionId: string | undefined;

  try {
    // Read JSON input from stdin
    const stdinBuffer = [];
    for await (const chunk of process.stdin) {
      stdinBuffer.push(chunk);
    }
    const inputData = JSON.parse(Buffer.concat(stdinBuffer).toString());
    claudeCodeSessionId = inputData.session_id;

    // Extract session_id and prompt from input data
    const content = inputData.prompt || '';

    // Skip if the prompt is too short or not important
    if (content.trim().length < 20) {
      logSkip("user_prompt", "too short");
      process.exit(0);
    }

    if (!detectImportantPrompt(content)) {
      logSkip("user_prompt", "not important");
      process.exit(0);
    }

    const keyContent = extractKeyContent(content);
    const description = `User request: ${keyContent}`;

    // Find tusk CLI using smart path resolution
    const cliPath = findTuskCli(import.meta.path);
    if (!cliPath) {
      logError("user_prompt", "CLI not found in any expected location");
      console.error(`⚠️ Tusk CLI not found. Set TUSK_CLI_PATH environment variable or ensure tusk is in a standard location.`);
      process.exit(0);
    }

    const cliArgs = [
      "bun", cliPath, "checkpoint", description,
      "user-request",
      "--entry-type=user-request",
      "--confidence=0.8"
    ];

    // Add session ID if available
    if (claudeCodeSessionId) {
      cliArgs.push(`--session-id=${claudeCodeSessionId}`);
    }

    const result = spawnSync(cliArgs, {
      stdout: "pipe",
      stderr: "pipe",
    });

    if (result.success) {
      const sessionInfo = claudeCodeSessionId ? ` [${claudeCodeSessionId.slice(0, 8)}...]` : "";
      logSuccess("user_prompt", `${keyContent}${sessionInfo}`);
      console.error(`✅ User prompt checkpoint saved: ${description}`);
    } else {
      const errorOutput = new TextDecoder().decode(result.stderr);
      logError("user_prompt", errorOutput);
      console.error(`⚠️ User prompt checkpoint failed: ${errorOutput}`);
    }
  } catch (error) {
    logError("user_prompt", String(error));
    console.error(`⚠️ User prompt hook error: ${error}`);
  }

  // Always exit successfully to not interfere with Claude
  process.exit(0);
}

main();