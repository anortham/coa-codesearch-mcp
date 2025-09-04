#!/usr/bin/env bash
set -euo pipefail

# Build and install Tree-sitter grammar dynamic libraries for macOS.
# - Defaults to Apple Silicon Homebrew prefix (/opt/homebrew)
# - Falls back to Intel Homebrew (/usr/local) or MacPorts (/opt/local)
# - Installs dylibs to DEST_DIR (defaults to <PREFIX>/lib)
# - Probes include paths via pkg-config and vendored headers
# - Supports custom install dir + env-driven probing via TREE_SITTER_NATIVE_PATHS
#
# Usage:
#   bash scripts/build-grammars-macos.sh             # Build default grammars (c-sharp, typescript, tsx, javascript)
#   DEST_DIR="$PWD/native" sh scripts/build-grammars-macos.sh
#   GRAMMARS="c-sharp javascript" sh scripts/build-grammars-macos.sh
#
# After using a custom DEST_DIR, set:
#   export TREE_SITTER_NATIVE_PATHS="$PWD/native:/opt/homebrew/lib:/usr/local/lib"

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "This script is intended for macOS (Darwin)." >&2
  exit 1
fi

# Detect a reasonable prefix
detect_prefix() {
  if command -v brew >/dev/null 2>&1; then
    brew --prefix || true
  fi
}

PREFIX_DEFAULT="$(detect_prefix)"
if [[ -z "${PREFIX_DEFAULT}" ]]; then
  if [[ -d /opt/homebrew ]]; then PREFIX_DEFAULT=/opt/homebrew; 
  elif [[ -d /usr/local ]]; then PREFIX_DEFAULT=/usr/local; 
  elif [[ -d /opt/local ]]; then PREFIX_DEFAULT=/opt/local; 
  else PREFIX_DEFAULT=/opt/homebrew; fi
fi

PREFIX="${PREFIX:-$PREFIX_DEFAULT}"
DEST_DIR="${DEST_DIR:-$PREFIX/lib}"
INCLUDE_DIR="${INCLUDE_DIR:-$PREFIX/include}"
REPOS_DIR="${REPOS_DIR:-$PWD/vendor/grammars}"
mkdir -p "$DEST_DIR" "$REPOS_DIR"

# Default set focused on this project’s needs
GRAMMARS=${GRAMMARS:-"c-sharp typescript tsx javascript python go rust java json html css bash"}

echo "Using settings:"
echo "  PREFIX     = $PREFIX"
echo "  DEST_DIR   = $DEST_DIR"
echo "  INCLUDE    = $INCLUDE_DIR"
echo "  REPOS_DIR  = $REPOS_DIR"
echo "  GRAMMARS   = $GRAMMARS"
echo

need_cmd() { command -v "$1" >/dev/null 2>&1 || { echo "Missing required command: $1" >&2; exit 1; }; }
need_cmd cc
need_cmd c++

ensure_repo() {
  local repo_url="$1"; shift
  local dir="$1"; shift
  if [[ ! -d "$dir/.git" ]]; then
    echo "Cloning $repo_url -> $dir"
    git clone --depth 1 "$repo_url" "$dir"
  else
    echo "Updating $dir"
    (cd "$dir" && git pull --ff-only || true)
  fi
}

has_header_at() {
  local root="$1"
  [[ -f "$root/tree_sitter/parser.h" ]]
}

# Discover extra include flags for tree-sitter headers
discover_include_flags() {
  local -a flags

  # 1) Respect explicit INCLUDE_DIR
  if [[ -n "${INCLUDE_DIR:-}" ]]; then
    flags+=(-I "$INCLUDE_DIR")
  fi

  # 2) pkg-config (if available) for system installs
  if command -v pkg-config >/dev/null 2>&1 && pkg-config --exists tree-sitter 2>/dev/null; then
    # shellcheck disable=SC2207
    flags+=($(pkg-config --cflags tree-sitter 2>/dev/null)) || true
  fi

  # 3) Vendored headers from any cloned grammar repo (e.g. c-sharp has src/tree_sitter)
  if [[ -d "$REPOS_DIR" ]]; then
    while IFS= read -r -d '' dir; do
      if has_header_at "$dir/src"; then
        flags+=(-I "$dir/src")
      fi
    done < <(find "$REPOS_DIR" -maxdepth 2 -mindepth 1 -type d -print0 2>/dev/null)
  fi

  printf '%s\n' "${flags[@]}"
}

