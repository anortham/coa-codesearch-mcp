# CodeSearch MCP Server - Test Suite

This test project provides comprehensive validation of the CodeSearch MCP Server's editing tools using real-world files and Golden Master testing methodology.

## 🎯 Testing Philosophy

**Real Testing Over Theater**: We test actual behavior with realistic data, not mocked scenarios with hardcoded values. Our editing tools work on real files from actual projects to ensure they handle production scenarios correctly.

### Core Testing Principles

1. **Golden Master Testing**: Compare actual tool output against known-good reference files
2. **Real-World File Testing**: Use actual files from the serena project, not synthetic test content
3. **DiffPlex Validation**: Sophisticated file comparison that detects encoding, line ending, and content changes
4. **Production-Ready Validation**: Ensure tools don't corrupt files or lose encoding/line endings

## 🏗️ Test Architecture

### Directory Structure

```
COA.CodeSearch.McpServer.Tests/
├── Integration/
│   └── WorkingGoldenMasterTests.cs      # Main Golden Master test suite
├── Resources/
│   └── GoldenMaster/
│       ├── Sources/                     # Original files for testing
│       │   ├── agent.py                 # Python file from serena project
│       │   ├── jinja_template.py        # Jinja template processor
│       │   ├── devcontainer.json        # VS Code container config
│       │   └── SampleFileUtilities.cs   # C# utilities for testing
│       ├── Controls/                    # Expected results after editing
│       │   ├── agent_delete_lines_5_7.py
│       │   ├── jinja_template_insert_import.py
│       │   ├── devcontainer_insert_extension.json
│       │   └── SampleFileUtilities_insert_method.cs
│       └── Manifests/
│           └── real_world_tests.json    # Test case definitions
├── Helpers/
│   ├── DiffValidator.cs                 # File comparison engine
│   └── TestFileManager.cs               # Test file lifecycle management
└── README.md                           # This file
```

## 🧪 Golden Master Testing Methodology

### What is Golden Master Testing?

Golden Master testing is a technique where you:
1. **Capture known-good output** from your system (the "golden master")
2. **Run your system** and compare its output to the golden master
3. **Any differences indicate** either bugs or intentional changes that need review

### Our Implementation

#### 1. Test Case Definition (`real_world_tests.json`)

```json
{
  "TestCases": [
    {
      "TestName": "DeleteLines_PythonAgent_RemoveImports",
      "Description": "Delete import multiprocessing and import os from Python agent file",
      "SourceFile": "agent.py",
      "ControlFile": "agent_delete_lines_5_7.py",
      "Operation": {
        "Tool": "DeleteLinesTool",
        "StartLine": 5,
        "EndLine": 6,
        "ContextLines": 3
      }
    }
  ]
}
```

#### 2. Test Execution Flow

1. **Copy Source File**: Create a working copy of the original file
2. **Apply Edit Operation**: Use the specified editing tool (Insert/Replace/Delete)
3. **Compare Results**: Use DiffPlex to compare against the control file
4. **Validate Integrity**: Ensure encoding, line endings, and content are preserved

#### 3. Validation Categories

- **Content Changes**: Verify the edit was applied correctly
- **Encoding Preservation**: Maintain original file encoding (UTF-8, UTF-16, etc.)
- **Line Ending Preservation**: Keep original line endings (LF, CRLF, CR)
- **Structure Integrity**: Ensure file structure remains valid

## 🛠️ Editing Tools Tested

### DeleteLinesTool
- **Purpose**: Remove specific line ranges from files
- **Test Cases**: Remove Python imports, delete method implementations
- **Validation**: Ensure surrounding content is unchanged

### InsertAtLineTool  
- **Purpose**: Insert new content at specific line positions
- **Test Cases**: Add imports, insert methods, add JSON array elements
- **Validation**: Ensure proper indentation and syntax preservation

### ReplaceLinesTool
- **Purpose**: Replace line ranges with new content
- **Test Cases**: Replace method implementations, update configurations
- **Validation**: Ensure replacement maintains file structure

## 🔧 Test Infrastructure

### DiffValidator (`Helpers/DiffValidator.cs`)

Sophisticated file comparison engine that:
- Detects encoding differences (UTF-8, UTF-16, BOM handling)
- Preserves original line endings (LF vs CRLF vs CR)
- Provides detailed diff reports with context
- Validates edit operations against expectations

```csharp
var diffResult = DiffValidator.ValidateEdit(actualFile, expectedFile, new EditExpectation
{
    RequireEncodingPreservation = true,
    RequireLineEndingPreservation = true,
    AllowedOperations = EditOperationType.Insert,
    TargetLineRange = (13, 13)
});
```

### TestFileManager (`Helpers/TestFileManager.cs`)

Manages test file lifecycle:
- Creates isolated copies of source files
- Provides cleanup after test completion
- Ensures tests don't interfere with each other

## 📊 Current Test Coverage

