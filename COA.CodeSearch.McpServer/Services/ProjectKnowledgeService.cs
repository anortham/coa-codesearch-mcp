using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text;
using COA.CodeSearch.McpServer.Models;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// HTTP client service for ProjectKnowledge MCP integration
/// </summary>
public class ProjectKnowledgeService : IProjectKnowledgeService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProjectKnowledgeService> _logger;
    private readonly string _baseUrl;
    private readonly bool _enabled;

    public ProjectKnowledgeService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ProjectKnowledgeService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = configuration.GetValue<string>("ProjectKnowledge:BaseUrl") ?? "http://localhost:5100";
        _enabled = configuration.GetValue<bool?>("ProjectKnowledge:Enabled") ?? true;

        // Configure HttpClient
        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "CodeSearch-MCP/2.0");
    }

    public async Task<string?> StoreKnowledgeAsync(
        string content,
        string type = "TechnicalDebt",
        Dictionary<string, object>? metadata = null,
        string[]? tags = null,
        string? priority = null)
    {
        if (!_enabled)
        {
            _logger.LogDebug("ProjectKnowledge integration disabled");
            return null;
        }

        try
        {
            var payload = new
            {
                content,
                type,
                metadata = metadata ?? new Dictionary<string, object>(),
                tags = tags ?? Array.Empty<string>(),
                priority = priority ?? "normal",
                source = "codesearch"
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
            });

            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogDebug("Storing knowledge in ProjectKnowledge: {Type} - {ContentPreview}", 
                type, content.Length > 50 ? content[..50] + "..." : content);

            var response = await _httpClient.PostAsync("/api/knowledge/store", httpContent);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(responseJson);
                
                var knowledgeId = result.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                _logger.LogInformation("Knowledge stored successfully with ID: {KnowledgeId}", knowledgeId);
                return knowledgeId;
            }
            else
            {
                _logger.LogWarning("Failed to store knowledge: {StatusCode} - {ReasonPhrase}", 
                    response.StatusCode, response.ReasonPhrase);
                return null;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "ProjectKnowledge API not reachable at {BaseUrl}", _baseUrl);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "ProjectKnowledge API request timed out");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error storing knowledge in ProjectKnowledge");
            return null;
        }
    }

    public async Task<bool> IsAvailableAsync()
    {
        if (!_enabled) return false;

        try
        {
            var response = await _httpClient.GetAsync("/api/knowledge/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IEnumerable<KnowledgeReference>?> SearchKnowledgeAsync(string query)
    {
        if (!_enabled) return null;

        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var response = await _httpClient.GetAsync($"/api/knowledge/search?query={encodedQuery}");

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                
                if (result.TryGetProperty("items", out var itemsProperty) && itemsProperty.ValueKind == JsonValueKind.Array)
                {
                    var knowledge = new List<KnowledgeReference>();
                    
                    foreach (var item in itemsProperty.EnumerateArray())
                    {
                        var knowledgeRef = new KnowledgeReference
                        {
                            Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                            Content = item.TryGetProperty("content", out var content) ? content.GetString() ?? "" : "",
                            Type = item.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "",
                            CreatedAt = item.TryGetProperty("createdAt", out var created) && DateTime.TryParse(created.GetString(), out var createdDate) ? createdDate : DateTime.MinValue
                        };
                        
                        if (item.TryGetProperty("tags", out var tagsProperty) && tagsProperty.ValueKind == JsonValueKind.Array)
                        {
                            knowledgeRef.Tags = tagsProperty.EnumerateArray()
                                .Where(t => t.ValueKind == JsonValueKind.String)
                                .Select(t => t.GetString()!)
                                .ToArray();
                        }
                        
                        knowledge.Add(knowledgeRef);
                    }
                    
                    return knowledge;
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search knowledge in ProjectKnowledge");
            return null;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}