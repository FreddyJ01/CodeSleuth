using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text;
using CodeSleuth.Infrastructure.VectorDatabase;

namespace CodeSleuth.Infrastructure.VectorDatabase;

/// <summary>
/// REST-based implementation of Qdrant operations as fallback for gRPC issues.
/// </summary>
public class QdrantRestService : IQdrantService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<QdrantRestService> _logger;
    private const string CollectionName = "code_chunks";
    private const uint VectorSize = 1536;

    public QdrantRestService(HttpClient httpClient, ILogger<QdrantRestService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitializeCollectionAsync()
    {
        try
        {
            _logger.LogInformation("Checking if collection '{CollectionName}' exists via REST API", CollectionName);

            // Check if collection exists
            var response = await _httpClient.GetAsync($"/collections/{CollectionName}");
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Collection '{CollectionName}' already exists", CollectionName);
                return;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Collection '{CollectionName}' does not exist, creating...", CollectionName);
                await CreateCollectionAsync();
            }
            else
            {
                _logger.LogError("Unexpected response when checking collection: {StatusCode}", response.StatusCode);
                response.EnsureSuccessStatusCode();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize collection '{CollectionName}'", CollectionName);
            throw new InvalidOperationException($"Failed to initialize collection '{CollectionName}': {ex.Message}", ex);
        }
    }

    public async Task UpsertAsync(Guid id, float[] vector, Dictionary<string, object> metadata)
    {
        try
        {
            if (vector == null)
                throw new ArgumentNullException(nameof(vector));
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            _logger.LogDebug("Upserting vector with ID: {Id}", id);

            var point = new
            {
                id = id.ToString(),
                vector = vector,
                payload = metadata
            };

            var points = new { points = new[] { point } };
            var json = JsonSerializer.Serialize(points);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync($"/collections/{CollectionName}/points", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to upsert vector {Id}. Status: {StatusCode}, Response: {Response}", 
                    id, response.StatusCode, errorContent);
                throw new InvalidOperationException($"Failed to upsert vector with ID '{id}': {response.StatusCode} - {errorContent}");
            }

            _logger.LogDebug("Successfully upserted vector with ID: {Id}", id);
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            _logger.LogError(ex, "Unexpected error upserting vector with ID: {Id}", id);
            throw new InvalidOperationException($"Failed to upsert vector with ID '{id}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Bulk upserts multiple vectors in a single API call for much better performance.
    /// </summary>
    public async Task UpsertBulkAsync(IEnumerable<(Guid id, float[] vector, Dictionary<string, object> metadata)> items, CancellationToken cancellationToken = default)
    {
        var itemsList = items.ToList();
        if (!itemsList.Any())
            return;

        try
        {
            _logger.LogDebug("Bulk upserting {Count} vectors", itemsList.Count);

            var points = itemsList.Select(item => new
            {
                id = item.id.ToString(),
                vector = item.vector,
                payload = item.metadata
            }).ToArray();

            var payload = new { points = points };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync($"/collections/{CollectionName}/points", content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to bulk upsert {Count} vectors. Status: {StatusCode}, Response: {Response}", 
                    itemsList.Count, response.StatusCode, errorContent);
                throw new InvalidOperationException($"Failed to bulk upsert {itemsList.Count} vectors: {response.StatusCode} - {errorContent}");
            }

            _logger.LogDebug("Successfully bulk upserted {Count} vectors", itemsList.Count);
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            _logger.LogError(ex, "Unexpected error bulk upserting {Count} vectors", itemsList.Count);
            throw new InvalidOperationException($"Failed to bulk upsert {itemsList.Count} vectors: {ex.Message}", ex);
        }
    }

    public async Task<List<QdrantSearchResult>> SearchSimilarAsync(float[] queryVector, int limit = 10, Dictionary<string, object>? filter = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (queryVector == null)
                throw new ArgumentNullException(nameof(queryVector));

            _logger.LogDebug("Searching for {Limit} similar vectors", limit);

            var searchRequest = new
            {
                vector = queryVector,
                limit = limit,
                with_payload = true,
                filter = filter
            };

            var json = JsonSerializer.Serialize(searchRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"/collections/{CollectionName}/points/search", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var searchResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

            var results = new List<QdrantSearchResult>();
            
            if (searchResponse.TryGetProperty("result", out var resultArray) && resultArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in resultArray.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idElement) &&
                        item.TryGetProperty("score", out var scoreElement) &&
                        item.TryGetProperty("payload", out var payloadElement))
                    {
                        var metadata = new Dictionary<string, object>();
                        
                        foreach (var prop in payloadElement.EnumerateObject())
                        {
                            metadata[prop.Name] = prop.Value.ValueKind switch
                            {
                                JsonValueKind.String => prop.Value.GetString()!,
                                JsonValueKind.Number => prop.Value.GetDouble(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                _ => prop.Value.ToString()!
                            };
                        }

                        results.Add(new QdrantSearchResult
                        {
                            Id = idElement.GetString()!,
                            Score = scoreElement.GetSingle(),
                            Metadata = metadata
                        });
                    }
                }
            }

            _logger.LogDebug("Found {Count} search results", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search similar vectors");
            throw new InvalidOperationException($"Failed to search similar vectors: {ex.Message}", ex);
        }
    }

    public async Task<List<string>> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing all collections");

            var response = await _httpClient.GetAsync("/collections", cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var collectionsResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

            var collections = new List<string>();
            
            if (collectionsResponse.TryGetProperty("result", out var resultElement) &&
                resultElement.TryGetProperty("collections", out var collectionsArray) &&
                collectionsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in collectionsArray.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var nameElement))
                    {
                        collections.Add(nameElement.GetString()!);
                    }
                }
            }

            _logger.LogDebug("Found {Count} collections", collections.Count);
            return collections;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list collections");
            throw new InvalidOperationException($"Failed to list collections: {ex.Message}", ex);
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        try
        {
            _logger.LogDebug("Deleting vector with ID: {Id}", id);

            var deleteRequest = new
            {
                points = new[] { id.ToString() }
            };

            var json = JsonSerializer.Serialize(deleteRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"/collections/{CollectionName}/points/delete", content);
            response.EnsureSuccessStatusCode();

            _logger.LogDebug("Successfully deleted vector with ID: {Id}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete vector with ID: {Id}", id);
            throw new InvalidOperationException($"Failed to delete vector with ID '{id}': {ex.Message}", ex);
        }
    }

    public async Task DeleteCollectionAsync()
    {
        try
        {
            _logger.LogInformation("Deleting collection '{CollectionName}'", CollectionName);

            var response = await _httpClient.DeleteAsync($"/collections/{CollectionName}");
            
            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Collection '{CollectionName}' deleted successfully", CollectionName);
            }
            else
            {
                response.EnsureSuccessStatusCode();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete collection '{CollectionName}'", CollectionName);
            throw new InvalidOperationException($"Failed to delete collection '{CollectionName}': {ex.Message}", ex);
        }
    }

    private async Task CreateCollectionAsync()
    {
        var createRequest = new
        {
            vectors = new
            {
                size = VectorSize,
                distance = "Cosine"
            }
        };

        var json = JsonSerializer.Serialize(createRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PutAsync($"/collections/{CollectionName}", content);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Collection '{CollectionName}' created successfully with {VectorSize} dimensions", 
            CollectionName, VectorSize);
    }
}