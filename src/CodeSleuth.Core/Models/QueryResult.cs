namespace CodeSleuth.Core.Models;

/// <summary>
/// Represents the result of a code search query, including the generated answer and relevant code references.
/// </summary>
public class QueryResult
{
    /// <summary>
    /// Gets or sets the AI-generated answer to the query based on the code analysis.
    /// </summary>
    public string Answer { get; set; } = "";

    /// <summary>
    /// Gets or sets the list of code references that support the answer.
    /// </summary>
    public List<CodeReference> References { get; set; } = new();

    /// <summary>
    /// Gets or sets the duration it took to process the query and generate the result.
    /// </summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Represents a reference to a specific section of code that is relevant to a query.
/// </summary>
public class CodeReference
{
    /// <summary>
    /// Gets or sets the file path of the referenced code.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Gets or sets the starting line number of the referenced code section.
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Gets or sets the ending line number of the referenced code section.
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Gets or sets the similarity score indicating how relevant this code reference is to the query.
    /// Score ranges from 0.0 (not relevant) to 1.0 (highly relevant).
    /// </summary>
    public float SimilarityScore { get; set; }
}
