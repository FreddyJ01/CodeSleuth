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
public class QdrantService
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
        _client = new QdrantClient(host, port);
        _logger.LogInformation("QdrantService initialized with host: {Host}, port: {Port}", host, port);
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
            _logger.LogDebug("Upserting vector with ID: {Id}", id);

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
            _logger.LogDebug("Successfully upserted vector with ID: {Id}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert vector with ID: {Id}", id);
            throw new InvalidOperationException($"Failed to upsert vector with ID '{id}': {ex.Message}", ex);
        }
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
    /// Disposes the QdrantClient resources.
    /// </summary>
    public void Dispose()
    {
        _client?.Dispose();
        _logger.LogDebug("QdrantService disposed");
    }
}