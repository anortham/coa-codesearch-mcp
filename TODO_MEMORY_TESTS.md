# TODO: Fix Failing Memory Tests

## Archive Date Range Query Issue (2 tests)

**Problem**: The NumericRangeQuery for date filtering is not working properly in archive tests. Memories are stored correctly with proper Created dates, but when searching for memories older than X days, the query returns 0 results.

**Affected Tests**:
- `FlexibleMemoryServiceUnitTests.ArchiveMemoriesAsync_OldMemories_ReturnsCount`
- `FlexibleMemorySearchTests.ArchiveMemories_MarksOldMemoriesAsArchived`

**Investigation Notes**:
- Memories are being stored with correct Created/Modified dates
- NumericDocValuesField is added for range query support
- The date range query `NumericRangeQuery.NewInt64Range("created", fromTicks, toTicks, true, true)` returns 0 results
- This might be a Lucene.NET specific issue with how numeric fields need to be indexed for range queries
- Consider using Int64Point fields instead of Int64Field (requires Lucene.NET 4.8+ features)

**Potential Solutions**:
1. Use Int64Point for indexing numeric fields (requires checking Lucene.NET version compatibility)
2. Ensure the index writer is properly committing and the reader is refreshed
3. Check if there's a mismatch between field storage and query field names
4. Investigate if NumericDocValuesField alone is sufficient or if additional indexing is needed

## MemoryMigration File Locking Issue (3 tests)

**Problem**: IOException occurs during test cleanup when trying to delete temporary directories. The error message is "The process cannot access the file because it is being used by another process."

**Affected Tests**:
- `MemoryMigrationTests.ConvertToFlexibleMemory_ArchitecturalDecision_MapsCorrectly`
- `MemoryMigrationTests.ConvertToFlexibleMemory_WorkSession_MapsAsLocal`
- `MemoryMigrationTests.ConvertToFlexibleMemory_SetsCorrectSharingFlag` (Theory with 3 test cases)

**Investigation Notes**:
- The issue occurs in the Dispose() method when trying to clean up test directories
- Lucene index files might still be locked by index writers/readers
- Need to ensure all Lucene resources are properly disposed before directory cleanup

**Potential Solutions**:
1. Add more robust disposal logic with retries
2. Ensure all IndexWriter and IndexReader instances are properly closed
3. Add delays or force garbage collection before directory deletion
4. Use a different test isolation strategy that doesn't require directory deletion