compile_grammar() {
  local name="$1"; shift
  local dir="$1"; shift
  local parser_c="$dir/src/parser.c"
  local scanner_c="$dir/src/scanner.c"
  local scanner_cc="$dir/src/scanner.cc"
  local out="$DEST_DIR/libtree-sitter-${name}.dylib"

  if [[ ! -f "$parser_c" ]]; then
    echo "Missing parser source: $parser_c" >&2
    exit 1
  fi

  # Optimization flags: avoid deprecated -Ofast warnings on newer clang
  local -a base_cflags=(-O3 -ffast-math -fPIC -dynamiclib)
  local -a inc_flags
  # shellcheck disable=SC2207
  inc_flags=($(discover_include_flags))

  # If we still don't have a usable header, hint clearly
  if [[ ! -f "$INCLUDE_DIR/tree_sitter/parser.h" ]]; then
    # Try to detect at least one vendored header to confirm we have coverage
    local found_vendor=false
    # Silence missing-glob errors by using nullglob
    local had_glob=false
    shopt -s nullglob
    for f in "${REPOS_DIR}"/*/src/tree_sitter/parser.h; do
      had_glob=true
      if [[ -f "$f" ]]; then found_vendor=true; break; fi
    done
    shopt -u nullglob
    if [[ "$found_vendor" != true ]]; then
      echo "Warning: Could not find tree_sitter/parser.h in INCLUDE_DIR ($INCLUDE_DIR) or vendored repos." >&2
      echo "         Ensure 'tree-sitter' C library headers are installed (e.g. 'brew install tree-sitter')." >&2
    fi
  fi

  local -a cmd=(cc "${base_cflags[@]}" "${inc_flags[@]}" -o "$out" "$parser_c")
  if [[ -f "$scanner_cc" ]]; then
    cmd=(c++ -std=c++17 "${base_cflags[@]}" "${inc_flags[@]}" -o "$out" "$parser_c" "$scanner_cc")
  elif [[ -f "$scanner_c" ]]; then
    cmd=(cc "${base_cflags[@]}" "${inc_flags[@]}" -o "$out" "$parser_c" "$scanner_c")
  fi

  echo "Building $out"
  "${cmd[@]}"
}

build_csharp() {
  local repo="$REPOS_DIR/tree-sitter-c-sharp"
  ensure_repo https://github.com/tree-sitter/tree-sitter-c-sharp "$repo"
  compile_grammar "c-sharp" "$repo"
}

build_javascript() {
  local repo="$REPOS_DIR/tree-sitter-javascript"
  ensure_repo https://github.com/tree-sitter/tree-sitter-javascript "$repo"
  compile_grammar "javascript" "$repo"
}

build_typescript() {
  local repo="$REPOS_DIR/tree-sitter-typescript"
  ensure_repo https://github.com/tree-sitter/tree-sitter-typescript "$repo"
  compile_grammar "typescript" "$repo/typescript"
}

build_tsx() {
  local repo="$REPOS_DIR/tree-sitter-typescript"
  ensure_repo https://github.com/tree-sitter/tree-sitter-typescript "$repo"
  compile_grammar "tsx" "$repo/tsx"
}

build_python() {
  local repo="$REPOS_DIR/tree-sitter-python"
  ensure_repo https://github.com/tree-sitter/tree-sitter-python "$repo"
  compile_grammar "python" "$repo"
}

build_go() {
  local repo="$REPOS_DIR/tree-sitter-go"
  ensure_repo https://github.com/tree-sitter/tree-sitter-go "$repo"
  compile_grammar "go" "$repo"
}

build_rust() {
  local repo="$REPOS_DIR/tree-sitter-rust"
  ensure_repo https://github.com/tree-sitter/tree-sitter-rust "$repo"
  compile_grammar "rust" "$repo"
}

build_java() {
  local repo="$REPOS_DIR/tree-sitter-java"
  ensure_repo https://github.com/tree-sitter/tree-sitter-java "$repo"
  compile_grammar "java" "$repo"
}

build_json() {
  local repo="$REPOS_DIR/tree-sitter-json"
  ensure_repo https://github.com/tree-sitter/tree-sitter-json "$repo"
  compile_grammar "json" "$repo"
}

build_html() {
  local repo="$REPOS_DIR/tree-sitter-html"
  ensure_repo https://github.com/tree-sitter/tree-sitter-html "$repo"
  compile_grammar "html" "$repo"
}

build_css() {
  local repo="$REPOS_DIR/tree-sitter-css"
  ensure_repo https://github.com/tree-sitter/tree-sitter-css "$repo"
  compile_grammar "css" "$repo"
}

build_bash() {
  local repo="$REPOS_DIR/tree-sitter-bash"
  ensure_repo https://github.com/tree-sitter/tree-sitter-bash "$repo"
  compile_grammar "bash" "$repo"
}

main() {
  for g in $GRAMMARS; do
    case "$g" in
      c-sharp|csharp) build_csharp ;;
      javascript|js) build_javascript ;;
      typescript|ts) build_typescript ;;
      tsx) build_tsx ;;
      python|py) build_python ;;
      go) build_go ;;
      rust) build_rust ;;
      java) build_java ;;
      json) build_json ;;
      html) build_html ;;
      css) build_css ;;
      bash|sh) build_bash ;;
      *) echo "Unknown grammar: $g" >&2; exit 1 ;;
    esac
  done

  echo
  echo "Done. Installed dylibs:"
  ls -1 "$DEST_DIR"/libtree-sitter-*.dylib || true
  echo
  echo "If you used a custom DEST_DIR, set:"
  echo "  export TREE_SITTER_NATIVE_PATHS=\"$DEST_DIR:/opt/homebrew/lib:/usr/local/lib\""
}

main "$@"