### File Types Tested
- **Python**: `.py` files with imports, methods, classes
- **JSON**: `.json` configuration files with arrays and objects  
- **C#**: `.cs` source files with methods and documentation
- **YAML**: `.yml` configuration files (planned)
- **Markdown**: `.md` documentation files (planned)

### Encoding Scenarios
- **UTF-8 without BOM**: Most common encoding
- **UTF-8 with BOM**: Windows-specific encoding (planned)
- **UTF-16**: Unicode encoding (planned)

### Test Results (Current)
- **Total Tests**: 6
- **Passing**: 6 (100%)
- **File Types**: 3 (Python, JSON, C#)
- **Tools Covered**: 3 (Delete, Insert, Replace)

## 🚀 Running the Tests

### Run All Golden Master Tests
```bash
dotnet test --filter "WorkingGoldenMasterTests"
```

### Run Specific Test
```bash
dotnet test --filter "GoldenMaster_DeleteLines_PythonAgent_RemoveImports"
```

### Test Output Example
```
✅ DeleteLines_PythonAgent_RemoveImports: Golden master validation passed
✅ InsertAtLine_JSON_AddExtension: Golden master validation passed
✅ ReplaceLines_PythonJinja_EnhanceMethod: Golden master validation passed
```

## 📝 Adding New Tests

### 1. Add Source File
Place your test file in `Resources/GoldenMaster/Sources/`

### 2. Create Control File  
Manually edit the source file to show expected results, save in `Resources/GoldenMaster/Controls/`

### 3. Update Test Manifest
Add your test case to `Resources/GoldenMaster/Manifests/real_world_tests.json`

### 4. Verify Test
Run the test suite to ensure your new test passes

## 🎭 Why Golden Master Testing?

### Traditional Testing Challenges
- **Synthetic Data**: "test123" strings don't reveal real-world issues
- **Mocking Everything**: Tests that mock all dependencies test nothing meaningful
- **Happy Path Only**: Ignoring error conditions and edge cases
- **Implementation Testing**: Testing how code works instead of what it does

### Golden Master Benefits
- **Real Data**: Uses actual files from production systems
- **Integration Testing**: Tests the complete workflow, not isolated units
- **Regression Detection**: Immediately catches unintended changes
- **Documentation**: Test files serve as examples of expected behavior

### Example: Why We Don't Mock File Operations

❌ **Traditional Approach**:
```csharp
var mockFileSystem = new Mock<IFileSystem>();
mockFileSystem.Setup(x => x.ReadAllText("test.txt")).Returns("Hello World");
// This tests nothing about actual file handling
```

✅ **Golden Master Approach**:
```csharp
var testFile = await _fileManager.CreateTestCopyAsync("actual_python_file.py");
var result = await _insertTool.ExecuteAsync(new InsertAtLineParameters { ... });
DiffValidator.ValidateEdit(testFile.FilePath, "expected_result.py", expectations);
// This tests real file operations with real content
```

## 🔍 Debugging Test Failures

### When a Test Fails

1. **Check the Diff Report**: The test output includes detailed differences
2. **Examine Context**: Look at surrounding lines for clues
3. **Verify Control File**: Ensure the expected result is correct
4. **Check Tool Behavior**: The tool might be working correctly but expectations are wrong

### Common Issues

- **Line Ending Mismatches**: Mixed LF/CRLF in source vs control files
- **Encoding Differences**: BOM presence/absence causing comparison failures  
- **Indentation Changes**: Tool applying different indentation than expected
- **Content Precision**: Extra/missing whitespace or punctuation

## 📋 Future Enhancements

### Planned Test Expansions

- [ ] **UTF-16 Encoding Tests**: Files with Unicode BOM
- [ ] **Large File Testing**: Multi-megabyte files for performance validation
- [ ] **Binary File Detection**: Ensure tools don't corrupt binary files
- [ ] **Edge Cases**: Empty files, single-line files, files without newlines
- [ ] **More File Types**: YAML, XML, Markdown, configuration files

### Testing Infrastructure

- [ ] **Performance Benchmarks**: Measure tool execution time
- [ ] **Memory Usage Validation**: Ensure tools don't leak memory
- [ ] **Concurrent Testing**: Verify thread safety of editing operations
- [ ] **Error Condition Testing**: Invalid file paths, permission errors

## 🏆 Success Metrics

Our Golden Master testing has achieved:

- **Zero File Corruption**: No test has ever produced corrupted output
- **100% Pass Rate**: All current tests pass consistently  
- **Production Confidence**: Tools are safe to use on real codebases
- **Regression Prevention**: Changes that break existing behavior are caught immediately

## 📚 References

- [Golden Master Testing Pattern](https://blog.thecodewhisperer.com/permalink/surviving-legacy-code-with-golden-master-and-sampling)
- [DiffPlex Library Documentation](https://github.com/mmanela/diffplex)
- [COA MCP Framework Testing Guide](https://docs.coa-framework.dev/testing)

---

*This testing methodology ensures that our editing tools work correctly on real files in production environments, not just synthetic test scenarios.*