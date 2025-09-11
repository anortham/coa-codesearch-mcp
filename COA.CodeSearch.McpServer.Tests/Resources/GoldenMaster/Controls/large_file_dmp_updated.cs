using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Collections.Concurrent;

namespace LargeFileTest
{
    /// <summary>
    /// A large class for testing performance of SearchAndReplaceTool operations.
    /// This file contains multiple classes and methods to create realistic file size scenarios.
    /// </summary>
    public sealed class LargeClass
    {
        private readonly ILogger<LargeClass> _logger;
        private readonly Dictionary<string, object> _cache = new();
        private readonly List<string> _items = new();
        private volatile bool _isInitialized = false;
        private readonly object _lock = new object();

        public LargeClass(ILogger<LargeClass> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_isInitialized) return;

            lock (_lock)
            {
                if (_isInitialized) return;

                _logger.LogInformation("Initializing LargeClass with extensive setup");
                
                // Populate with sample data
                for (int i = 0; i < 1000; i++)
                {
                    _items.Add($"Item_{i:D4}");
                    _cache[$"key_{i}"] = $"value_{i}";
                }

                _isInitialized = true;
            }

            await Task.Delay(10, cancellationToken);
            _logger.LogInformation("LargeClass initialization completed");
        }

        public async Task<List<string>> ProcessItemsAsync(IEnumerable<string> inputItems)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("Class not initialized");

            var results = new List<string>();
            var processedCount = 0;

            await foreach (var item in ConvertToAsyncEnumerable(inputItems))
            {
                var processed = await ProcessSingleItemAsync(item);
                results.Add(processed);
                processedCount++;

                if (processedCount % 100 == 0)
                {
                    _logger.LogDebug("Processed {Count} items", processedCount);
                }
            }

            _logger.LogInformation("Completed processing {Total} items", results.Count);
            return results;
        }

        private async IAsyncEnumerable<string> ConvertToAsyncEnumerable(IEnumerable<string> items)
        {
            foreach (var item in items)
            {
                await Task.Yield();
                yield return item;
            }
        }

        private async Task<string> ProcessSingleItemAsync(string item)
        {
            await Task.Delay(1); // Simulate async processing
            
            var processed = item switch
            {
                null => throw new ArgumentNullException(nameof(item)),
                "" => "EMPTY",
                var s when s.StartsWith("special_") => ProcessSpecialItem(s),
                var s when s.Length > 50 => s.Substring(0, 50) + "...",
                _ => item.ToUpperInvariant()
            };

            return $"PROCESSED_{processed}";
        }

        private string ProcessSpecialItem(string specialItem)
        {
            var parts = specialItem.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return specialItem;

            var result = new StringBuilder();
            for (int i = 1; i < parts.Length; i++)
            {
                result.Append(parts[i]);
                if (i < parts.Length - 1)
                    result.Append("-");
            }

            return result.ToString().ToLowerInvariant();
        }

        public void ClearCache()
        {
            lock (_lock)
            {
                _cache.Clear();
                _logger.LogInformation("Cache cleared");
            }
        }

        public int GetCacheSize()
        {
            lock (_lock)
            {
                return _cache.Count;
            }
        }

        public Dictionary<string, object> GetCacheSnapshot()
        {
            lock (_lock)
            {
                return new Dictionary<string, object>(_cache);
            }
        }
    }

    public class DataProcessor
    {
        private readonly ConcurrentDictionary<string, ProcessingResult> _results = new();
        private readonly SemaphoreSlim _semaphore = new(Environment.ProcessorCount);

        public async Task<ProcessingResult[]> ProcessBatchAsync(string[] items)
        {
            var tasks = items.Select(ProcessItemWithThrottling).ToArray();
            return await Task.WhenAll(tasks);
        }

        private async Task<ProcessingResult> ProcessItemWithThrottling(string item)
        {
            await _semaphore.WaitAsync();
            try
            {
                return await ProcessItemInternal(item);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task<ProcessingResult> ProcessItemInternal(string item)
        {
            var cacheKey = GenerateCacheKey(item);
            
            if (_results.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            // Simulate processing time
            await Task.Delay(Random.Shared.Next(10, 50));

            var result = new ProcessingResult
            {
                OriginalItem = item,
                ProcessedItem = item.Reverse().ToArray() |> new string(),
                ProcessingTime = TimeSpan.FromMilliseconds(Random.Shared.Next(10, 50)),
                Timestamp = DateTime.UtcNow,
                Success = true
            };

            _results.TryAdd(cacheKey, result);
            return result;
        }

        private static string GenerateCacheKey(string item)
        {
            return Convert.ToHexString(Encoding.UTF8.GetBytes(item)).ToLowerInvariant();
        }

        public void ClearResults()
        {
            _results.Clear();
        }

        public int GetResultCount() => _results.Count;
    }

    public record ProcessingResult
    {
        public string OriginalItem { get; init; } = string.Empty;
        public string ProcessedItem { get; init; } = string.Empty;
        public TimeSpan ProcessingTime { get; init; }
        public DateTime Timestamp { get; init; }
        public bool Success { get; init; }
        public string? Error { get; init; }
    }

    public static class Extensions
    {
        public static IEnumerable<T> Reverse<T>(this IEnumerable<T> source)
        {
            var items = source.ToArray();
            for (int i = items.Length - 1; i >= 0; i--)
            {
                yield return items[i];
            }
        }
    }

    public class PerformanceMetrics
    {
        private readonly Dictionary<string, List<double>> _metrics = new();
        private readonly object _lock = new object();

        public void RecordMetric(string name, double value)
        {
            lock (_lock)
            {
                if (!_metrics.TryGetValue(name, out var values))
                {
                    values = new List<double>();
                    _metrics[name] = values;
                }
                values.Add(value);
            }
        }

        public double GetAverage(string name)
        {
            lock (_lock)
            {
                return _metrics.TryGetValue(name, out var values) && values.Count > 0
                    ? values.Average()
                    : 0.0;
            }
        }

        public double GetMax(string name)
        {
            lock (_lock)
            {
                return _metrics.TryGetValue(name, out var values) && values.Count > 0
                    ? values.Max()
                    : 0.0;
            }
        }

        public double GetMin(string name)
        {
            lock (_lock)
            {
                return _metrics.TryGetValue(name, out var values) && values.Count > 0
                    ? values.Min()
                    : 0.0;
            }
        }

        public int GetCount(string name)
        {
            lock (_lock)
            {
                return _metrics.TryGetValue(name, out var values) ? values.Count : 0;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _metrics.Clear();
            }
        }

        public Dictionary<string, MetricSummary> GetAllSummaries()
        {
            lock (_lock)
            {
                return _metrics.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new MetricSummary
                    {
                        Count = kvp.Value.Count,
                        Average = kvp.Value.Average(),
                        Min = kvp.Value.Min(),
                        Max = kvp.Value.Max(),
                        Sum = kvp.Value.Sum()
                    });
            }
        }
    }

    public record MetricSummary
    {
        public int Count { get; init; }
        public double Average { get; init; }
        public double Min { get; init; }
        public double Max { get; init; }
        public double Sum { get; init; }
    }
}