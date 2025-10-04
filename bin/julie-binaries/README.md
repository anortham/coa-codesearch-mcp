# Julie Binaries for Distribution

This directory contains cross-compiled `julie-codesearch` binaries bundled with the CodeSearch NuGet package.

## Building Binaries

Run the build script to create binaries for all platforms:

```bash
cd ../../scripts
./build-julie-binaries.sh
```

This will create:
- `julie-codesearch-macos-arm64` - macOS ARM64 (M1/M2/M3)
- `julie-codesearch-macos-x64` - macOS Intel
- `julie-codesearch-linux-x64` - Linux x64
- `julie-codesearch-windows.exe` - Windows x64 (requires mingw-w64)

## Requirements

- Rust toolchain installed
- Cross-compilation targets: `rustup target add <target>`
- For Windows builds: `brew install mingw-w64` (macOS) or equivalent

## Testing Binaries

Test each binary:

```bash
./julie-codesearch-macos-arm64 --version
./julie-codesearch-linux-x64 --help
```

## Packaging

Binaries are automatically included in the NuGet package via .csproj configuration.
They are copied to platform-specific runtime folders at build time.

## Size Considerations

Each binary is ~45-50MB (statically linked Rust binary with tree-sitter grammars).
Total package size with all platforms: ~150-200MB.

## Git

Binaries are NOT checked into git (see .gitignore).
Build them locally or download from releases before packaging.
