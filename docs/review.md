Here’s a focused quality review of the CodeSearch MCP Server with an eye toward reliability of the new Tree‑sitter type extraction and the
line-editing tools.

What’s Solid

- Editing foundation: Centralized file utilities (FileLineUtilities) handle encoding detection, line splitting, and newline preservation
  correctly, and tools use it when writing. This is the right approach to avoid corruption.
- Golden Master tests: Real-file copy→edit→diff methodology (with DiffPlex and custom validation) for Delete/Insert/Replace gives strong
  regression protection.
- Type extraction integration: Thoughtful Tree‑sitter setup with platform-aware resolver, native fallback on macOS, graceful degradation for
  unsupported languages, and Lucene storage (type_info) the tools re-use.
- Index lifecycle: Lucene index is guarded by WriteLockManager, explicit lock recovery, cleanup, and disposal; metrics and circuit breaker
  patterns present.

Critical Risks / Gaps

- SearchAndReplace multi-line matching doesn’t actually work: - Matching is line-based (scans each line with ContainsPattern(line)), so multi-line patterns in manifests (e.g., replacing two-line YAML
  frontmatter) will never match as intended. AdvancedPatternMatcher supports a “multiline” mode but is called against a single line string, not
  the full content. - Impact: Tests that expect multi-line replacements are likely failing silently or not meaningfully validating. Functionality gap vs
  README claims.
- Multiple occurrences per line not consistently replaced: - Regex mode replaces all matches per line, but “literal”/“fuzzy” modes use AdvancedPatternMatcher.PerformReplacement which replaces only
  the first occurrence. This will miss second+ hits on a line.
- Concurrency on file edits: Insert/Replace/Delete tools perform write operations without per-file locking. Concurrent edits (tests try 5
  at once) can succeed “some” of the time, but there’s no guarantee the file isn’t left partially corrupted on races. This is a real-world
  reliability risk for agent workflows.
- Unbounded edit scope: Edit tools accept any absolute path and do not restrict to configured/allowed workspaces. That’s powerful but risky
  in an agent context (accidental edits outside the project). Consider a safe-guard or allowlist.

Test Suite Observations

- Golden Master coverage is a strong base: Python/JSON/C# examples and line-ending/encoding preservation checks via TestFileManager and
  DiffValidator.
- Weak assertions in some unit tests: - SearchAndReplaceToolTests include tests that assert only response.Success (with comments “framework might be catching exceptions…”) and
  TODOs. These don’t validate correctness or error semantics (e.g., index not found, workspace unresolvable).
- Missing high-signal cases:
  - Multi-line search/replace end-to-end (should fail today; exposes the gap).
  - Multiple literal occurrences per line replacement.
  - BOM-preserving editing (some coverage via TestFileManager; add explicit Golden Master for BOM vs no‑BOM).
  - Mixed line endings inside single files (CRLF/LF) to ensure preservation logic holds.

Code Smells / Inconsistencies

- Duplicate path validation helpers: - ValidateAndResolvePath is implemented in tools (Delete/Insert/Replace) and also in FileLineUtilities. The tools inject
  IPathResolutionService but do not use it. Standardize on a single path resolver to avoid drift.
- Console debug prints: - FindPatternsTool uses System.Console.WriteLine for debug which bypasses the logger and can leak into STDIO responses. Use the logger
  consistently.
- SearchAndReplace’s “smart” matching pipeline is mismatched with how matches are applied: - The AdvancedPatternMatcher supports multi-line and whitespace-insensitive modes, but is run against line strings, not the whole
  content. This is misleading and causes non-functional modes.
- Ambiguous success signaling: - Some tools create success responses while returning error details in Data/Result; some tests consequently only check response.Success.
  Clarify a consistent contract for error propagation (top-level vs Data-level).

Functional Gaps

- SearchAndReplace - Multi-line patterns: Not supported in practice; yet documented/suggested. - Per-line multiple matches in literal/fuzzy modes: Only first match replaced. - No atomic batch write strategy: If a file has multiple line changes and one fails, partial application occurs. - No file backup/undo path: Delete/Insert/Replace tools return deleted/original content, but SearchAndReplace does not provide an easy
  rollback story.
- Editing Tools - No per-file synchronization: Concurrent edits can interleave writes. Add a per-file semaphore keyed by absolute path. - Workspace safety rail: No enforcement binding edits to a workspace root. Add an opt-in setting to restrict edits to within
  workspacePath or .git root.

Tree‑sitter Integration

