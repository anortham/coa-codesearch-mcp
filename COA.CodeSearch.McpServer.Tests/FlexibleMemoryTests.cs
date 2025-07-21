using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests;

public class FlexibleMemoryTests
{
    [Fact]
    public void FlexibleMemoryEntry_SetAndGetFields_WorksCorrectly()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry
        {
            Type = MemoryTypes.TechnicalDebt,
            Content = "Refactor authentication module"
        };
        
        // Act
        memory.SetField(MemoryFields.Status, MemoryStatus.Pending);
        memory.SetField(MemoryFields.Priority, MemoryPriority.High);
        memory.SetField(MemoryFields.Tags, new[] { "auth", "refactor", "security" });
        memory.SetField(MemoryFields.DueDate, DateTime.UtcNow.AddDays(7));
        memory.SetField(MemoryFields.Complexity, 8); // 1-10 scale
        memory.SetField(MemoryFields.RelatedTo, new[] { "mem-123", "mem-456" });
        
        // Assert
        Assert.Equal(MemoryStatus.Pending, memory.GetField<string>(MemoryFields.Status));
        Assert.Equal(MemoryPriority.High, memory.GetField<string>(MemoryFields.Priority));
        
        var tags = memory.GetField<string[]>(MemoryFields.Tags);
        Assert.NotNull(tags);
        Assert.Equal(3, tags.Length);
        Assert.Contains("auth", tags);
        
        var dueDate = memory.GetField<DateTime>(MemoryFields.DueDate);
        Assert.True(dueDate > DateTime.UtcNow);
        
        Assert.Equal(8, memory.GetField<int>(MemoryFields.Complexity));
        
