using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeSleuth.Infrastructure.VectorDatabase;

/// <summary>
/// Interface for vector database operations.
/// </summary>
public interface IQdrantService
{
    /// <summary>
    /// Initializes the code_chunks collection if it doesn't exist.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InitializeCollectionAsync();

    /// <summary>
    /// Upserts a vector into the database with the given metadata.
    /// </summary>
    /// <param name="id">The unique identifier for the vector.</param>
    /// <param name="vector">The vector data.</param>
    /// <param name="metadata">The metadata associated with the vector.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpsertAsync(Guid id, float[] vector, Dictionary<string, object> metadata);

    /// <summary>
    /// Bulk upserts multiple vectors in a single operation for better performance.
    /// </summary>
    /// <param name="items">Collection of items to upsert (id, vector, metadata).</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpsertBulkAsync(IEnumerable<(Guid id, float[] vector, Dictionary<string, object> metadata)> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for vectors similar to the provided query vector.
    /// </summary>
    /// <param name="queryVector">The query vector to search for similar vectors.</param>
    /// <param name="limit">Maximum number of results to return. Defaults to 10.</param>
    /// <param name="filter">Optional filter criteria for metadata-based filtering.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of search results.</returns>
    Task<List<QdrantSearchResult>> SearchSimilarAsync(
        float[] queryVector, 
        int limit = 10, 
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all collections in the Qdrant database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the list of collection names.</returns>
    Task<List<string>> ListCollectionsAsync(CancellationToken cancellationToken = default);
}