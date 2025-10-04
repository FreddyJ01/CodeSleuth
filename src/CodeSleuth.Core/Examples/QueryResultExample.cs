using CodeSleuth.Core.Models;
using System.Diagnostics;

namespace CodeSleuth.Examples;

/// <summary>
/// Example class demonstrating usage of QueryResult and CodeReference models.
/// This shows how the models would be used in a real RAG (Retrieval-Augmented Generation) scenario.
/// </summary>
public static class QueryResultExample
{
    /// <summary>
    /// Creates a sample QueryResult that might be returned from a code search query.
    /// </summary>
    /// <returns>A sample QueryResult with multiple code references.</returns>
    public static QueryResult CreateSampleQueryResult()
    {
        var stopwatch = Stopwatch.StartNew();
        
        // Simulate processing time
        Thread.Sleep(10);
        
        stopwatch.Stop();
        
        return new QueryResult
        {
            Answer = @"The repository uses dependency injection through the built-in .NET Core DI container. 
The main configuration happens in Program.cs where services are registered using builder.Services.AddSingleton() 
and builder.Services.AddScoped() methods. The IndexingService class demonstrates constructor injection 
by accepting multiple service dependencies in its constructor.",
            
            References = new List<CodeReference>
            {
                new CodeReference
                {
                    FilePath = "src/CodeSleuth.API/Program.cs",
                    StartLine = 15,
                    EndLine = 45,
                    SimilarityScore = 0.95f
                },
                new CodeReference
                {
                    FilePath = "src/CodeSleuth.Core/Services/IndexingService.cs",
                    StartLine = 25,
                    EndLine = 35,
                    SimilarityScore = 0.89f
                },
                new CodeReference
                {
                    FilePath = "src/CodeSleuth.API/Controllers/RepositoryController.cs",
                    StartLine = 40,
                    EndLine = 45,
                    SimilarityScore = 0.76f
                }
            },
            
            Duration = stopwatch.Elapsed
        };
    }

    /// <summary>
    /// Demonstrates how to work with QueryResult data.
    /// </summary>
    /// <param name="queryResult">The query result to analyze.</param>
    public static void AnalyzeQueryResult(QueryResult queryResult)
    {
        Console.WriteLine($"Query Answer: {queryResult.Answer}");
        Console.WriteLine($"Processing Duration: {queryResult.Duration.TotalMilliseconds:F2}ms");
        Console.WriteLine($"Number of References: {queryResult.References.Count}");
        Console.WriteLine();

        // Sort references by similarity score (highest first)
        var sortedReferences = queryResult.References
            .OrderByDescending(r => r.SimilarityScore)
            .ToList();

        Console.WriteLine("Code References (sorted by relevance):");
        for (int i = 0; i < sortedReferences.Count; i++)
        {
            var reference = sortedReferences[i];
            Console.WriteLine($"  {i + 1}. {reference.FilePath}");
            Console.WriteLine($"     Lines: {reference.StartLine}-{reference.EndLine}");
            Console.WriteLine($"     Similarity: {reference.SimilarityScore:P1}");
            Console.WriteLine();
        }

        // Calculate statistics
        if (queryResult.References.Any())
        {
            var avgScore = queryResult.References.Average(r => r.SimilarityScore);
            var maxScore = queryResult.References.Max(r => r.SimilarityScore);
            var minScore = queryResult.References.Min(r => r.SimilarityScore);
            var totalLines = queryResult.References.Sum(r => r.EndLine - r.StartLine + 1);

            Console.WriteLine("Statistics:");
            Console.WriteLine($"  Average Similarity: {avgScore:P1}");
            Console.WriteLine($"  Best Match: {maxScore:P1}");
            Console.WriteLine($"  Weakest Match: {minScore:P1}");
            Console.WriteLine($"  Total Lines Referenced: {totalLines}");
        }
    }

    /// <summary>
    /// Filters references to only include high-quality matches.
    /// </summary>
    /// <param name="queryResult">The query result to filter.</param>
    /// <param name="minimumScore">The minimum similarity score to include.</param>
    /// <returns>A new QueryResult with only high-quality references.</returns>
    public static QueryResult FilterHighQualityReferences(QueryResult queryResult, float minimumScore = 0.8f)
    {
        var filteredReferences = queryResult.References
            .Where(r => r.SimilarityScore >= minimumScore)
            .ToList();

        return new QueryResult
        {
            Answer = queryResult.Answer,
            References = filteredReferences,
            Duration = queryResult.Duration
        };
    }

    /// <summary>
    /// Creates a summary of code references by file.
    /// </summary>
    /// <param name="queryResult">The query result to summarize.</param>
    /// <returns>A dictionary mapping file paths to reference counts.</returns>
    public static Dictionary<string, int> GetReferencesByFile(QueryResult queryResult)
    {
        return queryResult.References
            .GroupBy(r => r.FilePath)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Example of how to create a QueryResult programmatically.
    /// </summary>
    public static void CreateQueryResultExample()
    {
        var startTime = DateTime.UtcNow;
        
        // Simulate query processing
        var queryResult = new QueryResult();
        
        // Set the AI-generated answer
        queryResult.Answer = "The authentication is handled using JWT tokens. The middleware validates tokens on each request.";
        
        // Add code references
        queryResult.References.Add(new CodeReference
        {
            FilePath = "src/Middleware/AuthenticationMiddleware.cs",
            StartLine = 20,
            EndLine = 35,
            SimilarityScore = 0.92f
        });
        
        queryResult.References.Add(new CodeReference
        {
            FilePath = "src/Services/AuthService.cs",
            StartLine = 45,
            EndLine = 60,
            SimilarityScore = 0.88f
        });
        
        // Set duration
        queryResult.Duration = DateTime.UtcNow - startTime;
        
        Console.WriteLine("Created QueryResult:");
        AnalyzeQueryResult(queryResult);
    }
}