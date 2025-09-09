using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.McpServer.ResponseBuilders;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace COA.CodeSearch.McpServer.Tests.ResponseBuilders
{
    [TestFixture]
    public class FindReferencesResponseBuilderTests
    {
        private FindReferencesResponseBuilder _responseBuilder = null!;
        private Mock<ILogger<FindReferencesResponseBuilder>> _loggerMock = null!;
        private Mock<IResourceStorageService> _storageServiceMock = null!;

        [SetUp]
        public void SetUp()
        {
            _loggerMock = new Mock<ILogger<FindReferencesResponseBuilder>>();
            _storageServiceMock = new Mock<IResourceStorageService>();
            _responseBuilder = new FindReferencesResponseBuilder(_loggerMock.Object, _storageServiceMock.Object);
        }

        #region Real Behavior Tests - Token Budget Enforcement
        
        [Test]
        public async Task BuildResponseAsync_WithSmallBudget_TruncatesResults()
        {
            // Arrange - Real C# code snippets that generate realistic tokens
            var realHits = new List<SearchHit>();
            for (int i = 0; i < 50; i++)
            {
                realHits.Add(new SearchHit
                {
                    FilePath = $@"C:\src\Services\UserService{i}.cs",
                    Score = 0.9f - (i * 0.01f),
                    Snippet = $"public class UserService{i} {{ public async Task<User> GetUserAsync(int id) {{ return await _repository.GetByIdAsync(id); }} }}",
                    Fields = new Dictionary<string, string>
                    {
                        ["type_info"] = $"{{\"type\":\"class\",\"name\":\"UserService{i}\",\"methods\":[\"GetUserAsync\",\"CreateUserAsync\",\"UpdateUserAsync\"],\"properties\":[\"IsActive\",\"LastModified\"]}}",
                        ["language"] = "csharp",
                        ["referenceType"] = "method_call",
                        ["size"] = "2048"
                    },
                    LineNumber = 42 + i,
                    ContextLines = new List<string> 
                    { 
                        "    // Get user by ID with validation",
                        $"    var user = userService{i}.GetUserAsync(userId);",
                        "    if (user == null) throw new UserNotFoundException();"
                    }
                });
            }
            
            var searchResult = new SearchResult
            {
                TotalHits = 50,
                Hits = realHits,
                SearchTime = TimeSpan.FromMilliseconds(45),
                Query = "GetUserAsync"
            };
            
            var context = new ResponseContext 
            { 
                ResponseMode = "full",
                TokenLimit = 3000  // Small budget - should force truncation
            };

            // Act
            var response = await _responseBuilder.BuildResponseAsync(searchResult, context);

            // Assert - Test ACTUAL behavior
            response.Should().NotBeNull();
            response.Success.Should().BeTrue();
            
            // REAL test: Actual token counting and budget enforcement
            var actualTokens = EstimateActualResponseTokens(response);
            actualTokens.Should().BeLessOrEqualTo(3000, "Response should respect token budget");
            
            // REAL test: Truncation should actually happen
            response.Data.Results.Hits.Should().HaveCountLessThan(50, "Large result set should be truncated");
            
            // REAL test: High-scoring results should be preserved
            var firstHitScore = response.Data.Results.Hits.First().Score;
            firstHitScore.Should().BeGreaterThan(0.8f, "Highest scoring results should be preserved");
        }

        [Test]
        public async Task BuildResponseAsync_WithLargeBudget_IncludesAllResults()
        {
            // Arrange - Smaller dataset that should fit in large budget
            var realHits = new List<SearchHit>();
            for (int i = 0; i < 10; i++)
            {
                realHits.Add(new SearchHit
                {
                    FilePath = $@"C:\src\Controllers\UserController{i}.cs",
                    Score = 0.95f,
                    Snippet = $"[HttpGet] public async Task<ActionResult<User>> GetUser(int id) => await _userService.GetUserAsync(id);",
                    Fields = new Dictionary<string, string>
                    {
                        ["type_info"] = $"{{\"type\":\"method\",\"name\":\"GetUser\",\"returnType\":\"ActionResult<User>\"}}",
                        ["language"] = "csharp", 
                        ["referenceType"] = "method_call"
                    },
                    LineNumber = 25 + i,
                    ContextLines = new List<string> { "[Route(\"api/users/{id}\")]", "[HttpGet]" }
                });
            }
            
            var searchResult = new SearchResult
            {
                TotalHits = 10,
                Hits = realHits,
                SearchTime = TimeSpan.FromMilliseconds(15),
                Query = "GetUserAsync"
            };
            
            var context = new ResponseContext 
            { 
                ResponseMode = "full",
                TokenLimit = 15000  // Large budget - should include everything
            };

            // Act
            var response = await _responseBuilder.BuildResponseAsync(searchResult, context);

            // Assert - Test ACTUAL behavior
            response.Should().NotBeNull();
            response.Data.Results.Hits.Should().HaveCount(10, "All results should fit in large budget");
            
            // REAL test: Essential fields should be preserved
            var firstHit = response.Data.Results.Hits.First();
            firstHit.Fields.Should().ContainKey("type_info");
            firstHit.Fields.Should().ContainKey("language");
            firstHit.Fields.Should().ContainKey("referenceType");
            firstHit.ContextLines.Should().NotBeNull();
        }

        [Test] 
        public async Task BuildResponseAsync_WithMassiveTypeInfo_CapsTokenUsage()
        {
            // Arrange - Hit with genuinely large type information
            var massiveTypeInfo = GenerateMassiveTypeInfo();  // ~2000 characters of real type data
            
            var hit = new SearchHit
            {
                FilePath = @"C:\src\Models\ComplexDomainModel.cs",
                Score = 0.98f,
                Snippet = "public class ComplexDomainModel : BaseEntity, IValidatable, IAuditable, ITrackable",
                Fields = new Dictionary<string, string>
                {
                    ["type_info"] = massiveTypeInfo,
                    ["language"] = "csharp",
                    ["referenceType"] = "class_declaration"
                },
                LineNumber = 1
            };
            
            var searchResult = new SearchResult
            {
                TotalHits = 1,
                Hits = new List<SearchHit> { hit },
                SearchTime = TimeSpan.FromMilliseconds(5),
                Query = "ComplexDomainModel"
            };
            
            var context = new ResponseContext { ResponseMode = "full", TokenLimit = 8000 };

            // Act
            var response = await _responseBuilder.BuildResponseAsync(searchResult, context);

            // Assert - Test ACTUAL type info capping
            var responseHit = response.Data.Results.Hits.First();
            var returnedTypeInfo = responseHit.Fields["type_info"];
            
            // REAL test: Type info should be capped, not the original massive size
            var typeInfoTokens = EstimateTokens(returnedTypeInfo);
            typeInfoTokens.Should().BeLessOrEqualTo(60, "Large type_info should be capped at 60 tokens max");
            
            // REAL test: Type info should still be valid JSON even when capped
            returnedTypeInfo.Should().StartWith("{", "Capped type_info should still be valid JSON");
        }

        [Test]
        public async Task BuildResponseAsync_WithZeroBudget_ReturnsMinimalResponse()
        {
            // Arrange
            var hits = new List<SearchHit>
            {
                new SearchHit
                {
                    FilePath = @"C:\src\Test.cs",
                    Score = 0.9f,
                    Snippet = "public void TestMethod() { }",
                    Fields = new Dictionary<string, string> { ["language"] = "csharp" },
                    LineNumber = 10
                }
            };
            
            var searchResult = new SearchResult { TotalHits = 1, Hits = hits, Query = "TestMethod" };
            var context = new ResponseContext { ResponseMode = "summary", TokenLimit = 100 }; // Tiny budget

            // Act
            var response = await _responseBuilder.BuildResponseAsync(searchResult, context);

            // Assert - Test ACTUAL minimal response behavior
            response.Should().NotBeNull();
            response.Success.Should().BeTrue();
            response.Data.Summary.Should().Contain("TestMethod");
            
            // REAL test: Minimal budget should produce minimal response
            var totalTokens = EstimateActualResponseTokens(response);
            totalTokens.Should().BeLessOrEqualTo(150, "Tiny budget should produce minimal response");
        }

        [Test]
        public async Task BuildResponseAsync_AdaptiveBudgetAllocation_ActuallyWorks()
        {
            // Test REAL adaptive behavior with different result counts
            var testCases = new[]
            {
                new { ResultCount = 0, Description = "Zero results should provide guidance" },
                new { ResultCount = 1, Description = "Single result should be concise" }, 
                new { ResultCount = 150, Description = "Many results should provide analysis" }
            };

            foreach (var testCase in testCases)
            {
                // Arrange
                var hits = GenerateRealSearchHits(testCase.ResultCount);
                var searchResult = new SearchResult 
                { 
                    TotalHits = testCase.ResultCount, 
                    Hits = hits,
                    Query = "TestMethod"
                };
                
                var context = new ResponseContext { ResponseMode = "full", TokenLimit = 8000 };

                // Act  
                var response = await _responseBuilder.BuildResponseAsync(searchResult, context);

                // Assert - Test ACTUAL adaptive behavior based on result count
                if (testCase.ResultCount == 0)
                {
                    // Zero results should provide helpful guidance
                    response.Insights.Should().NotBeEmpty("Zero results should provide insights to help user");
                    response.Actions.Should().NotBeEmpty("Zero results should provide alternative actions");
                    response.Insights.First().Should().Contain("No references found");
                    response.Actions.First().Description.Should().Contain("symbol");
                }
                else if (testCase.ResultCount == 1)
                {
                    // Single result should be simple and focused
                    response.Data.Results.Hits.Should().HaveCount(1, "Single result should be included");
                    response.Insights.Should().NotBeEmpty("Should provide at least basic insights");
                    response.Actions.Should().NotBeEmpty("Should provide relevant actions");
                }
                else if (testCase.ResultCount >= 150)
                {
                    // Many results should provide analysis and guidance
                    response.Data.Results.Hits.Should().NotBeEmpty("Should include results even if truncated");
                    response.Data.Results.Hits.Count.Should().BeLessOrEqualTo(testCase.ResultCount, "May truncate for token budget");
                    
                    // Should provide analysis insights for large result sets
                    response.Insights.Should().NotBeEmpty("Large result sets should have analysis insights");
                    response.Actions.Should().NotBeEmpty("Large result sets should have guidance actions");
                    
                    // Should mention high reference count
                    var highCountInsight = response.Insights.FirstOrDefault(i => i.Contains("High reference count") || i.Contains("references found"));
                    highCountInsight.Should().NotBeNull("Should warn about high reference count for large result sets");
                }
            }
        }

        #endregion

        #region Real Data Helpers
        
        private List<SearchHit> GenerateRealSearchHits(int count)
        {
            var hits = new List<SearchHit>();
            var codeSnippets = new[]
            {
                "public class UserService { public async Task<User> GetUserAsync(int id) => await _repository.GetByIdAsync(id); }",
                "[HttpPost] public async Task<ActionResult<User>> CreateUser([FromBody] CreateUserRequest request) { return await _userService.CreateAsync(request); }",
                "private readonly IUserRepository _userRepository; public UserController(IUserRepository userRepository) { _userRepository = userRepository; }",
                "public interface IUserService { Task<User> GetUserAsync(int id); Task<User> CreateAsync(CreateUserRequest request); }",
                "public record CreateUserRequest(string Name, string Email, DateTime DateOfBirth);"
            };
            
            for (int i = 0; i < count; i++)
            {
                hits.Add(new SearchHit
                {
                    FilePath = $@"C:\src\{(i % 2 == 0 ? "Services" : "Controllers")}\File{i}.cs",
                    Score = 0.95f - (i * 0.005f),
                    Snippet = codeSnippets[i % codeSnippets.Length],
                    Fields = new Dictionary<string, string>
                    {
                        ["type_info"] = $"{{\"type\":\"{(i % 3 == 0 ? "class" : "method")}\",\"name\":\"Item{i}\"}}",
                        ["language"] = "csharp",
                        ["referenceType"] = i % 2 == 0 ? "method_call" : "declaration"
                    },
                    LineNumber = 10 + i,
                    ContextLines = new List<string> { $"// Context for item {i}", $"var result{i} = method();" }
                });
            }
            
            return hits;
        }
        
        private string GenerateMassiveTypeInfo()
        {
            // Generate realistic but large type information
            var properties = new List<string>();
            var methods = new List<string>();
            
            for (int i = 0; i < 50; i++)
            {
                properties.Add($"\"Property{i}\"");
                methods.Add($"\"Method{i}Async\"");
            }
            
            return $@"{{
                ""type"": ""class"",
                ""name"": ""ComplexDomainModel"",
                ""namespace"": ""MyApp.Domain.Models"",
                ""properties"": [{string.Join(",", properties)}],
                ""methods"": [{string.Join(",", methods)}],
                ""interfaces"": [""IValidatable"", ""IAuditable"", ""ITrackable""],
                ""baseClass"": ""BaseEntity"",
                ""attributes"": [""Table(\""complex_models\"")"", ""JsonSerializable""]
            }}";
        }
        
        private int EstimateActualResponseTokens(AIOptimizedResponse<SearchResult> response)
        {
            var tokens = 0;
            
            // Estimate data tokens
            if (response.Data?.Results?.Hits != null)
            {
                foreach (var hit in response.Data.Results.Hits)
                {
                    tokens += EstimateTokens(hit.FilePath);
                    tokens += EstimateTokens(hit.Snippet ?? "");
                    if (hit.Fields != null)
                    {
                        foreach (var field in hit.Fields)
                        {
                            tokens += EstimateTokens(field.Value);
                        }
                    }
                    if (hit.ContextLines != null)
                    {
                        tokens += hit.ContextLines.Sum(line => EstimateTokens(line));
                    }
                }
            }
            
            // Estimate insights tokens
            if (response.Insights != null)
            {
                tokens += response.Insights.Sum(insight => EstimateTokens(insight));
            }
            
            // Estimate actions tokens  
            if (response.Actions != null)
            {
                tokens += response.Actions.Sum(action => EstimateTokens(action.Description ?? ""));
            }
            
            return tokens;
        }
        
        private int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            
            // Simple but realistic token estimation (roughly 4 characters per token)
            return Math.Max(1, text.Length / 4);
        }
        
        #endregion
    }
}
