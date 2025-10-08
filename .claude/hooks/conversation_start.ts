#!/usr/bin/env bun
/**
 * Tusk Conversation Start Hook - Session Context Setup
 *
 * Automatically restores relevant context when starting new conversations,
 * especially useful for continuing work after compaction or session restart.
 *
 * This hook implements SMART CONTEXT CONTINUITY!
 */

import { spawnSync } from "bun";
import { logSuccess, logError, logSkip, findTuskCli } from "./hook-logger.ts";

interface SessionContext {
  workspaceInfo: string;
  recentWork: string[];
  activeTopics: string[];
  timeOfDay: string;
}

function getWorkspaceContext(): string {
  try {
    // Get git status for current context
    const gitResult = spawnSync(["git", "status", "--porcelain"], { stdout: "pipe", stderr: "pipe" });
    if (gitResult.success) {
      const stdout = new TextDecoder().decode(gitResult.stdout);
      const changes = stdout.trim().split('\n').filter(line => line.trim()).length;

      // Get current branch
      const branchResult = spawnSync(["git", "branch", "--show-current"], { stdout: "pipe", stderr: "pipe" });
      const branch = branchResult.success
        ? new TextDecoder().decode(branchResult.stdout).trim()
        : "unknown";

      if (changes > 0) {
        return `Git: ${branch} branch, ${changes} uncommitted changes`;
      } else {
        return `Git: ${branch} branch, clean working tree`;
      }
    }
  } catch {
    // Not a git repo or git not available
  }

  try {
    // Get current directory context
    const pwd = process.cwd();
    const dirName = pwd.split('/').pop() || pwd;
    return `Working in: ${dirName}`;
  } catch {
    return "Starting new session";
  }
}

function getTimeContext(): string {
  const now = new Date();
  const hour = now.getHours();

  if (hour < 6) return "late night";
  if (hour < 12) return "morning";
  if (hour < 17) return "afternoon";
  if (hour < 22) return "evening";
  return "late night";
}

function detectSessionType(context: SessionContext): string {
  const { workspaceInfo, timeOfDay } = context;

  // Check if this looks like a continuation
  if (workspaceInfo.includes("uncommitted changes")) {
    return "work-continuation";
  }

  // Check if it's a new day
  if (timeOfDay === "morning") {
    return "daily-startup";
  }

  return "new-session";
}

async function getRecentContext(): Promise<string[]> {
  const cliPath = findTuskCli(import.meta.path);
  if (!cliPath) return [];

  try {
    // Get recent checkpoints from current workspace
    const result = spawnSync([
      "bun", cliPath, "recall",
      "--days", "2",
      "--workspace", "current",
      "--maxEntries", "5"
    ], {
      stdout: "pipe",
      stderr: "pipe",
    });

    if (result.success) {
      const output = new TextDecoder().decode(result.stdout);

      // Extract key recent activities with better parsing
      const lines = output.split('\n').filter(line => {
        // Look for lines with bullet points that contain actual work descriptions
        return (line.includes('•') || line.includes('-')) &&
               !line.includes('Found 0') &&
               !line.includes('workspace:') &&
               line.trim().length > 10;
      });

      // Clean up the lines and extract meaningful descriptions
      const cleanedLines = lines.map(line => {
        // Remove timestamps and metadata, keep the description
        return line.replace(/^\s*[•-]\s*/, '')
                  .replace(/\s*\|\s*\d+[hm]\s+ago.*$/, '')
                  .replace(/\s*\(\d{4}-\d{2}-\d{2}.*?\)/, '')
                  .trim();
      }).filter(line => line.length > 0);

      return cleanedLines.slice(0, 3); // Last 3 meaningful activities
    }
  } catch {
    // Failed to get context, continue without it
  }

  return [];
}

async function main() {
  let claudeCodeSessionId: string | undefined;

  try {
    // Try to read Claude Code hook input from stdin
    try {
      const stdinInput = await new Response(Bun.stdin.stream()).text();
      if (stdinInput.trim()) {
        const hookInput = JSON.parse(stdinInput);
        claudeCodeSessionId = hookInput.session_id;
      }
    } catch (stdinError) {
      // No stdin input or invalid JSON - continue without session ID
      console.error("📝 No Claude Code session input detected, running standalone");
    }

    // Build session context
    const context: SessionContext = {
      workspaceInfo: getWorkspaceContext(),
      recentWork: await getRecentContext(),
      activeTopics: [], // Could be enhanced to detect active topics
      timeOfDay: getTimeContext(),
    };

    const sessionType = detectSessionType(context);

    // Create contextual checkpoint
    let description = `Session started (${sessionType}) - ${context.timeOfDay}`;

    if (context.workspaceInfo) {
      description += ` | ${context.workspaceInfo}`;
    }

    if (context.recentWork.length > 0) {
      description += ` | Continuing from recent work`;
    }

    const cliPath = findTuskCli(import.meta.path);
    if (!cliPath) {
      logError("conversation_start", "CLI not found");
      process.exit(0);
    }

    // Create session start checkpoint with contextual tags and session info
    const tags = ["session-start", sessionType, context.timeOfDay];

    const cliArgs = [
      "bun", cliPath, "checkpoint", description,
      tags.join(","),
      "--entry-type=session-marker",
      "--confidence=0.95"
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
      logSuccess("conversation_start", `${sessionType} (${context.timeOfDay})${sessionInfo}`);

      // Provide contextual session information
      if (context.recentWork.length > 0) {
        console.error(`🔄 Session context restored (${sessionType})`);
        console.error(`📋 Recent work found: ${context.recentWork.length} activities`);
        context.recentWork.forEach((work, i) => {
          console.error(`   ${i + 1}. ${work.substring(0, 60)}${work.length > 60 ? '...' : ''}`);
        });
        console.error(`💡 Use recall() to restore full context + active plan`);
      } else {
        console.error(`🆕 Fresh session started (${sessionType})`);
        if (sessionType === "work-continuation") {
          console.error(`💡 No recent context found - use recall(days: 7) for older work + active plan`);
        } else {
          console.error(`💡 Use recall() to see your active plan and recent context`);
        }
      }

      // Additional workspace context
      if (context.workspaceInfo.includes("uncommitted changes")) {
        console.error(`⚠️  Uncommitted changes detected - work in progress`);
      }
    } else {
      const errorOutput = new TextDecoder().decode(result.stderr);
      logError("conversation_start", errorOutput);
    }
  } catch (error) {
    logError("conversation_start", String(error));
  }

  process.exit(0);
}

main();