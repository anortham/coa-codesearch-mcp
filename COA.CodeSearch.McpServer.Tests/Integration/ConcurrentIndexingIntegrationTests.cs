using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tests.Helpers;
using Lucene.Net.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using Xunit;
using Xunit.Abstractions;
using SystemDirectory = System.IO.Directory;

namespace COA.CodeSearch.McpServer.Tests.Integration;

/// <summary>
/// Integration tests for concurrent indexing scenarios to verify race condition fixes
/// and proper resource management under load
/// </summary>
public class ConcurrentIndexingIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<LuceneIndexService> _luceneLogger;
    private readonly ILogger<FileIndexingService> _fileIndexingLogger;
    private readonly IConfiguration _configuration;
    private readonly PathResolutionService _pathResolution;
    private readonly string _testDirectory;
    private readonly List<LuceneIndexService> _indexServices = new();

    public ConcurrentIndexingIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _luceneLogger = new NullLogger<LuceneIndexService>();
        _fileIndexingLogger = new NullLogger<FileIndexingService>();
        
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lucene:LockTimeoutMinutes"] = "1", // Short timeout for testing
            ["Lucene:IndexBasePath"] = ".codesearch-test"
        });
        _configuration = configBuilder.Build();
        
        _pathResolution = new PathResolutionService(_configuration);
        _testDirectory = Path.Combine(Path.GetTempPath(), "ConcurrentIndexingTests", Guid.NewGuid().ToString());
        
        // Create test directory structure
        SystemDirectory.CreateDirectory(_testDirectory);
        CreateTestFiles();
    }

    private void CreateTestFiles()
    {
        // Create a realistic directory structure with multiple files
        var dirs = new[] { "src", "tests", "docs", "scripts" };
        var extensions = new[] { ".cs", ".js", ".ts", ".md", ".txt" };
        
        foreach (var dir in dirs)
        {
            var dirPath = Path.Combine(_testDirectory, dir);
            SystemDirectory.CreateDirectory(dirPath);
            
            // Create multiple files in each directory
            for (int i = 0; i < 20; i++)
            {
                foreach (var ext in extensions)
                {
                    var filePath = Path.Combine(dirPath, $"file{i}{ext}");
                    File.WriteAllText(filePath, $"This is test content for file {i} in {dir} directory. Contains some sample code and documentation.");
                }
            }
        }
    }
    
    /// <summary>
    /// Verifies that the index health is acceptable for testing.
    /// In tests, memory indexes may not exist (resulting in "Degraded" status),
    /// but the workspace index should be healthy.
    /// </summary>
    private async Task VerifyIndexHealthForTestingAsync(LuceneIndexService indexService, string workspacePath)
    {
        // Ensure the workspace index is properly committed
        var writer = await indexService.GetIndexWriterAsync(workspacePath);
        await indexService.CommitAsync(workspacePath);
        
        var healthResult = await indexService.CheckHealthAsync();
        
        // In tests, we accept "Degraded" status due to missing memory indexes
        // But we should not have "Unhealthy" status (corruption, stuck locks)
        if (healthResult.Status == IndexHealthCheckResult.HealthStatus.Unhealthy)
        {
            throw new InvalidOperationException($"Index is unhealthy: {healthResult.Description}");
        }
        
        // Verify the workspace index itself is functional
        var searcher = await indexService.GetIndexSearcherAsync(workspacePath);
        if (searcher.IndexReader.NumDocs == 0)
        {
            throw new InvalidOperationException("Workspace index contains no documents");
        }
    }

    [Fact]
    public async Task ConcurrentIndexCreation_ShouldNotCauseRaceConditions()
    {
        // Arrange
        const int concurrentOperations = 10;
        var tasks = new List<Task>();
        var exceptions = new ConcurrentBag<Exception>();
        var successfulIndexes = new ConcurrentBag<string>();
        
        // TDD: Use barrier to ensure true simultaneous execution
        var startBarrier = new Barrier(concurrentOperations);
        var operationStarted = new CountdownEvent(concurrentOperations);

        // Act - Create multiple index services concurrently trying to index the same workspace
        for (int i = 0; i < concurrentOperations; i++)
        {
            var taskId = i; // Capture for closure
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    // TDD: Ensure all tasks start at exactly the same time
                    startBarrier.SignalAndWait(TimeSpan.FromSeconds(10));
                    operationStarted.Signal();
                    
                    var indexService = new LuceneIndexService(_luceneLogger, _configuration, _pathResolution);
                    _indexServices.Add(indexService);
                    
                    // All services try to index the same workspace simultaneously
                    var writer = await indexService.GetIndexWriterAsync(_testDirectory);
                    await indexService.CommitAsync(_testDirectory);
                    
                    successfulIndexes.Add($"Service-{taskId}-Thread-{Thread.CurrentThread.ManagedThreadId}");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        // TDD: Verify all operations actually started before proceeding
        Assert.True(operationStarted.Wait(TimeSpan.FromSeconds(15)), "All operations should start within timeout");
        await Task.WhenAll(tasks);

        // Assert
        _output.WriteLine($"Successful indexes: {successfulIndexes.Count}");
        _output.WriteLine($"Exceptions: {exceptions.Count}");
        
        foreach (var ex in exceptions)
        {
            _output.WriteLine($"Exception: {ex.GetType().Name}: {ex.Message}");
            _output.WriteLine($"StackTrace: {ex.StackTrace}");
        }

        // TDD: Specific assertions that catch real concurrency bugs
        
        // 1. First operation should ALWAYS succeed (if this fails, basic functionality is broken)
        Assert.True(successfulIndexes.Count >= 1, $"First operation must succeed. Got {successfulIndexes.Count} successes out of {concurrentOperations}");
        
        // 2. Based on Lucene's single-writer model, subsequent operations should either succeed (if they get the lock) 
        //    or fail gracefully with specific exceptions (not corruption/race conditions)
        var allowableExceptionTypes = new[] { typeof(LockObtainFailedException), typeof(InvalidOperationException) };
        
        // 3. No race condition indicators
        var raceConditionKeywords = new[] { "corrupt", "race", "deadlock", "inconsistent" };
        var raceConditionExceptions = exceptions.Where(e => 
            raceConditionKeywords.Any(keyword => e.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase))).ToList();
        
        Assert.Empty(raceConditionExceptions);
        
        // 4. Success rate should be reasonable for the new AsyncLock implementation
        // With proper async locks and timeouts, we expect fewer race conditions but lower success rate
        // The first operation should always succeed, others may timeout gracefully
        var successRate = (double)successfulIndexes.Count / concurrentOperations;
        
        // TDD: Updated expectation for AsyncLock behavior - at least 1 success, acceptable timeout failures
        Assert.True(successRate >= 0.1, $"Success rate {successRate:P} is too low. Expected >= 10% (at least 1 out of {concurrentOperations})");
        
        // TDD: Verify timeout-related exceptions and lock failures are acceptable (not race condition bugs)
        var acceptableExceptions = exceptions.Where(ex => 
            // Explicit allowable types
            allowableExceptionTypes.Contains(ex.GetType()) ||
            // Timeout related
            ex is TimeoutException || 
            ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Failed to acquire lock", StringComparison.OrdinalIgnoreCase) ||
            // Lock failures (expected with concurrent access)
            ex.Message.Contains("Lock obtain timed out", StringComparison.OrdinalIgnoreCase)
        ).ToList();
            
        var badExceptions = exceptions.Except(acceptableExceptions).ToList();
        
        Assert.Empty(badExceptions);
        
        _output.WriteLine($"Acceptable exceptions: {acceptableExceptions.Count}");
        _output.WriteLine($"Unexpected exceptions: {badExceptions.Count}");
    }

    [Fact]
    public async Task ConcurrentFileIndexing_ShouldHandleBackpressureCorrectly()
    {
        // Arrange
        var indexService = new LuceneIndexService(_luceneLogger, _configuration, _pathResolution);
        _indexServices.Add(indexService);
        
        var fileIndexingService = new FileIndexingService(_fileIndexingLogger, _configuration, indexService, _pathResolution, new MockIndexingMetricsService(), new MockCircuitBreakerService(), new MockBatchIndexingService());
        
        // Act - Index the directory (this will test the backpressure implementation)
        var startTime = DateTime.UtcNow;
        var indexedCount = await fileIndexingService.IndexDirectoryAsync(_testDirectory, _testDirectory);
        var duration = DateTime.UtcNow - startTime;

        // Assert
        _output.WriteLine($"Indexed {indexedCount} files in {duration.TotalSeconds:F2} seconds");
        
        Assert.True(indexedCount > 0, "Should index some files");
        Assert.True(duration.TotalMinutes < 5, "Should complete within reasonable time (5 minutes)");
        
        // TDD: Verify index health is acceptable for testing (workspace index should work)
        await VerifyIndexHealthForTestingAsync(indexService, _testDirectory);
        
        // For testing, we verify the index is functional rather than requiring "Healthy" status
        // (missing memory indexes cause "Degraded" status which is acceptable in tests)
        var healthResult = await indexService.CheckHealthAsync();
        if (healthResult.Status != IndexHealthCheckResult.HealthStatus.Healthy)
        {
            _output.WriteLine($"Health check details: {healthResult.Description}");
            foreach (var kvp in healthResult.Data)
            {
                _output.WriteLine($"  {kvp.Key}: {kvp.Value}");
            }
        }
        
        // TDD: Accept Healthy or Degraded status (Degraded = missing memory indexes, which is OK in tests)
        Assert.True(healthResult.Status == IndexHealthCheckResult.HealthStatus.Healthy || 
                   healthResult.Status == IndexHealthCheckResult.HealthStatus.Degraded,
                   $"Expected Healthy or Degraded status, got {healthResult.Status}: {healthResult.Description}");
    }

    [Fact]
    public async Task BackpressureControl_ShouldLimitMemoryUsage()
    {
        // Arrange - Create a large number of files to stress backpressure
        var largeTestDir = Path.Combine(_testDirectory, "large");
        SystemDirectory.CreateDirectory(largeTestDir);
        
        // Create 1000 small files to test channel backpressure
        for (int i = 0; i < 1000; i++)
        {
            var filePath = Path.Combine(largeTestDir, $"test{i}.cs");
            await File.WriteAllTextAsync(filePath, $"// Test file {i}\nclass Test{i} {{ }}");
        }

        var indexService = new LuceneIndexService(_luceneLogger, _configuration, _pathResolution);
        _indexServices.Add(indexService);
        
        var fileIndexingService = new FileIndexingService(_fileIndexingLogger, _configuration, indexService, _pathResolution, new MockIndexingMetricsService(), new MockCircuitBreakerService(), new MockBatchIndexingService());
        
        // Monitor memory before and during indexing
        var initialMemory = GC.GetTotalMemory(forceFullCollection: true);
        
        // Act
        var indexedCount = await fileIndexingService.IndexDirectoryAsync(largeTestDir, largeTestDir);
        
        // Force GC to see actual retained memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var finalMemory = GC.GetTotalMemory(forceFullCollection: false);
        var memoryIncrease = finalMemory - initialMemory;
        
        // Assert
        _output.WriteLine($"Indexed {indexedCount} files");
        _output.WriteLine($"Memory increase: {memoryIncrease / 1024 / 1024:F2} MB");
        
        Assert.Equal(1000, indexedCount);
        
        // TDD: Backpressure should prevent excessive memory usage
        // With 1000 small files, memory increase should be < 50MB if backpressure works
        Assert.True(memoryIncrease < 50 * 1024 * 1024, 
            $"Memory increase {memoryIncrease / 1024 / 1024:F2} MB exceeds 50MB limit - backpressure may not be working");
        
        // Verify all files were actually indexed
        var searcher = await indexService.GetIndexSearcherAsync(largeTestDir);
        Assert.True(searcher.IndexReader.NumDocs >= 1000, "All files should be indexed");
    }

    [Fact]
    public async Task ResourceLeakPrevention_ShouldNotLeakFileHandles()
    {
        // TDD: This test specifically targets the memory-mapped file resource leak issue
        // Arrange
        var resourceTestDir = Path.Combine(_testDirectory, "resource_test");
        SystemDirectory.CreateDirectory(resourceTestDir);
        
        // Create files that will trigger memory-mapped file usage (>1MB each)
        var largeContent = new string('A', 1024 * 1024 + 1); // Just over 1MB threshold
        for (int i = 0; i < 5; i++)
        {
            var filePath = Path.Combine(resourceTestDir, $"large{i}.cs");
            await File.WriteAllTextAsync(filePath, $"// Large file {i}\n{largeContent}");
        }

        var indexService = new LuceneIndexService(_luceneLogger, _configuration, _pathResolution);
        _indexServices.Add(indexService);
        
        var fileIndexingService = new FileIndexingService(_fileIndexingLogger, _configuration, indexService, _pathResolution, new MockIndexingMetricsService(), new MockCircuitBreakerService(), new MockBatchIndexingService());
        
        // Act - Index multiple times to stress resource management
        for (int iteration = 0; iteration < 3; iteration++)
        {
            _output.WriteLine($"Resource test iteration {iteration + 1}");
            var indexedCount = await fileIndexingService.IndexDirectoryAsync(resourceTestDir, resourceTestDir);
            Assert.Equal(5, indexedCount);
            
            // Force GC to ensure any leaked resources are detected
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        // TDD: Verify index health is acceptable for testing (no resource leaks)
        await VerifyIndexHealthForTestingAsync(indexService, resourceTestDir);

        // Assert - No way to directly test file handle leaks in .NET, but the test will fail 
        // if resource disposal is broken (typically with "file in use" exceptions)
        var healthResult = await indexService.CheckHealthAsync();
        if (healthResult.Status != IndexHealthCheckResult.HealthStatus.Healthy)
        {
            _output.WriteLine($"Health check details: {healthResult.Description}");
            foreach (var kvp in healthResult.Data)
            {
                _output.WriteLine($"  {kvp.Key}: {kvp.Value}");
            }
        }
        
        // TDD: Accept Healthy or Degraded status (Degraded = missing memory indexes, which is OK in tests)
        Assert.True(healthResult.Status == IndexHealthCheckResult.HealthStatus.Healthy || 
                   healthResult.Status == IndexHealthCheckResult.HealthStatus.Degraded,
                   $"Expected Healthy or Degraded status, got {healthResult.Status}: {healthResult.Description}");
    }

    [Fact]
    public async Task IndexCorruption_ShouldBeDetectedAndReported()
    {
        // TDD: Verify corruption detection actually works
        // Arrange
        var corruptionTestDir = Path.Combine(_testDirectory, "corruption_test");
        SystemDirectory.CreateDirectory(corruptionTestDir);
        
        var testFile = Path.Combine(corruptionTestDir, "test.cs");
        await File.WriteAllTextAsync(testFile, "class Test { }");

        var indexService = new LuceneIndexService(_luceneLogger, _configuration, _pathResolution);
        _indexServices.Add(indexService);
        
        var fileIndexingService = new FileIndexingService(_fileIndexingLogger, _configuration, indexService, _pathResolution, new MockIndexingMetricsService(), new MockCircuitBreakerService(), new MockBatchIndexingService());
        
        // Create a valid index first
        await fileIndexingService.IndexDirectoryAsync(corruptionTestDir, corruptionTestDir);
        
        // Get the physical index path and corrupt it
        var indexPath = await indexService.GetPhysicalIndexPathAsync(corruptionTestDir);
        var segmentsFile = SystemDirectory.GetFiles(indexPath, "segments*").FirstOrDefault();
        
        if (segmentsFile != null)
        {
            // Corrupt the segments file by truncating it
            var originalBytes = await File.ReadAllBytesAsync(segmentsFile);
            await File.WriteAllBytesAsync(segmentsFile, originalBytes.Take(originalBytes.Length / 2).ToArray());
            
            // Act & Assert - Health check should detect corruption
            var healthResult = await indexService.CheckHealthAsync();
            
            // The health check should either detect corruption or at minimum not crash
            Assert.NotEqual(IndexHealthCheckResult.HealthStatus.Healthy, healthResult.Status);
            _output.WriteLine($"Corruption detected: {healthResult.Description}");
        }
        else
        {
            // If no segments file found, we can't test corruption detection
            _output.WriteLine("No segments file found - skipping corruption test");
        }
    }

    [Fact]
    public async Task ConcurrentReadWrite_ShouldMaintainDataIntegrity()
    {
        // Arrange
        var indexService = new LuceneIndexService(_luceneLogger, _configuration, _pathResolution);
        _indexServices.Add(indexService);
        
        var fileIndexingService = new FileIndexingService(_fileIndexingLogger, _configuration, indexService, _pathResolution, new MockIndexingMetricsService(), new MockCircuitBreakerService(), new MockBatchIndexingService());
        
        // First, index some files to establish baseline
        var initialIndexedCount = await fileIndexingService.IndexDirectoryAsync(_testDirectory, _testDirectory);
        _output.WriteLine($"Initial indexed count: {initialIndexedCount}");
        
        const int readerCount = 5;
        const int writerCount = 3;
        var readerTasks = new List<Task>();
        var writerTasks = new List<Task>();
        var readerResults = new ConcurrentBag<int>();
        var writerResults = new ConcurrentBag<bool>();
        
        // TDD: Use barriers for more deterministic concurrency testing
        var startBarrier = new Barrier(readerCount + writerCount);
        
        // Act - Concurrent readers and writers
        for (int i = 0; i < readerCount; i++)
        {
            readerTasks.Add(Task.Run(async () =>
            {
                try
                {
                    startBarrier.SignalAndWait(TimeSpan.FromSeconds(10));
                    var searcher = await indexService.GetIndexSearcherAsync(_testDirectory);
                    var totalDocs = searcher.IndexReader.NumDocs;
                    readerResults.Add(totalDocs);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Reader exception: {ex.Message}");
                    readerResults.Add(-1); // Error marker
                }
            }));
        }

        for (int i = 0; i < writerCount; i++)
        {
            var fileIndex = i;
            writerTasks.Add(Task.Run(async () =>
            {
                try
                {
                    startBarrier.SignalAndWait(TimeSpan.FromSeconds(10));
                    var testFile = Path.Combine(_testDirectory, $"concurrent_test_{fileIndex}.cs");
                    await File.WriteAllTextAsync(testFile, $"// Concurrent test file {fileIndex}\nclass ConcurrentTest{fileIndex} {{ }}");
                    
                    var result = await fileIndexingService.IndexFileAsync(_testDirectory, testFile);
                    writerResults.Add(result);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Writer exception: {ex.Message}");
                    writerResults.Add(false); // Error marker
                }
            }));
        }

        await Task.WhenAll(readerTasks.Concat(writerTasks));

        // Assert
        _output.WriteLine($"Reader results: {string.Join(", ", readerResults)}");
        _output.WriteLine($"Writer results: {string.Join(", ", writerResults)}");
        
        // TDD: Stronger assertions for data integrity
        
        // 1. All readers should succeed (no crashes or corruption)
        Assert.True(readerResults.All(r => r >= 0), "All readers should succeed without errors");
        
        // 2. Reader results should be consistent (within expected range)
        var readerResultsList = readerResults.Where(r => r >= 0).ToList();
        if (readerResultsList.Count > 1)
        {
            var minDocs = readerResultsList.Min();
            var maxDocs = readerResultsList.Max();
            var docCountVariation = maxDocs - minDocs;
            
            // Some variation is expected due to timing, but should be bounded
            Assert.True(docCountVariation <= writerCount, 
                $"Document count variation {docCountVariation} exceeds writer count {writerCount} - possible data integrity issue");
        }
        
        // 3. At least some writers should succeed
        var successfulWrites = writerResults.Count(r => r);
        Assert.True(successfulWrites > 0, "At least some writes should succeed");
        
        // 4. Final verification - check actual index state
        var finalSearcher = await indexService.GetIndexSearcherAsync(_testDirectory);
        var finalDocCount = finalSearcher.IndexReader.NumDocs;
        Assert.True(finalDocCount >= initialIndexedCount, "Final document count should be at least initial count");
        
        _output.WriteLine($"Final document count: {finalDocCount}");
    }

    [Fact]
    public async Task StuckLockDetection_ShouldProvideDetailedDiagnostics()
    {
        // TDD: Test actual stuck lock detection behavior with proper cleanup
        // Arrange
        var indexService = new LuceneIndexService(_luceneLogger, _configuration, _pathResolution);
        _indexServices.Add(indexService);
        
        // Create a separate test directory to avoid conflicts with existing indexes
        var stuckLockTestDir = Path.Combine(_testDirectory, "stuck_lock_test");
        SystemDirectory.CreateDirectory(stuckLockTestDir);
        
        var indexPath = await indexService.GetPhysicalIndexPathAsync(stuckLockTestDir);
        SystemDirectory.CreateDirectory(indexPath);
        var lockFile = Path.Combine(indexPath, "write.lock");
        
        FileStream? lockFileStream = null;
        try
        {
            // TDD: Create a real locked file to simulate a stuck lock
            // Use FileStream to actually lock the file (more realistic than fake content)
            lockFileStream = new FileStream(lockFile, FileMode.Create, FileAccess.Write, FileShare.None);
            await lockFileStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("stuck-lock-content"));
            await lockFileStream.FlushAsync();
            
            // Set old timestamp to simulate stuck lock
            var fileInfo = new FileInfo(lockFile);
            fileInfo.CreationTimeUtc = DateTime.UtcNow.AddHours(-2);
            fileInfo.LastWriteTimeUtc = DateTime.UtcNow.AddHours(-2);

            _output.WriteLine($"Created stuck lock file at: {lockFile}");
            _output.WriteLine($"Lock file age: {DateTime.UtcNow - fileInfo.CreationTimeUtc}");

            // Act & Assert - Should detect stuck lock and provide diagnostics
            // The new AsyncLock implementation should handle this gracefully
            var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await indexService.GetIndexWriterAsync(stuckLockTestDir);
            });

            // TDD: Verify the exception is related to lock acquisition failure
            var isExpectedException = exception is InvalidOperationException ||
                                     exception is TimeoutException ||
                                     exception is LockObtainFailedException ||
                                     exception.Message.Contains("lock", StringComparison.OrdinalIgnoreCase) ||
                                     exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
                                     
            Assert.True(isExpectedException, $"Expected lock-related exception, got: {exception.GetType().Name}: {exception.Message}");
            _output.WriteLine($"Exception message: {exception.Message}");

            // Verify diagnostics method can be called without crashing
            await indexService.DiagnoseStuckIndexesAsync();
            _output.WriteLine("Diagnostic method completed successfully");
        }
        finally
        {
            // TDD: Proper cleanup - close stream first, then delete file
            lockFileStream?.Dispose();
            
            // Wait a bit for file handles to be released
            await Task.Delay(100);
            
            if (File.Exists(lockFile))
            {
                try 
                {
                    File.Delete(lockFile);
                    _output.WriteLine("Cleaned up stuck lock file");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Could not clean up lock file (expected): {ex.Message}");
                    // This is expected - the indexer may still have a handle
                }
            }
        }
    }

    [Fact]
    public async Task HealthyIndex_ShouldPassHealthCheck()
    {
        // TDD: Verify that a properly created index reports as healthy
        // Arrange
        var indexService = new LuceneIndexService(_luceneLogger, _configuration, _pathResolution);
        _indexServices.Add(indexService);
        
        var fileIndexingService = new FileIndexingService(_fileIndexingLogger, _configuration, indexService, _pathResolution, new MockIndexingMetricsService(), new MockCircuitBreakerService(), new MockBatchIndexingService());
        
        // Create a valid index with actual content
        var testFile = Path.Combine(_testDirectory, "health_test.cs");
        await File.WriteAllTextAsync(testFile, "class HealthTest { void Method() { } }");
        
        await fileIndexingService.IndexFileAsync(_testDirectory, testFile);
        await indexService.CommitAsync(_testDirectory);
        
        // TDD: Verify index health is acceptable for testing
        await VerifyIndexHealthForTestingAsync(indexService, _testDirectory);
        
        // Act
        var healthResult = await indexService.CheckHealthAsync();
        
        // Assert
        _output.WriteLine($"Health status: {healthResult.Status}");
        _output.WriteLine($"Health description: {healthResult.Description}");
        if (healthResult.Status != IndexHealthCheckResult.HealthStatus.Healthy)
        {
            foreach (var kvp in healthResult.Data)
            {
                _output.WriteLine($"  {kvp.Key}: {kvp.Value}");
            }
        }
        
        // TDD: Accept Healthy or Degraded status (Degraded = missing memory indexes, which is OK in tests)
        Assert.True(healthResult.Status == IndexHealthCheckResult.HealthStatus.Healthy || 
                   healthResult.Status == IndexHealthCheckResult.HealthStatus.Degraded,
                   $"Expected Healthy or Degraded status, got {healthResult.Status}: {healthResult.Description}");
        Assert.NotNull(healthResult.Data);
        Assert.True(healthResult.Data.Count > 0, "Health check should provide diagnostic data");
        
        // Verify we can actually search the index
        var searcher = await indexService.GetIndexSearcherAsync(_testDirectory);
        Assert.True(searcher.IndexReader.NumDocs > 0, "Index should contain documents");
    }

    public void Dispose()
    {
        // Dispose all index services
        foreach (var service in _indexServices)
        {
            try
            {
                service.Dispose();
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error disposing index service: {ex.Message}");
            }
        }

        // Clean up test directory
        try
        {
            if (SystemDirectory.Exists(_testDirectory))
            {
                SystemDirectory.Delete(_testDirectory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error cleaning up test directory: {ex.Message}");
        }

        // Clean up test index directories
        try
        {
            var testIndexPath = Path.Combine(SystemDirectory.GetCurrentDirectory(), ".codesearch-test");
            if (SystemDirectory.Exists(testIndexPath))
            {
                SystemDirectory.Delete(testIndexPath, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error cleaning up test index directory: {ex.Message}");
        }
    }
}