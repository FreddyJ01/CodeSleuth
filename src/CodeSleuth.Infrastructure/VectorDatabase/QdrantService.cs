using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CodeSleuth.Infrastructure.VectorDatabase;

/// <summary>
/// Service for interacting with Qdrant vector database.
/// Provides methods for managing collections and performing vector operations.
/// </summary>
public class QdrantService : IQdrantService
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantService> _logger;
    private const string CollectionName = "code_chunks";
    private const uint VectorSize = 1536;

    /// <summary>
    /// Initializes a new instance of the QdrantService class.
    /// </summary>
    /// <param name="host">The Qdrant server host. Defaults to "localhost".</param>
    /// <param name="port">The Qdrant server port. Defaults to 6334.</param>
    /// <param name="logger">The logger instance for logging operations.</param>
    public QdrantService(ILogger<QdrantService> logger, string host = "localhost", int port = 6334)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        try
        {
            // Use simple constructor with proper host format (just hostname, not URL)
            _client = new QdrantClient(host, port, https: false);
            
            _logger.LogInformation("QdrantService initialized - host: {Host}, port: {Port}", host, port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize QdrantService with host: {Host}, port: {Port}", host, port);
            throw;
        }
    }

    /// <summary>
    /// Initializes the code_chunks collection if it doesn't exist.
    /// Creates a collection with 1536 dimensions and Cosine distance metric.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when collection creation fails.</exception>
    public async Task InitializeCollectionAsync()
    {
        try
        {
            _logger.LogInformation("Checking if collection '{CollectionName}' exists", CollectionName);

            // Check if collection exists
            var collections = await _client.ListCollectionsAsync();
            var collectionExists = collections.Contains(CollectionName);

            if (collectionExists)
            {
                _logger.LogInformation("Collection '{CollectionName}' already exists", CollectionName);
                return;
            }

            _logger.LogInformation("Creating collection '{CollectionName}' with {VectorSize} dimensions", CollectionName, VectorSize);

            // Create collection with Cosine distance metric
            var vectorParams = new VectorParams
            {
                Size = VectorSize,
                Distance = Distance.Cosine
            };

            await _client.CreateCollectionAsync(CollectionName, vectorParams);
            _logger.LogInformation("Collection '{CollectionName}' created successfully", CollectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize collection '{CollectionName}'", CollectionName);
            throw new InvalidOperationException($"Failed to initialize collection '{CollectionName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Stores a vector with metadata in the collection.
    /// </summary>
    /// <param name="id">The unique identifier for the vector.</param>
    /// <param name="vector">The vector data to store.</param>
    /// <param name="metadata">Additional metadata to associate with the vector.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when vector or metadata is null.</exception>
    /// <exception cref="ArgumentException">Thrown when vector dimensions don't match expected size.</exception>
    /// <exception cref="InvalidOperationException">Thrown when upsert operation fails.</exception>
    public async Task UpsertAsync(Guid id, float[] vector, Dictionary<string, object> metadata)
    {
        if (vector == null)
            throw new ArgumentNullException(nameof(vector));
        
        if (metadata == null)
            throw new ArgumentNullException(nameof(metadata));

        if (vector.Length != VectorSize)
            throw new ArgumentException($"Vector must have {VectorSize} dimensions, but received {vector.Length}", nameof(vector));

        try
        {
            await UpsertWithRetryAsync(id, vector, metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert vector with ID: {Id} after all retry attempts", id);
            throw new InvalidOperationException($"Failed to upsert vector with ID '{id}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Upserts a vector with retry logic for handling transient network issues.
    /// </summary>
    private async Task UpsertWithRetryAsync(Guid id, float[] vector, Dictionary<string, object> metadata, int maxRetries = 3)
    {
        Exception? lastException = null;
        
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogDebug("Upserting vector with ID: {Id} (attempt {Attempt}/{MaxRetries})", id, attempt + 1, maxRetries + 1);

                var pointStruct = new PointStruct
                {
                    Id = new PointId { Uuid = id.ToString() },
                    Vectors = vector,
                    Payload = { }
                };

                // Add metadata to payload
                foreach (var kvp in metadata)
                {
                    pointStruct.Payload[kvp.Key] = ConvertToValue(kvp.Value);
                }

                await _client.UpsertAsync(CollectionName, new[] { pointStruct });
                _logger.LogDebug("Successfully upserted vector with ID: {Id} on attempt {Attempt}", id, attempt + 1);
                return; // Success, exit retry loop
            }
            catch (Grpc.Core.RpcException ex) when (IsRetryableGrpcError(ex) && attempt < maxRetries)
            {
                lastException = ex;
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 1000); // Exponential backoff
                _logger.LogWarning(ex, "Retryable gRPC error for vector ID {Id} on attempt {Attempt}, retrying in {Delay}ms: {Message}", 
                    id, attempt + 1, delay.TotalMilliseconds, ex.Message);
                
                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Non-retryable error for vector ID {Id} on attempt {Attempt}: {Message}", id, attempt + 1, ex.Message);
                throw;
            }
        }
        
        // If we get here, all retries failed
        throw lastException ?? new InvalidOperationException($"Failed to upsert vector with ID '{id}' after {maxRetries + 1} attempts");
    }

    /// <summary>
    /// Determines if a gRPC error is retryable.
    /// </summary>
    private static bool IsRetryableGrpcError(Grpc.Core.RpcException ex)
    {
        return ex.StatusCode switch
        {
            Grpc.Core.StatusCode.Internal => ex.Message.Contains("HTTP/2") || ex.Message.Contains("PROTOCOL_ERROR"),
            Grpc.Core.StatusCode.Unavailable => true,
            Grpc.Core.StatusCode.DeadlineExceeded => true,
            Grpc.Core.StatusCode.ResourceExhausted => true,
            _ => false
        };
    }

    /// <summary>
    /// Searches for vectors similar to the query vector.
    /// </summary>
    /// <param name="queryVector">The query vector to search with.</param>
    /// <param name="limit">The maximum number of results to return. Defaults to 10.</param>
    /// <returns>A list of scored points representing the search results.</returns>
    /// <exception cref="ArgumentNullException">Thrown when queryVector is null.</exception>
    /// <exception cref="ArgumentException">Thrown when queryVector dimensions don't match expected size.</exception>
    /// <exception cref="InvalidOperationException">Thrown when search operation fails.</exception>
    public async Task<List<ScoredPoint>> SearchAsync(float[] queryVector, int limit = 10)
    {
        if (queryVector == null)
            throw new ArgumentNullException(nameof(queryVector));

        if (queryVector.Length != VectorSize)
            throw new ArgumentException($"Query vector must have {VectorSize} dimensions, but received {queryVector.Length}", nameof(queryVector));

        if (limit <= 0)
            throw new ArgumentException("Limit must be greater than 0", nameof(limit));

        try
        {
            _logger.LogDebug("Searching for similar vectors with limit: {Limit}", limit);

            var searchResult = await _client.SearchAsync(
                collectionName: CollectionName,
                vector: queryVector,
                limit: (uint)limit,
                payloadSelector: true // Include all payload fields
            );

            _logger.LogDebug("Search completed, found {Count} results", searchResult.Count);
            return searchResult.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search vectors");
            throw new InvalidOperationException($"Failed to search vectors: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Searches for similar vectors in the collection with metadata filtering.
    /// </summary>
    /// <param name="queryVector">The query vector to search for similar vectors.</param>
    /// <param name="limit">The maximum number of results to return. Defaults to 10.</param>
    /// <param name="filter">Metadata filter conditions to apply to the search.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A list of search results with metadata and content.</returns>
    /// <exception cref="ArgumentNullException">Thrown when queryVector is null.</exception>
    /// <exception cref="ArgumentException">Thrown when queryVector has incorrect dimensions or limit is invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown when search operation fails.</exception>
    public async Task<List<QdrantSearchResult>> SearchSimilarAsync(
        float[] queryVector, 
        int limit = 10, 
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (queryVector == null)
            throw new ArgumentNullException(nameof(queryVector));

        if (queryVector.Length != VectorSize)
            throw new ArgumentException($"Query vector must have {VectorSize} dimensions, but received {queryVector.Length}", nameof(queryVector));

        if (limit <= 0)
            throw new ArgumentException("Limit must be greater than 0", nameof(limit));

        try
        {
            _logger.LogDebug("Searching for similar vectors with limit: {Limit}, filter: {@Filter}", limit, filter);

            // Build filter conditions if provided
            Filter? qdrantFilter = null;
            if (filter != null && filter.Any())
            {
                var conditions = new List<Condition>();
                foreach (var kvp in filter)
                {
                    var condition = new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = kvp.Key,
                            Match = new Match { Keyword = kvp.Value.ToString() }
                        }
                    };
                    conditions.Add(condition);
                }

                qdrantFilter = new Filter
                {
                    Must = { conditions }
                };
            }

            var searchResult = await _client.SearchAsync(
                collectionName: CollectionName,
                vector: queryVector,
                limit: (uint)limit,
                filter: qdrantFilter,
                payloadSelector: true // Include all payload fields
            );

            _logger.LogDebug("Search completed, found {Count} results", searchResult.Count);
            
            // Convert ScoredPoint to QdrantSearchResult
            var results = new List<QdrantSearchResult>();
            foreach (var point in searchResult)
            {
                var metadata = new Dictionary<string, object>();
                var content = string.Empty;

                if (point.Payload != null)
                {
                    foreach (var kvp in point.Payload)
                    {
                        if (kvp.Key == "content")
                        {
                            content = kvp.Value.StringValue ?? string.Empty;
                        }
                        else
                        {
                            metadata[kvp.Key] = ConvertFromValue(kvp.Value);
                        }
                    }
                }

                results.Add(new QdrantSearchResult
                {
                    Id = point.Id?.Uuid ?? string.Empty,
                    Score = point.Score,
                    Content = content,
                    Metadata = metadata
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search vectors with filter");
            throw new InvalidOperationException($"Failed to search vectors: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Lists all collections in the Qdrant instance.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A list of collection names.</returns>
    public async Task<List<string>> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing all collections");
            var collections = await _client.ListCollectionsAsync();
            return collections.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list collections");
            throw new InvalidOperationException($"Failed to list collections: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Converts a Qdrant Value back to a .NET object.
    /// </summary>
    /// <param name="value">The Qdrant Value to convert.</param>
    /// <returns>The converted .NET object.</returns>
    private static object ConvertFromValue(Value value)
    {
        return value.KindCase switch
        {
            Value.KindOneofCase.StringValue => value.StringValue,
            Value.KindOneofCase.IntegerValue => value.IntegerValue,
            Value.KindOneofCase.DoubleValue => value.DoubleValue,
            Value.KindOneofCase.BoolValue => value.BoolValue,
            _ => value.ToString()
        };
    }

    /// <summary>
    /// Deletes the code_chunks collection.
    /// This method is primarily intended for testing purposes.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when collection deletion fails.</exception>
    public async Task DeleteCollectionAsync()
    {
        try
        {
            _logger.LogWarning("Deleting collection '{CollectionName}'", CollectionName);
            await _client.DeleteCollectionAsync(CollectionName);
            _logger.LogInformation("Collection '{CollectionName}' deleted successfully", CollectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete collection '{CollectionName}'", CollectionName);
            throw new InvalidOperationException($"Failed to delete collection '{CollectionName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Converts a .NET object to a Qdrant Value for payload storage.
    /// </summary>
    /// <param name="obj">The object to convert.</param>
    /// <returns>A Qdrant Value representing the object.</returns>
    private static Value ConvertToValue(object obj)
    {
        return obj switch
        {
            string str => new Value { StringValue = str },
            int intVal => new Value { IntegerValue = intVal },
            long longVal => new Value { IntegerValue = longVal },
            float floatVal => new Value { DoubleValue = floatVal },
            double doubleVal => new Value { DoubleValue = doubleVal },
            bool boolVal => new Value { BoolValue = boolVal },
            _ => new Value { StringValue = obj?.ToString() ?? string.Empty }
        };
    }

    /// <summary>
    /// Bulk upserts multiple vectors (not implemented in gRPC version).
    /// </summary>
    public Task UpsertBulkAsync(IEnumerable<(Guid id, float[] vector, Dictionary<string, object> metadata)> items, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Bulk upsert is only available in QdrantRestService");
    }

    /// <summary>
    /// Disposes the QdrantClient resources.
    /// </summary>
    public void Dispose()
    {
        _client?.Dispose();
        _logger.LogDebug("QdrantService disposed");
    }
}