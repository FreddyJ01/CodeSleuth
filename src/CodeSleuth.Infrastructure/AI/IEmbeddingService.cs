using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeSleuth.Infrastructure.AI;

/// <summary>
/// Interface for embedding generation services.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generates an embedding vector for the given text.
    /// </summary>
    /// <param name="text">The text to generate an embedding for.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the embedding vector.</returns>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embedding vectors for multiple texts in a batch operation.
    /// </summary>
    /// <param name="texts">The collection of texts to generate embeddings for.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the embedding vectors.</returns>
    Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken = default);
}