macOS setup for Tree-sitter (Homebrew-based)

Overview
- This project uses Tree-sitter via `TreeSitter.DotNet`. On macOS, the NuGet package does not ship Darwin native libraries.
- We now resolve Tree-sitter native libraries from Homebrew/MacPorts using a DllImportResolver in `Program.cs`.

Install core runtime
- Apple Silicon: `brew install tree-sitter` (installs `/opt/homebrew/lib/libtree-sitter.dylib`)
- Intel mac: `brew install tree-sitter` (installs `/usr/local/lib/libtree-sitter.dylib`)

Build required grammar dylibs
- Easiest: run the helper script (it clones repos and compiles):
```
# Default builds: c-sharp, typescript, tsx, javascript, python, go, rust, java, json, html, css, bash
sh scripts/build-grammars-macos.sh

# To install into a local folder instead of Homebrew, then point the resolver to it:
DEST_DIR="$PWD/native" sh scripts/build-grammars-macos.sh
export TREE_SITTER_NATIVE_PATHS="$PWD/native:/opt/homebrew/lib:/usr/local/lib"
```

- Or build manually for each grammar you need. Typical commands (adjust paths for Intel mac by replacing `/opt/homebrew` with `/usr/local`). Below are common examples; the script automates these.

TypeScript
```
git clone https://github.com/tree-sitter/tree-sitter-typescript.git
cd tree-sitter-typescript/typescript
c++ -Ofast -fPIC -I /opt/homebrew/include -dynamiclib -o /opt/homebrew/lib/libtree-sitter-typescript.dylib src/parser.c src/scanner.cc
```

TSX
```
cd ../tsx
c++ -Ofast -fPIC -I /opt/homebrew/include -dynamiclib -o /opt/homebrew/lib/libtree-sitter-tsx.dylib src/parser.c src/scanner.cc
```

JavaScript
```
git clone https://github.com/tree-sitter/tree-sitter-javascript.git
cd tree-sitter-javascript
c++ -Ofast -fPIC -I /opt/homebrew/include -dynamiclib -o /opt/homebrew/lib/libtree-sitter-javascript.dylib src/parser.c src/scanner.cc
```

C#
```
git clone https://github.com/tree-sitter/tree-sitter-c-sharp.git
cd tree-sitter-c-sharp
cc -Ofast -fPIC -I /opt/homebrew/include -dynamiclib -o /opt/homebrew/lib/libtree-sitter-c-sharp.dylib src/parser.c src/scanner.c
```

Optional examples
- Python:
```
git clone https://github.com/tree-sitter/tree-sitter-python.git
cd tree-sitter-python
cc -Ofast -fPIC -I /opt/homebrew/include -dynamiclib -o /opt/homebrew/lib/libtree-sitter-python.dylib src/parser.c src/scanner.c
```

Verification
- List installed dylibs: `ls /opt/homebrew/lib/libtree-sitter*.dylib`
- Inspect linkage: `otool -L /opt/homebrew/lib/libtree-sitter.dylib`

Custom locations
- Set `TREE_SITTER_NATIVE_PATHS` to a colon-separated list of directories containing the dylibs.
- Example: `export TREE_SITTER_NATIVE_PATHS="$PWD/native:/opt/homebrew/lib"`

Notes
- Library names must match `libtree-sitter-<name>.dylib` and export `tree_sitter_<name>`.
- We special-case C# in code to use `tree-sitter-c-sharp` / `tree_sitter_c_sharp`.
Go
```
git clone https://github.com/tree-sitter/tree-sitter-go.git
cd tree-sitter-go
cc -Ofast -fPIC -I /opt/homebrew/include -dynamiclib -o /opt/homebrew/lib/libtree-sitter-go.dylib src/parser.c src/scanner.c
```

Rust
```
git clone https://github.com/tree-sitter/tree-sitter-rust.git
cd tree-sitter-rust
cc -Ofast -fPIC -I /opt/homebrew/include -dynamiclib -o /opt/homebrew/lib/libtree-sitter-rust.dylib src/parser.c src/scanner.c
```

Java
```
git clone https://github.com/tree-sitter/tree-sitter-java.git
cd tree-sitter-java
cc -Ofast -fPIC -I /opt/homebrew/include -dynamiclib -o /opt/homebrew/lib/libtree-sitter-java.dylib src/parser.c src/scanner.c
```

JSON
```
git clone https://github.com/tree-sitter/tree-sitter-json.git
cd tree-sitter-json
cc -Ofast -fPIC -I /opt/homebrew/include -dynamiclib -o /opt/homebrew/lib/libtree-sitter-json.dylib src/parser.c src/scanner.c
```

HTML
```
git clone https://github.com/tree-sitter/tree-sitter-html.git
cd tree-sitter-html
cc -Ofast -fPIC -I /opt/homebrew/include -dynamiclib -o /opt/homebrew/lib/libtree-sitter-html.dylib src/parser.c src/scanner.c
```

CSS
```
git clone https://github.com/tree-sitter/tree-sitter-css.git
cd tree-sitter-css
cc -Ofast -fPIC -I /opt/homebrew/include -dynamiclib -o /opt/homebrew/lib/libtree-sitter-css.dylib src/parser.c src/scanner.c
```

Bash
```
git clone https://github.com/tree-sitter/tree-sitter-bash.git
cd tree-sitter-bash
c++ -Ofast -fPIC -I /opt/homebrew/include -dynamiclib -o /opt/homebrew/lib/libtree-sitter-bash.dylib src/parser.c src/scanner.cc
```
