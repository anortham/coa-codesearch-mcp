/**
 * Tusk Hook Logger - Minimal logging with daily rotation
 *
 * Provides concise logging for hook activities with automatic daily log rotation
 * to prevent log files from growing indefinitely.
 */

import { appendFileSync, existsSync, writeFileSync, statSync, renameSync } from "fs";
import { join, resolve, dirname } from "path";
import { homedir } from "os";

const HOOKS_LOG_PATH = join(homedir(), ".tusk", "hooks.log");
const MAX_LOG_AGE_MS = 24 * 60 * 60 * 1000; // 24 hours
const MAX_LOG_SIZE_BYTES = 100 * 1024; // 100KB
const MAX_ARCHIVED_LOGS = 7; // Keep last 7 archived logs

/**
 * Find tusk CLI path - works from both project and global hook directories
 */
export function findTuskCli(hookPath: string): string | null {
  // Try relative path first (for project-local hooks)
  const hookDir = dirname(hookPath);
  const projectCliPath = resolve(hookDir, '../../cli.ts');
  if (existsSync(projectCliPath)) {
    return projectCliPath;
  }

  // Common development locations
  const commonPaths = [
    join(homedir(), 'Source/tusk/cli.ts'),
    join(homedir(), 'source/tusk/cli.ts'),
    join(homedir(), 'src/tusk/cli.ts'),
    join(homedir(), 'dev/tusk/cli.ts'),
    join(homedir(), 'projects/tusk/cli.ts'),
    join(homedir(), 'tusk/cli.ts'),
  ];

  for (const path of commonPaths) {
    if (existsSync(path)) {
      return path;
    }
  }

  // Check environment variable
  const envPath = process.env.TUSK_CLI_PATH;
  if (envPath && existsSync(envPath)) {
    return envPath;
  }

  return null;
}

function shouldRotateLog(): boolean {
  if (!existsSync(HOOKS_LOG_PATH)) {
    return false;
  }

  try {
    const stats = statSync(HOOKS_LOG_PATH);
    const ageMs = Date.now() - stats.mtime.getTime();
    const sizeBytes = stats.size;

    // Rotate if file is too old OR too large
    return ageMs > MAX_LOG_AGE_MS || sizeBytes > MAX_LOG_SIZE_BYTES;
  } catch {
    return false;
  }
}

function cleanupOldArchivedLogs(): void {
  try {
    const tuskDir = dirname(HOOKS_LOG_PATH);
    const archivePattern = /^hooks\.log\.(\d{4}-\d{2}-\d{2})$/;

    if (!existsSync(tuskDir)) return;

    const { readdirSync } = require("fs");
    const files = readdirSync(tuskDir)
      .filter((file: string) => archivePattern.test(file))
      .map((file: string) => ({
        name: file,
        path: join(tuskDir, file),
        date: file.match(archivePattern)?.[1] || ''
      }))
      .sort((a, b) => b.date.localeCompare(a.date)); // Sort newest first

    // Remove old archived logs beyond our limit
    if (files.length >= MAX_ARCHIVED_LOGS) {
      for (const file of files.slice(MAX_ARCHIVED_LOGS - 1)) {
        const { unlinkSync } = require("fs");
        unlinkSync(file.path);
      }
    }
  } catch {
    // Silently fail cleanup - don't block hook execution
  }
}

function rotateLog(): void {
  try {
    if (shouldRotateLog()) {
      const today = new Date().toISOString().split('T')[0];
      const timestamp = new Date().toISOString().replace(/[:T]/g, '-').slice(0, 19);

      // Archive the current log file
      const archivePath = join(dirname(HOOKS_LOG_PATH), `hooks.log.${today}`);

      // Only archive if there's actual content (more than just the header)
      if (existsSync(HOOKS_LOG_PATH)) {
        const stats = statSync(HOOKS_LOG_PATH);
        if (stats.size > 50) { // More than just the header
          renameSync(HOOKS_LOG_PATH, archivePath);
        }
      }

      // Create new log file
      writeFileSync(HOOKS_LOG_PATH, `=== Tusk Hooks Log - ${today} ===\n`);

      // Clean up old archived logs
      cleanupOldArchivedLogs();
    }
  } catch (error) {
    // Silently fail rotation - don't block hook execution
  }
}

export function logHookActivity(hookName: string, action: string, details?: string): void {
  try {
    rotateLog();

    const timestamp = new Date().toLocaleTimeString();
    const message = details ? `${action}: ${details}` : action;
    const logEntry = `[${timestamp}] ${hookName}: ${message}\n`;

    appendFileSync(HOOKS_LOG_PATH, logEntry);
  } catch {
    // Silently fail logging - don't block hook execution
  }
}

export function logSuccess(hookName: string, description: string): void {
  logHookActivity(hookName, "✅ Saved", description);
}

export function logError(hookName: string, error: string): void {
  logHookActivity(hookName, "❌ Failed", error);
}

export function logSkip(hookName: string, reason: string): void {
  logHookActivity(hookName, "⏭️ Skipped", reason);
}