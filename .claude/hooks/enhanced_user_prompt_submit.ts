#!/usr/bin/env bun
/**
 * Tusk Enhanced User Prompt Submit Hook
 *
 * ULTRA-SOPHISTICATED prompt analysis with:
 * - Multi-dimensional pattern detection
 * - Work type classification
 * - Technology extraction
 * - Complexity estimation
 * - Priority assessment
 *
 * This replaces the basic user_prompt_submit.ts with AI-level intelligence!
 */

import { spawnSync } from "bun";
import { logSuccess, logError, logSkip, findTuskCli } from "./hook-logger.ts";

interface WorkPattern {
  patterns: RegExp[];
  type: string;
  priority: number;
  complexity: number;
}

interface PromptAnalysis {
  workType: string;
  technologies: string[];
  complexity: number;
  priority: number;
  summary: string;
  confidence: number;
}

const WORK_PATTERNS: WorkPattern[] = [
  // High-priority development work
  {
    patterns: [
      /\b(implement|build|create|develop|code|write)\b.*\b(feature|functionality|component|module|system)\b/i,
      /\b(add|new)\b.*\b(feature|component|endpoint|api|function)\b/i,
    ],
    type: "feature-development",
    priority: 3,
    complexity: 3,
  },

  // Bug fixing and debugging
  {
    patterns: [
      /\b(fix|debug|resolve|solve)\b.*\b(bug|issue|problem|error|exception)\b/i,
      /\b(error|exception|crash|fail|failing|broken)\b/i,
      /\b(not working|doesn't work|broken|failing)\b/i,
    ],
    type: "bug-fix",
    priority: 4,
    complexity: 2,
  },

  // Architecture and refactoring
  {
    patterns: [
      /\b(refactor|restructure|reorganize|modularize)\b/i,
      /\b(architecture|design|structure|pattern)\b/i,
      /\b(improve|optimize|enhance|clean up)\b.*\b(code|codebase|structure)\b/i,
    ],
    type: "refactoring",
    priority: 2,
    complexity: 4,
  },

  // Testing and quality
  {
    patterns: [
      /\b(test|testing|spec|coverage|unit test|integration test)\b/i,
      /\b(write tests|add tests|test coverage)\b/i,
    ],
    type: "testing",
    priority: 2,
    complexity: 2,
  },

  // Performance and optimization
  {
    patterns: [
      /\b(optimize|performance|speed up|faster|efficiency)\b/i,
      /\b(slow|bottleneck|memory|cpu|performance issue)\b/i,
    ],
    type: "performance",
    priority: 3,
    complexity: 3,
  },

  // Security work
  {
    patterns: [
      /\b(security|vulnerability|auth|authentication|authorization|secure)\b/i,
      /\b(encrypt|decrypt|token|session|permission)\b/i,
    ],
    type: "security",
    priority: 4,
    complexity: 3,
  },

  // DevOps and deployment
  {
    patterns: [
      /\b(deploy|deployment|ci\/cd|pipeline|docker|kubernetes)\b/i,
      /\b(production|staging|environment|infrastructure)\b/i,
    ],
    type: "devops",
    priority: 3,
    complexity: 3,
  },

  // Documentation and analysis
  {
    patterns: [
      /\b(document|documentation|explain|analyze|review|investigate)\b/i,
      /\b(understand|learn|research|study)\b/i,
    ],
    type: "analysis",
    priority: 1,
    complexity: 1,
  },
];

const TECHNOLOGY_PATTERNS = [
  // Languages
  { pattern: /\b(typescript|javascript|python|rust|go|java|c\+\+|c#|php|ruby)\b/i, category: "language" },
  { pattern: /\b(html|css|scss|sass|less)\b/i, category: "frontend" },

  // Frameworks
  { pattern: /\b(react|vue|angular|svelte|nextjs|nuxt)\b/i, category: "frontend-framework" },
  { pattern: /\b(node|express|fastify|koa|nestjs)\b/i, category: "backend-framework" },
  { pattern: /\b(django|flask|rails|laravel|spring)\b/i, category: "backend-framework" },

  // Databases
  { pattern: /\b(mysql|postgresql|sqlite|mongodb|redis|elasticsearch)\b/i, category: "database" },

  // Cloud & DevOps
  { pattern: /\b(aws|azure|gcp|docker|kubernetes|terraform)\b/i, category: "devops" },

  // Tools
  { pattern: /\b(git|github|gitlab|jenkins|webpack|vite|rollup)\b/i, category: "tooling" },
];

function analyzePrompt(text: string): PromptAnalysis | null {
  let bestMatch: WorkPattern | null = null;
  let bestScore = 0;

  // Analyze against work patterns
  for (const workPattern of WORK_PATTERNS) {
    let score = 0;
    for (const pattern of workPattern.patterns) {
      const matches = text.match(new RegExp(pattern.source, 'gi'));
      if (matches) {
        score += matches.length * workPattern.priority;
      }
    }

    if (score > bestScore) {
      bestScore = score;
      bestMatch = workPattern;
    }
  }

  // Must have minimum score to be considered important
  if (bestScore < 3) {
    return null;
  }

  // Extract technologies
  const technologies: string[] = [];
  for (const techPattern of TECHNOLOGY_PATTERNS) {
    const matches = text.match(techPattern.pattern);
    if (matches) {
      technologies.push(matches[0].toLowerCase());
    }
  }

  // Calculate confidence based on multiple factors
  const hasSpecificTerms = technologies.length > 0;
  const hasCodeKeywords = /\b(function|class|method|api|endpoint|database|query)\b/i.test(text);
  const confidence = Math.min(
    0.3 + (bestScore / 20) + (hasSpecificTerms ? 0.2 : 0) + (hasCodeKeywords ? 0.2 : 0),
    1.0
  );

  return {
    workType: bestMatch!.type,
    technologies,
    complexity: bestMatch!.complexity,
    priority: bestMatch!.priority,
    summary: extractSmartSummary(text, bestMatch!.type),
    confidence,
  };
}

function extractSmartSummary(text: string, workType: string): string {
  // Remove common prefixes
  let cleaned = text.replace(/^(please|can you|could you|help me|i need to|let's|okay,?|ok,?)\s*/i, '');

  // Look for the core action/object
  const actionPatterns: Record<string, RegExp[]> = {
    "feature-development": [
      /\b(implement|build|create|add)\s+([^.!?]+)/i,
      /\b(new|build a)\s+([^.!?]+)/i,
    ],
    "bug-fix": [
      /\b(fix|debug|resolve)\s+([^.!?]+)/i,
      /\b(error|issue|problem)\s*:?\s*([^.!?]+)/i,
    ],
    "refactoring": [
      /\b(refactor|improve|optimize)\s+([^.!?]+)/i,
      /\b(clean up|restructure)\s+([^.!?]+)/i,
    ],
    "testing": [
      /\b(test|add tests for)\s+([^.!?]+)/i,
      /\b(write tests|test coverage)\s+([^.!?]+)/i,
    ],
  };

  const patterns = actionPatterns[workType] || [];
  for (const pattern of patterns) {
    const match = cleaned.match(pattern);
    if (match && match[1] && match[1].length > 5) {
      let result = match[1].trim();
      if (result.length > 80) {
        result = result.substring(0, 77) + "...";
      }
      return result;
    }
  }

  // Fallback to first meaningful sentence
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

  // Final fallback
  if (cleaned.length > 80) {
    return cleaned.substring(0, 77) + "...";
  }
  return cleaned || "Work request";
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

    const content = inputData.prompt || '';
    claudeCodeSessionId = inputData.session_id;

    // Skip very short prompts
    if (content.trim().length < 15) {
      logSkip("enhanced_user_prompt", "too short");
      process.exit(0);
    }

    // Analyze the prompt
    const analysis = analyzePrompt(content);
    if (!analysis) {
      logSkip("enhanced_user_prompt", "not work-related");
      process.exit(0);
    }

    // Skip low-confidence matches
    if (analysis.confidence < 0.4) {
      logSkip("enhanced_user_prompt", `low confidence: ${analysis.confidence.toFixed(2)}`);
      process.exit(0);
    }

    // Create rich description with metadata
    const description = `User ${analysis.workType}: ${analysis.summary}`;

    // Find tusk CLI
    const cliPath = findTuskCli(import.meta.path);
    if (!cliPath) {
      logError("enhanced_user_prompt", "CLI not found");
      process.exit(0);
    }

    // Build tags for rich metadata
    const tags = [
      "user-request",
      analysis.workType,
      `priority-${analysis.priority}`,
      `complexity-${analysis.complexity}`,
      ...analysis.technologies.map(t => `tech-${t}`),
    ];

    const cliArgs = [
      "bun", cliPath, "checkpoint", description,
      tags.join(","),
      "--entry-type=user-request",
      `--confidence=${analysis.confidence.toFixed(2)}`
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
      logSuccess("enhanced_user_prompt", `${analysis.workType}: ${analysis.summary.substring(0, 50)}...${sessionInfo}`);
      console.error(`✅ Enhanced prompt checkpoint: ${analysis.workType} (${analysis.technologies.join(', ') || 'general'})`);
    } else {
      const errorOutput = new TextDecoder().decode(result.stderr);
      logError("enhanced_user_prompt", errorOutput);
    }
  } catch (error) {
    logError("enhanced_user_prompt", String(error));
  }

  process.exit(0);
}

main();