        var relatedTo = memory.GetField<string[]>(MemoryFields.RelatedTo);
        Assert.NotNull(relatedTo);
        Assert.Equal(2, relatedTo.Length);
    }
    
    [Fact]
    public void FlexibleMemoryEntry_ConvenienceProperties_Work()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry();
        
        // Act
        memory.SetField(MemoryFields.Status, MemoryStatus.InProgress);
        memory.SetField(MemoryFields.Priority, MemoryPriority.Critical);
        memory.SetField(MemoryFields.Tags, new[] { "bug", "critical" });
        memory.SetField(MemoryFields.RelatedTo, new[] { "parent-123" });
        
        // Assert - Test convenience properties
        Assert.Equal(MemoryStatus.InProgress, memory.Status);
        Assert.Equal(MemoryPriority.Critical, memory.Priority);
        Assert.NotNull(memory.Tags);
        Assert.Equal(2, memory.Tags.Length);
        Assert.NotNull(memory.RelatedTo);
        Assert.Single(memory.RelatedTo);
    }
    
    [Fact]
    public void FlexibleMemoryEntry_HandlesComplexFieldTypes()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry();
        
        // Complex object as field
        var complexData = new
        {
            Component = "UserService",
            Methods = new[] { "Login", "Register", "ResetPassword" },
            LineCount = 1500,
            Metrics = new
            {
                Complexity = 45,
                Coverage = 0.85
            }
        };
        
        // Act
        memory.SetField("analysisData", complexData);
        
        // Assert
        var retrieved = memory.GetField<dynamic>("analysisData");
        Assert.NotNull(retrieved);
        
        // Since it's deserialized from JSON, we need to access it appropriately
        var json = JsonSerializer.Serialize(retrieved);
        Assert.Contains("UserService", json);
        Assert.Contains("Login", json);
        Assert.Contains("1500", json);
    }
    
    [Fact]
    public void FlexibleMemoryEntry_TimestampTicks_CalculatedCorrectly()
    {
        // Arrange
        var created = DateTime.UtcNow;
        var memory = new FlexibleMemoryEntry
        {
            Created = created
        };
        
        // Assert
        Assert.Equal(created.Ticks, memory.TimestampTicks);
    }
    
    [Fact]
    public void WorkingMemory_CreateSessionScoped_SetsCorrectly()
    {
        // Act
        var workingMemory = WorkingMemory.CreateSessionScoped("Remember to check auth token expiry");
        
        // Assert
        Assert.Equal("WorkingMemory", workingMemory.Type);
        Assert.Equal("Remember to check auth token expiry", workingMemory.Content);
        Assert.False(workingMemory.IsShared);
        Assert.True(workingMemory.ExpiresAt > DateTime.UtcNow);
        Assert.True(workingMemory.ExpiresAt <= DateTime.UtcNow.AddHours(24));
        
        var expiresField = workingMemory.GetField<DateTime>(MemoryFields.ExpiresAt);
        Assert.Equal(workingMemory.ExpiresAt, expiresField);
    }
    
    [Fact]
    public void DateRangeFilter_ParseRelativeTime_WorksCorrectly()
    {
        // Arrange & Act
        var testCases = new[]
        {
            ("today", 0, 0),
            ("yesterday", -1, -1),
            ("last-week", -7, 0),
            ("last-7-days", -7, 0),
            ("last-30-days", -30, 0),
            ("last-3-weeks", -21, 0),
            ("last-2-hours", 0, 0) // Same day
        };
        
        foreach (var (relativeTime, expectedDaysFromStart, expectedDaysFromEnd) in testCases)
        {
            var filter = new DateRangeFilter { RelativeTime = relativeTime };
            filter.ParseRelativeTime();
            
            Assert.NotNull(filter.From);
            Assert.NotNull(filter.To);
            
            // Check dates are in expected range (with some tolerance for test execution time)
            var daysDiffStart = (DateTime.UtcNow - filter.From.Value).TotalDays;
            var daysDiffEnd = (DateTime.UtcNow - filter.To.Value).TotalDays;
            
            Assert.True(Math.Abs(daysDiffStart - Math.Abs(expectedDaysFromStart)) < 1);
            Assert.True(Math.Abs(daysDiffEnd - Math.Abs(expectedDaysFromEnd)) < 1);
        }
    }
    
    [Fact]
    public void FlexibleMemorySearchRequest_DefaultValues_SetCorrectly()
    {
        // Arrange & Act
        var request = new FlexibleMemorySearchRequest();
        
        // Assert
        Assert.Equal(string.Empty, request.Query);
        Assert.Equal(1, request.RelationshipDepth);
        Assert.True(request.OrderDescending);
        Assert.Equal(50, request.MaxResults);
        Assert.False(request.IncludeArchived);
        Assert.True(request.BoostRecent);
        Assert.True(request.BoostFrequent);
    }
    
    [Fact]
    public void MemoryTypes_ContainsAllExpectedTypes()
    {
        // Assert - Check new types exist
        Assert.Equal("TechnicalDebt", MemoryTypes.TechnicalDebt);
        Assert.Equal("DeferredTask", MemoryTypes.DeferredTask);
        Assert.Equal("Question", MemoryTypes.Question);
        Assert.Equal("Assumption", MemoryTypes.Assumption);
        Assert.Equal("Experiment", MemoryTypes.Experiment);
        Assert.Equal("Learning", MemoryTypes.Learning);
        Assert.Equal("Blocker", MemoryTypes.Blocker);
        Assert.Equal("Idea", MemoryTypes.Idea);
        
        // Check backward compatibility types
        Assert.Equal("ArchitecturalDecision", MemoryTypes.ArchitecturalDecision);
        Assert.Equal("CodePattern", MemoryTypes.CodePattern);
        Assert.Equal("SecurityRule", MemoryTypes.SecurityRule);
    }
    
    [Fact]
    public void FlexibleMemoryEntry_NullFieldHandling()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry();
        
        // Act - Try to get non-existent fields
        var status = memory.GetField<string>("nonexistent");
        var number = memory.GetField<int>("nothere");
        var array = memory.GetField<string[]>("missing");
        
        // Assert - Should return defaults
        Assert.Null(status);
        Assert.Equal(0, number);
        Assert.Null(array);
    }
    
    [Fact]
    public void FlexibleMemoryEntry_ModifiedTimestamp_Updates()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry();
        var originalModified = memory.Modified;
        
        // Act
        Thread.Sleep(10); // Ensure time difference
        memory.Modified = DateTime.UtcNow;
        
        // Assert
        Assert.True(memory.Modified > originalModified);
    }
    
    [Theory]
    [InlineData("status", "in-progress")]
    [InlineData("priority", "high")]
    [InlineData("risk", "medium")]
    public void FlexibleMemoryEntry_StringFields_StoreCorrectly(string fieldName, string value)
    {
        // Arrange
        var memory = new FlexibleMemoryEntry();
        
        // Act
        memory.SetField(fieldName, value);
        
        // Assert
        Assert.Equal(value, memory.GetField<string>(fieldName));
    }
}