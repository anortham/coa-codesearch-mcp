> Here’s a focused engineering review highlighting issues that could erode confidence, grouped by impact and with concrete fixes.

Critical

- Tree‑sitter runtime portability - macOS-only resolver and native P/Invoke path: Program.ConfigureTreeSitterNativeResolver() and
  TypeExtractionService.ExtractTypesWithNativeApiMac handle Darwin well, but Linux/Windows rely entirely on TreeSitter.DotNet to locate
  grammars that are often not packaged. This can silently disable type extraction on CI/Linux. - Tests copy only .dll files: COA.CodeSearch.McpServer.Tests.csproj copies tree-sitter*.dll (Windows only). No .dylib or .so for macOS/
  Linux, so type extraction tests can fail or be skipped implicitly. - Fixes: - Add a Linux resolver mirroring the macOS approach to probe `libtree-sitter*.so`in`/usr/lib`, `/usr/local/lib`, and
`$TREE_SITTER_NATIVE_PATHS`.
    - Add platform-conditional copy targets for tests:
      - macOS: copy `libtree-sitter*.dylib`      - Linux: copy`libtree-sitter*.so`      - Windows: keep`tree-sitter\*.dll`    - Provide a Linux build script like`scripts/build-grammars-linux.sh`(parity with`build-grammars-macos.sh`) and document it in README.
    - Gate type-extraction tests behind a feature flag or `[Platform]` attribute when grammars aren’t available; fail clearly with a helpful
  message instead of flakiness.

- Path length guard blocks valid paths - In PathResolutionService.ValidateWorkspacePath, paths > 240 characters are rejected on all platforms. That’s too strict for Linux/macOS
  and also unnecessary on modern Windows (long path support + .NET 9). - Fix: remove that check or make it Windows-only with a capability probe. Don’t reject long paths across the board.

High

- Unused/outdated ASP.NET Core references - COA.CodeSearch.McpServer.csproj targets net9.0 but references Microsoft.AspNetCore.Mvc.Core v2.2.5 and Swashbuckle.AspNetCore. Program
  runs STDIO only; HTTP API is removed. - These deps are legacy, add restore weight, and can confuse maintainers. - Fix: Remove Microsoft.AspNetCore.Mvc.Core and Swashbuckle.AspNetCore if HTTP mode is indeed gone. If stubs remain, replace with clear
  TODOs or keep a separate branch. - Fix: Remove Microsoft.AspNetCore.Mvc.Core and Swashbuckle.AspNetCore if HTTP mode is indeed gone. If stubs remain, replace with clear
  TODOs or keep a separate branch.
- Version string drift - Package version is 2.1.4 in COA.CodeSearch.McpServer.csproj while Program.WithServerInfo("CodeSearch", "2.0.0") and appsettings.json
  ("McpServer": { "Version": "2.0.0" }) are stale. - Fix: Align all version surfaces to 2.1.4 (or current). This matters for telemetry, support logs, and user trust.
- TypeExtraction recursion-guard is brittle - In TypeExtractionService, specialized analyzer loop avoidance uses !filePath.Contains(".ts")/... which can misfire when paths
  coincidentally contain those substrings. - Fix: Check by file extension only (e.g., Path.GetExtension(filePath)), or thread a reentrancy flag through the extraction call chain.
- Native node traversal lacks parent linkage on macOS - FindParentTypeNative returns null due to no parent linkage in NativeNodeAdapter, causing missing ContainingType for methods on macOS
  native path (results will be lower quality on macOS vs managed path). - Fix: Thread parent adapters when constructing children (e.g., add a parent field in NativeNodeAdapter and set it in Children
  enumeration) to enable parent traversal.

Medium

- Linux gaps in docs and scripts - README and docs/macos-tree-sitter.md are strong for macOS, but there’s no Linux equivalent. CI and devs on Linux will struggle to
  enable grammars. - Fix: Add a Linux section mirroring macOS instructions and include a build-grammars-linux.sh. - Fix: Add a Linux section mirroring macOS instructions and include a build-grammars-linux.sh.
- Case sensitivity assumptions in path logic - Several places normalize or compare paths with OrdinalIgnoreCase (e.g., DirectorySearchTool, some PathResolutionService operations). On
  Linux, directories can differ by case; collapsing case can mask issues. - Fix: Prefer case-sensitive compares unless you are explicitly normalizing per-OS. When stripping workspace prefixes, use case-sensitive
  compare on Linux.
- Security/safety of editing tools - InsertAtLineTool/ReplaceLinesTool/DeleteLinesTool validate file existence but don’t ensure the file is within a workspace. An agent
  could be asked to edit arbitrary system files. - Fix: Add an optional guard to restrict edits to the current indexed workspace root (configurable), returning a clear error when out-
  of-scope.
- README vs implementation drift - README mentions “Global Logging: ~/.coa/codesearch/logs”, but PathResolutionService uses per-workspace .coa/codesearch/logs under the
  primary workspace. This discrepancy can confuse users looking for logs. - Fix: Update README to match current behavior or add a configurable switch for global vs per-workspace logs.

Low

- Package bloat - Microsoft.VisualStudio.Azure.Containers.Tools.Targets and DockerDefaultTargetOS seem unrelated to CLI/STDIO use. They add noise to
  restore/build. - Fix: Remove unless actively used. - Fix: Remove unless actively used.
- Logging verbosity - Many tools log at Information level for routine operations. Defaults in appsettings.json mostly keep this tame, but consider reducing
  Info → Debug for high-frequency paths to keep logs lean in production.
- Test realism and portability - Some tests use Windows-style test paths (e.g., C:\source\... in FileSearchToolTests). While harmless, adding a couple of POSIX path
  samples will raise confidence on Linux/macOS.

Tests: quick repairs

- COA.CodeSearch.McpServer.Tests.csproj - Add platform-conditional ItemGroups: - macOS: `<TreeSitterFiles Include="$(OutDir)..\..\..\..\COA.CodeSearch.McpServer\bin\$(Configuration)\$(TargetFramework)\libtree-
sitter*.dylib" />` - Linux: `<TreeSitterFiles Include="$(OutDir)..\..\..\..\COA.CodeSearch.McpServer\bin\$(Configuration)\$(TargetFramework)\libtree-
sitter*.so" />` - Windows: keep `.dll`.
- Gate TypeExtraction tests behind availability checks:
  - E.g., before running, `NativeLibrary.TryLoad("tree-sitter", out _) || skip`.
  - Or use an env var like `CODESRCH_ENABLE_TYPEEX_TESTS=1`.

Cross‑platform checklist

- macOS: You’ve done well (resolver, build script, docs).
- Linux:
  - Add .so resolver and Linux build script + docs.
  - Add test copy target.
- Windows:
  - Tests already copy .dll. Keep the managed path (no native interop) and ensure grammars are available (via NuGet or local builds).

Quick wins (high ROI)

- Remove unused ASP.NET Core + Swashbuckle packages from COA.CodeSearch.McpServer.csproj.
- Align version strings in Program.cs and appsettings.json with the package version.
- Relax/OS-gate the 240-char path check.
- Fix the recursion guard to rely on extension instead of substring match.
- Add platform-aware test copy rules for tree-sitter libs; add Linux grammar script + doc.
