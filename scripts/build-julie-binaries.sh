#!/bin/bash
# Cross-platform build script for julie-codesearch binaries
# Builds for macOS (ARM64/x64), Linux (x64), and Windows (x64)

set -e

JULIE_REPO="${JULIE_REPO:-$HOME/Source/julie}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
OUTPUT_DIR="$SCRIPT_DIR/../bin/julie-binaries"

echo "🔨 Building julie-codesearch binaries for distribution"
echo "📁 Julie repo: $JULIE_REPO"
echo "📦 Output: $OUTPUT_DIR"
echo ""

# Ensure output directory exists
mkdir -p "$OUTPUT_DIR"

# Change to Julie repo
cd "$JULIE_REPO"

# Verify we're in the right place
if [ ! -f "Cargo.toml" ]; then
    echo "❌ Error: Not in Julie repository (no Cargo.toml found)"
    echo "   Set JULIE_REPO environment variable or run from correct directory"
    exit 1
fi

# Check if julie-codesearch binary exists in Cargo.toml
if ! grep -q 'name = "julie-codesearch"' Cargo.toml; then
    echo "❌ Error: julie-codesearch binary not found in Cargo.toml"
    exit 1
fi

echo "✅ Found julie-codesearch in Cargo.toml"
echo ""

# Function to build for a target
build_target() {
    local target=$1
    local output_name=$2

    echo "🔨 Building for $target..."

    # Add target if not already installed
    rustup target add "$target" 2>/dev/null || true

    # Build
    cargo build --release --bin julie-codesearch --target "$target"

    # Copy to output directory with platform-specific name
    local binary_name="julie-codesearch"
    if [[ "$target" == *"windows"* ]]; then
        binary_name="julie-codesearch.exe"
    fi

    cp "target/$target/release/$binary_name" "$OUTPUT_DIR/$output_name"

    # Show size
    ls -lh "$OUTPUT_DIR/$output_name" | awk '{print "   Size:", $5}'
    echo ""
}

# Build for all platforms
echo "🚀 Starting cross-platform builds..."
echo ""

# macOS ARM64 (M1/M2/M3 Macs)
build_target "aarch64-apple-darwin" "julie-codesearch-macos-arm64"

# macOS x64 (Intel Macs)
build_target "x86_64-apple-darwin" "julie-codesearch-macos-x64"

# Linux x64 (most common)
build_target "x86_64-unknown-linux-gnu" "julie-codesearch-linux-x64"

# Windows x64
# Note: This requires mingw-w64 toolchain
if command -v x86_64-w64-mingw32-gcc &> /dev/null; then
    build_target "x86_64-pc-windows-gnu" "julie-codesearch-windows.exe"
else
    echo "⚠️  Skipping Windows build (mingw-w64 not installed)"
    echo "   Install with: brew install mingw-w64"
    echo ""
fi

echo "✅ Build complete!"
echo ""
echo "📦 Binaries created in: $OUTPUT_DIR"
ls -lh "$OUTPUT_DIR"
echo ""
echo "🔍 Verify binaries:"
echo "   macOS ARM64: $OUTPUT_DIR/julie-codesearch-macos-arm64"
echo "   macOS x64:   $OUTPUT_DIR/julie-codesearch-macos-x64"
echo "   Linux x64:   $OUTPUT_DIR/julie-codesearch-linux-x64"
if [ -f "$OUTPUT_DIR/julie-codesearch-windows.exe" ]; then
    echo "   Windows x64: $OUTPUT_DIR/julie-codesearch-windows.exe"
fi
echo ""
echo "📝 Next steps:"
echo "   1. Test binaries on each platform"
echo "   2. Run: dotnet pack to bundle in NuGet package"
echo "   3. Publish package: dotnet nuget push"
