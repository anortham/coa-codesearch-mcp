#!/usr/bin/env bun
/**
 * Tusk Post Response Hook - Insight & Knowledge Capture
 *
 * Captures Claude's insights, explanations, and discoveries to preserve
 * valuable knowledge that might be lost during compaction.
 *
 * This hook is the SECRET SAUCE for context preservation!
 */

import { spawnSync } from "bun";
import { logSuccess, logError, logSkip, findTuskCli } from "./hook-logger.ts";

interface InsightPattern {
  pattern: RegExp;
  type: string;
  weight: number;
}

const INSIGHT_PATTERNS: InsightPattern[] = [
  // Technical discoveries and explanations
  { pattern: /\b(discovered|found that|realized|learned|insight|key insight)\b/i, type: "discovery", weight: 3 },
  { pattern: /\b(the issue is|the problem is|root cause|turns out)\b/i, type: "diagnosis", weight: 3 },
  { pattern: /\b(the solution is|approach is|strategy|best practice|pattern)\b/i, type: "solution", weight: 3 },

  // Architecture and design insights
  { pattern: /\b(architecture|design pattern|refactor|modular|structure)\b/i, type: "architecture", weight: 2 },
  { pattern: /\b(performance|optimization|efficiency|bottleneck)\b/i, type: "performance", weight: 2 },
  { pattern: /\b(security|vulnerability|authentication|authorization)\b/i, type: "security", weight: 2 },

  // Code quality and methodology
  { pattern: /\b(best practice|convention|standard|pattern|anti-pattern)\b/i, type: "methodology", weight: 2 },
  { pattern: /\b(test|testing|coverage|spec|validation)\b/i, type: "testing", weight: 1 },

  // Important findings and decisions
  { pattern: /\b(important|crucial|critical|key|essential|fundamental)\b/i, type: "important", weight: 2 },
  { pattern: /\b(decision|chose|decided|recommend|suggestion)\b/i, type: "decision", weight: 2 },

  // Technical explanations
  { pattern: /\b(because|since|due to|reason|explanation|why)\b/i, type: "explanation", weight: 1 },
  { pattern: /\b(here's how|this works by|implementation|algorithm)\b/i, type: "explanation", weight: 1 },
];

function analyzeContent(text: string): { score: number; insights: string[]; primaryType: string | null } {
  let totalScore = 0;
  const foundInsights: string[] = [];
  const typeScores: Record<string, number> = {};

  // Check for code blocks (indicate technical content)
  const codeBlocks = (text.match(/```[\s\S]*?```/g) || []).length;
  totalScore += codeBlocks * 2;

  // Check for bullet points and structured content
  const bulletPoints = (text.match(/^[\s]*[-*•]\s+/gm) || []).length;
  totalScore += Math.min(bulletPoints * 0.5, 3);

  // Analyze against insight patterns
  for (const { pattern, type, weight } of INSIGHT_PATTERNS) {
    const matches = text.match(new RegExp(pattern.source, 'gi')) || [];
    if (matches.length > 0) {
      totalScore += matches.length * weight;
      typeScores[type] = (typeScores[type] || 0) + matches.length * weight;
      foundInsights.push(`${type}: ${matches.length} matches`);
    }
  }

  // Find primary insight type
  const primaryType = Object.keys(typeScores).length > 0
    ? Object.entries(typeScores).sort(([,a], [,b]) => b - a)[0][0]
    : null;

  return { score: totalScore, insights: foundInsights, primaryType };
}

function extractKeyInsights(text: string, primaryType: string | null): string {
  // Try to extract the most valuable sentence or two
  const sentences = text.split(/[.!?]+/).map(s => s.trim()).filter(s => s.length > 20);

  if (primaryType) {
    // Look for sentences containing primary insight type keywords
    const typePattern = INSIGHT_PATTERNS.find(p => p.type === primaryType)?.pattern;
    if (typePattern) {
      const relevantSentence = sentences.find(s => typePattern.test(s));
      if (relevantSentence && relevantSentence.length < 150) {
        return relevantSentence;
      }
    }
  }

  // Fallback to first substantial sentence
  const firstGoodSentence = sentences.find(s => s.length > 30 && s.length < 150);
  if (firstGoodSentence) {
    return firstGoodSentence;
  }

  // Final fallback
  return text.split('\n').find(line => line.trim().length > 20)?.trim() || "Knowledge captured";
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

    // Extract assistant response content
    let content = '';
    if (inputData.assistant_message?.content) {
      content = inputData.assistant_message.content;
    } else if (inputData.content) {
      content = inputData.content;
    } else if (inputData.message?.content) {
      content = inputData.message.content;
    }

    if (!content || content.length < 50) {
      logSkip("post_response", "response too short");
      process.exit(0);
    }

    // Analyze content for insights
    const analysis = analyzeContent(content);

    // Only save if it contains valuable insights (threshold: 8 points)
    if (analysis.score < 8) {
      logSkip("post_response", `low insight score: ${analysis.score}`);
      process.exit(0);
    }

    const keyInsight = extractKeyInsights(content, analysis.primaryType);
    const description = analysis.primaryType
      ? `Claude ${analysis.primaryType}: ${keyInsight}`
      : `Claude insight: ${keyInsight}`;

    // Find tusk CLI
    const cliPath = findTuskCli(import.meta.path);
    if (!cliPath) {
      logError("post_response", "CLI not found");
      process.exit(0);
    }

    // Create tags based on insight analysis
    const tags = ["claude-insight"];
    if (analysis.primaryType) {
      tags.push(analysis.primaryType);
    }

    const cliArgs = [
      "bun", cliPath, "checkpoint", description,
      tags.join(","),
      "--entry-type=progress",
      `--confidence=${(analysis.score / 15).toFixed(2)}` // Normalize score to 0-1
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
      logSuccess("post_response", `${analysis.primaryType || 'insight'}: ${keyInsight.substring(0, 50)}...${sessionInfo}`);
    } else {
      const errorOutput = new TextDecoder().decode(result.stderr);
      logError("post_response", errorOutput);
    }
  } catch (error) {
    logError("post_response", String(error));
  }

  process.exit(0);
}

main();