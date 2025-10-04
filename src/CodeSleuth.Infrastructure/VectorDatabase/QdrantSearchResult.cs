namespace CodeSleuth.Infrastructure.VectorDatabase;

/// <summary>
/// Represents a search result from Qdrant vector database.
/// </summary>
public class QdrantSearchResult
{
    /// <summary>
    /// Gets or sets the unique identifier of the search result.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the similarity score of the search result.
    /// Higher scores indicate better matches.
    /// </summary>
    public float Score { get; set; }

    /// <summary>
    /// Gets or sets the content/text of the code chunk.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the metadata associated with the search result.
    /// This typically includes file path, line numbers, and other contextual information.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}