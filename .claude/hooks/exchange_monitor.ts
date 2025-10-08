#!/usr/bin/env bun
/**
 * Tusk Exchange Monitor - Smart Checkpoint Reminders
 *
 * Tracks user prompts and tool usage patterns to provide contextual reminders
 * about checkpointing important discussions. Uses activity-based triggers
 * instead of time-based nagging.
 *
 * Gentle escalation strategy: nudge → suggest → remind → warn
 */

import { readFileSync, writeFileSync, existsSync } from "fs";
import { join } from "path";
import { homedir } from "os";

const STATE_PATH = join(homedir(), ".tusk", "exchange_state.json");

interface ExchangeState {
  promptCount: number;
  lastCheckpointPrompt: number;
  lastCheckpointTime: number;
  discussionDepth: number;
  lastReminderAt: number;
}

function loadState(): ExchangeState {
  if (existsSync(STATE_PATH)) {
    try {
      return JSON.parse(readFileSync(STATE_PATH, "utf-8"));
    } catch {
      // Corrupted state, start fresh
    }
  }
  return {
    promptCount: 0,
    lastCheckpointPrompt: 0,
    lastCheckpointTime: Date.now(),
    discussionDepth: 0,
    lastReminderAt: 0
  };
}

function saveState(state: ExchangeState): void {
  try {
    const tuskDir = join(homedir(), ".tusk");
    if (!existsSync(tuskDir)) {
      const { mkdirSync } = require("fs");
      mkdirSync(tuskDir, { recursive: true });
    }
    writeFileSync(STATE_PATH, JSON.stringify(state, null, 2));
  } catch {
    // Silently fail - don't block workflow
  }
}

function analyzePromptDepth(prompt: string): number {
  const deepPatterns = [
    /\b(plan|planning|architect|design|approach|strategy)\b/i,
    /\b(compare|versus|vs|pros? and cons?|tradeoff|alternative)\b/i,
    /\b(decide|decision|chose|selected|should we)\b/i,
    /\b(requirement|need|must have|should|goal)\b/i,
    /\b(how|why|explain|elaborate|tell me more)\b/i,
  ];

  let depth = 0;
  for (const pattern of deepPatterns) {
    if (pattern.test(prompt)) depth++;
  }
  return depth;
}

function getReminder(promptsSinceCheckpoint: number, depth: number, minutesSince: number): string | null {
  // High-value discussion + multiple prompts = remind
  if (depth >= 2 && promptsSinceCheckpoint >= 5) {
    return "💡 This discussion looks important - consider checkpointing to preserve it.";
  }

  // Many prompts regardless = eventual reminder
  if (promptsSinceCheckpoint >= 10) {
    return `📝 ${promptsSinceCheckpoint} exchanges since last checkpoint - might be time to save your progress.`;
  }

  // Moderate discussion length
  if (promptsSinceCheckpoint >= 8 && depth >= 1) {
    return "💭 Consider checkpointing this discussion before moving on.";
  }

  // Long time + some activity
  if (minutesSince > 15 && promptsSinceCheckpoint >= 5) {
    return `⏰ It's been ${minutesSince} minutes since your last checkpoint.`;
  }

  return null;
}

async function main() {
  try {
    // Read JSON input from stdin
    const stdinBuffer = [];
    for await (const chunk of process.stdin) {
      stdinBuffer.push(chunk);
    }
    const inputData = JSON.parse(Buffer.concat(stdinBuffer).toString());

    const state = loadState();
    const prompt = inputData.prompt || '';

    // Increment prompt counter
    state.promptCount++;

    // Analyze discussion depth
    const currentDepth = analyzePromptDepth(prompt);
    state.discussionDepth = Math.max(state.discussionDepth, currentDepth);

    // Check if this prompt mentions tusk tools (indicates checkpoint was made)
    const mentionsTusk = /\b(checkpoint|recall|plan|tusk)\b/i.test(prompt);

    if (mentionsTusk) {
      // Reset counters - user is engaging with tusk
      state.lastCheckpointPrompt = state.promptCount;
      state.lastCheckpointTime = Date.now();
      state.discussionDepth = 0;
      state.lastReminderAt = state.promptCount;
      saveState(state);
      process.exit(0);
    }

    // Calculate metrics
    const promptsSinceCheckpoint = state.promptCount - state.lastCheckpointPrompt;
    const minutesSince = Math.floor((Date.now() - state.lastCheckpointTime) / 60000);
    const promptsSinceReminder = state.promptCount - state.lastReminderAt;

    // Don't remind too frequently (at least 3 prompts between reminders)
    if (promptsSinceReminder < 3) {
      saveState(state);
      process.exit(0);
    }

    // Get context-appropriate reminder
    const reminder = getReminder(promptsSinceCheckpoint, state.discussionDepth, minutesSince);

    if (reminder) {
      // Use stdout (console.log) for UserPromptSubmit hooks so the reminder
      // gets added as context to the conversation with exit code 0
      console.log(`\n${reminder}\n`);
      state.lastReminderAt = state.promptCount;
    }

    saveState(state);
  } catch (error) {
    // Silently fail - don't block workflow
  }

  process.exit(0);
}

main();
