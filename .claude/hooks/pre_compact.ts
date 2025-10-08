#!/usr/bin/env bun
/**
 * Tusk Enhanced Pre-Compact Hook
 *
 * Captures rich context before compaction including:
 * - Git status and recent changes
 * - Project context files
 * - Session metadata
 * - Transcript backup creation
 *
 * This prevents loss of work progress when Claude Code compacts the conversation.
 * Cross-platform compatible for Windows, macOS, and Linux.
 */

import { spawnSync } from "bun";
import { logSuccess, logError, findTuskCli } from "./hook-logger.ts";
import * as fs from "fs";
import * as path from "path";
import * as os from "os";

interface ClaudeCompactInput {
  session_id?: string;
  transcript_path?: string;
  trigger?: "manual" | "auto";
  custom_instructions?: string;
}

async function getGitContext(): Promise<string> {
  try {
    // Get current branch
    const branchResult = spawnSync(["git", "rev-parse", "--abbrev-ref", "HEAD"], {
      stdout: "pipe", stderr: "pipe", timeout: 5000
    });
    const branch = branchResult.success
      ? new TextDecoder().decode(branchResult.stdout).trim()
      : "unknown";

    // Get uncommitted changes
    const statusResult = spawnSync(["git", "status", "--porcelain"], {
      stdout: "pipe", stderr: "pipe", timeout: 5000
    });

    let changes = [];
    if (statusResult.success) {
      const output = new TextDecoder().decode(statusResult.stdout).trim();
      changes = output ? output.split('\n').filter(line => line.trim()) : [];
    }

    // Get recent commits (last 3)
    const logResult = spawnSync(["git", "log", "--oneline", "-3"], {
      stdout: "pipe", stderr: "pipe", timeout: 5000
    });
    const recentCommits = logResult.success
      ? new TextDecoder().decode(logResult.stdout).trim()
      : "";

    let context = `Branch: ${branch}`;
    if (changes.length > 0) {
      context += `\nUncommitted changes: ${changes.length} files`;
      context += `\n${changes.slice(0, 5).join('\n')}`;
    }
    if (recentCommits) {
      context += `\n\nRecent commits:\n${recentCommits}`;
    }

    return context;
  } catch {
    return "Git context unavailable";
  }
}

async function getProjectContext(): Promise<string> {
  const contextFiles = [
    ".claude/CONTEXT.md",
    ".claude/TODO.md",
    "TODO.md",
    "README.md",
    "CLAUDE.md"
  ];

  const contexts = [];

  for (const file of contextFiles) {
    try {
      if (fs.existsSync(file)) {
        const content = fs.readFileSync(file, 'utf-8').trim();
        if (content) {
          contexts.push(`=== ${file} ===\n${content.substring(0, 500)}${content.length > 500 ? '...' : ''}`);
        }
      }
    } catch {
      // Skip files we can't read
    }
  }

  return contexts.length > 0 ? contexts.join('\n\n') : "No project context files found";
}

async function backupTranscript(transcriptPath: string, trigger: string): Promise<string | null> {
  if (!transcriptPath || !fs.existsSync(transcriptPath)) {
    return null;
  }

  try {
    const tuskDir = path.join(os.homedir(), ".tusk");
    const backupDir = path.join(tuskDir, "transcript-backups");

    // Ensure backup directory exists
    fs.mkdirSync(backupDir, { recursive: true });

    const timestamp = new Date().toISOString().replace(/[:T]/g, '-').slice(0, 19);
    const backupPath = path.join(backupDir, `transcript-${timestamp}-${trigger}.md`);

    // Copy transcript to backup location (likely markdown format from Claude Code)
    fs.copyFileSync(transcriptPath, backupPath);

    return backupPath;
  } catch {
    return null;
  }
}

async function main() {
  let claudeCodeSessionId: string | undefined;

  try {
    // Ensure transcript backup directory exists up front
    const tuskDir = path.join(os.homedir(), ".tusk");
    const backupDir = path.join(tuskDir, "transcript-backups");
    try {
      fs.mkdirSync(backupDir, { recursive: true });
    } catch {
      // Directory creation failed but continue - will fail later if needed
    }

    // Read JSON input from stdin if available
    let inputData: ClaudeCompactInput = {};
    try {
      const stdinBuffer = [];
      for await (const chunk of process.stdin) {
        stdinBuffer.push(chunk);
      }
      if (stdinBuffer.length > 0) {
        inputData = JSON.parse(Buffer.concat(stdinBuffer).toString()) || {};
        claudeCodeSessionId = (inputData as any).session_id;
      }
    } catch {
      // No input or invalid JSON - continue with empty data
    }

    const now = new Date();
    const trigger = inputData.trigger || "auto";

    // Gather rich context
    const gitContext = await getGitContext();
    const projectContext = await getProjectContext();

    // Backup transcript if available
    let transcriptBackup = null;
    if (inputData.transcript_path) {
      transcriptBackup = await backupTranscript(inputData.transcript_path, trigger);
    }

    // Create comprehensive description
    const baseDescription = `Pre-compaction context preservation (${trigger})`;
    const contextDetails = [
      "=== Session Info ===",
      `Session ID: ${inputData.session_id || 'unknown'}`,
      `Timestamp: ${now.toLocaleString()}`,
      `Trigger: ${trigger}`,
      "",
      "=== Git Context ===",
      gitContext,
      "",
      "=== Project Context ===",
      projectContext
    ];

    if (transcriptBackup) {
      contextDetails.push("", "=== Transcript Backup ===", `Saved to: ${transcriptBackup}`);
    }

    const description = `${baseDescription}\n\n${contextDetails.join('\n')}`;

    // Find tusk CLI using smart path resolution
    const cliPath = findTuskCli(import.meta.path);
    if (!cliPath) {
      logError("pre_compact", "CLI not found in any expected location");
      console.error(`⚠️ Tusk CLI not found. Set TUSK_CLI_PATH environment variable or ensure tusk is in a standard location.`);
      process.exit(0);
    }

    // Create checkpoint with rich context
    const tags = [
      "pre-compaction",
      "context-preservation",
      trigger,
      "auto-checkpoint"
    ];

    const cliArgs = [
      "bun", cliPath, "checkpoint", description,
      tags.join(","),
      "--entry-type=auto-save",
      "--confidence=0.9"
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
      logSuccess("pre_compact", `rich context preserved (${trigger})${sessionInfo}`);
      console.error(`✅ Pre-compaction checkpoint saved`);
      console.error(`💡 Your active plan and context will survive - use recall() after restart`);
    } else {
      const errorOutput = new TextDecoder().decode(result.stderr);
      logError("pre_compact", errorOutput);
      console.error(`⚠️ Pre-compact checkpoint failed: ${errorOutput}`);
    }
  } catch (error) {
    logError("pre_compact", String(error));
    console.error(`⚠️ Pre-compact checkpoint error: ${error}`);
  }

  // Always exit successfully to not block compaction
  process.exit(0);
}

main();