- Platform-aware loader and native resolver look good. However: - Unsupported languages are listed but also mapped to extensions; extraction always fails for them. Ensure messaging and docs are
  explicit that type features will degrade for Go/Swift/etc. (you did note limited support in README). - GetSymbolsOverviewTool falls back to direct extraction if index doesn’t contain type_info. Good. Add telemetry that counts extraction
  fallbacks vs index hits to monitor performance in larger repos.

Performance / Safety

- File writes preserve original line endings using WriteAllLinesPreservingEndingsAsync. This is careful and correct; one caveat: - SplitLines trims a single trailing empty line. It preserves whether original file ended with newline, but cannot represent multiple
  trailing blank lines distinctly. If you need exact trailing blank line preservation, carry last-N trailing newline counts in metadata when
  splitting.
- Lucene service lifecycle and write-lock recovery is robust, including force removals and repair flow. Good.

Docs

- README is comprehensive with Tree‑sitter setup for macOS/Linux including native path environment variances. Consider surfacing “limited
  support languages” in tool error messages, not only README.

High-Value Fixes (Short Term)

- Fix multi-line SearchAndReplace: - Detect when search pattern contains line breaks or when matchMode=multiline; read full file content and apply pattern across the file,
  not per-line. - Rebuild per-file edits from a single transformed content string to apply once, preserving encoding/newlines via
  WriteAllLinesPreservingEndingsAsync.
- Replace multiple literal occurrences per line: - For literal/whitespace-insensitive/fuzzy, loop until no more matches per line or implement a global content replacement strategy for
  those modes.
- Add per-file write locks: - Introduce a ConcurrentDictionary<string, SemaphoreSlim> in a shared editing service. Acquire around all writes in Delete/Insert/
  Replace/SearchAndReplace to serialize file modifications.
- Use IPathResolutionService consistently: - Replace duplicated ValidateAndResolvePath code in tools with FileLineUtilities.ValidateAndResolvePath or methods on
  IPathResolutionService.
- Remove console debugging:
  - In FindPatternsTool, replace System.Console.WriteLine with \_logger.LogDebug.

Test Improvements (Short Term)

- Add failing Golden Masters that expose current gaps:
  - Multi-line replacement at file start and across paragraphs.
  - Lines with multiple occurrences (literal) replaced all.
- Tighten assertions in SearchAndReplaceToolTests: - Assert that response.Success and response.Data.Results.Success semantics match intended behavior, or standardize on a single definitive
  status field. - Replace “framework might be catching exceptions” tests with precise assertions on error codes/messages in the response payload.
- Add concurrency stress test for editing tools, then validate structure is intact and no partial merges happen (should pass once per-file
  locks are added).

Medium-Term Enhancements

- Workspace guardrail for editing tools:
  - Add an optional parameter or config to constrain edits to a given workspacePath, returning a safe error otherwise.
- Unified Edit API surface: - Provide a single “edit engine” used by Delete/Insert/Replace/SearchAndReplace to share locking, path resolution, read+write logic, and
  context generation.
- Type extraction telemetry:
  - Track rate of index hits for type_info vs direct extraction; log language coverage/unsupported hits to refine grammar setup.

Notable Files to Adjust

- Multi-line matching and multiple replacements:
  - COA.CodeSearch.McpServer/Tools/SearchAndReplaceTool.cs
  - COA.CodeSearch.McpServer/Services/AdvancedPatternMatcher.cs
- Write synchronization:
  - Shared editing service or augment FileLineUtilities to provide per-file semaphores.
- Path resolution consistency:
  - InsertAtLineTool.cs, ReplaceLinesTool.cs, DeleteLinesTool.cs → drop local ValidateAndResolvePath, use centralized service/util.
- Logging cleanup:
  - FindPatternsTool.cs → remove Console.WriteLine
- Tests:
  - COA.CodeSearch.McpServer.Tests/Integration/WorkingGoldenMasterTests.cs
  - COA.CodeSearch.McpServer.Tests/Tools/SearchAndReplaceToolTests.cs
  - Add new Golden Masters under Resources/GoldenMaster/Manifests/ for multi-line and multi-occurrence cases.

Bottom Line

- The core architecture and most of the editing plumbing are strong. Encoding and newline preservation are handled carefully. Type extraction
  is thoughtfully integrated with platform nuances.
- The biggest reliability gap today is SearchAndReplace’s mismatch between advertised capabilities and actual line-based implementation —
  especially multi-line and multiple literal occurrences — and lack of per-file locking across all edit tools.
- Addressing these (plus small consistency and testing fixes) will materially increase confidence for agent-driven, unattended refactors
  and edits.
