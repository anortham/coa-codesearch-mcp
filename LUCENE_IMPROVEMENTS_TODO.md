# 🔧 Lucene Index Improvements - Integration Checklist

**Status: Code written but NOT integrated**

## ❗ Current Issue
- Stuck `write.lock` file prevents index updates
- No automatic cleanup of stale locks
- No proper error recovery

## ✅ What We've Done
1. Created `ImprovedLuceneIndexService.cs` based on CoA Intranet patterns
2. Added stuck lock detection (15-minute timeout)
3. Added proper writer lifecycle management
4. Added cleanup methods

## ❌ What Still Needs Integration

### 1. Replace Existing Service
```csharp
// In Program.cs, replace:
services.AddSingleton<LuceneIndexService>();

// With:
services.AddSingleton<ImprovedLuceneIndexService>();
// Optional: Add background cleanup service
services.AddHostedService<LuceneMaintenanceService>();
```

### 2. Update FileIndexingService
- Change dependency from `LuceneIndexService` to `ImprovedLuceneIndexService`
- Update writer management to use new patterns
- Add proper commit/dispose calls

### 3. Update FastTextSearchTool
- Use new service for index access
- Handle lock errors gracefully
- Add retry logic for busy indexes

### 4. Add Configuration
```json
// Add to appsettings.json:
{
  "Lucene": {
    "IndexBasePath": ".codesearch",
    "LockTimeoutMinutes": 15,
    "EnableAutoCleanup": true,
    "CleanupIntervalMinutes": 30
  }
}
```

### 5. Create Background Cleanup Service (Optional)
```csharp
public class LuceneMaintenanceService : BackgroundService
{
    private readonly ImprovedLuceneIndexService _luceneService;
    private readonly ILogger<LuceneMaintenanceService> _logger;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _luceneService.CleanupStuckIndexes();
                _logger.LogInformation("Lucene maintenance completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Lucene maintenance");
            }
            
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }
}
```

### 6. Manual Cleanup Before Next Session
```bash
# Remove stuck lock file manually
rm .codesearch/index/write.lock

# Or delete entire index to start fresh
rm -rf .codesearch/index/
```

## 🚨 IMPORTANT FOR NEXT SESSION

**The improved service is NOT active yet!** To avoid losing context:

1. **Before starting**: Check for and remove any `write.lock` files
2. **First task**: Complete the integration steps above
3. **Test**: Run `index_workspace` to verify new lock handling works
4. **Document**: Use memory system to record the integration

## 📋 Quick Integration Guide

```csharp
// Step 1: Update DI registration
services.AddSingleton<ImprovedLuceneIndexService>();
services.AddSingleton<ILuceneIndexService>(provider => 
    provider.GetRequiredService<ImprovedLuceneIndexService>());

// Step 2: Update dependent services
public FileIndexingService(ImprovedLuceneIndexService luceneService, ...)

// Step 3: Add startup cleanup
public class Startup
{
    public void Configure(IApplicationBuilder app, ImprovedLuceneIndexService luceneService)
    {
        // Clean stuck locks on startup
        luceneService.CleanupStuckIndexes();
    }
}
```

## ✨ Benefits Once Integrated
- No more stuck locks
- Automatic recovery from crashes
- Better error messages
- Consistent index state
- No manual lock file deletion needed

**Priority: HIGH - Complete this FIRST in next session before using any indexing features!**