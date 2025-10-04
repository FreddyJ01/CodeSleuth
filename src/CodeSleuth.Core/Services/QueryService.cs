using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using CodeSleuth.Core.Models;
using CodeSleuth.Infrastructure.AI;
using CodeSleuth.Infrastructure.VectorDatabase;

namespace CodeSleuth.Core.Services;

/// <summary>
/// Service that implements RAG (Retrieval Augmented Generation) for code questions.
/// Combines vector search with large language models to provide contextual answers about code.
/// </summary>
public class QueryService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IQdrantService _qdrantService;
    private readonly IChatCompletionService _chatCompletionService;
    private readonly ILogger<QueryService> _logger;

    /// <summary>
    /// Initializes a new instance of the QueryService.
    /// </summary>
    /// <param name="embeddingService">Service for generating text embeddings.</param>
    /// <param name="qdrantService">Service for vector database operations.</param>
    /// <param name="chatCompletionService">Service for LLM chat completions.</param>
    /// <param name="logger">Logger for this service.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public QueryService(
        IEmbeddingService embeddingService,
        IQdrantService qdrantService,
        IChatCompletionService chatCompletionService,
        ILogger<QueryService> logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _qdrantService = qdrantService ?? throw new ArgumentNullException(nameof(qdrantService));
        _chatCompletionService = chatCompletionService ?? throw new ArgumentNullException(nameof(chatCompletionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Asks a question about code in a specific repository using RAG (Retrieval Augmented Generation).
    /// </summary>
    /// <param name="question">The question to ask about the code.</param>
    /// <param name="repoName">The name of the repository to search in.</param>
    /// <param name="maxResults">Maximum number of code chunks to retrieve for context (default: 5).</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A QueryResult containing the AI-generated answer and supporting code references.</returns>
    /// <exception cref="ArgumentException">Thrown when question or repoName is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the operation fails.</exception>
    public async Task<QueryResult> AskQuestionAsync(
        string question, 
        string repoName, 
        int maxResults = 5, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question cannot be null or empty.", nameof(question));
        
        if (string.IsNullOrWhiteSpace(repoName))
            throw new ArgumentException("Repository name cannot be null or empty.", nameof(repoName));

        if (maxResults <= 0)
            throw new ArgumentException("Max results must be greater than zero.", nameof(maxResults));

        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Processing question for repository '{RepoName}': {Question}", repoName, question);

            // Step 1: Generate embedding for the question
            _logger.LogDebug("Generating embedding for question");
            var questionEmbedding = await _embeddingService.GenerateEmbeddingAsync(question, cancellationToken);
            
            if (questionEmbedding == null || questionEmbedding.Length == 0)
            {
                throw new InvalidOperationException("Failed to generate embedding for the question.");
            }

            // Step 2: Search Qdrant for similar code chunks
            _logger.LogDebug("Searching for similar code chunks in repository '{RepoName}' (max results: {MaxResults})", repoName, maxResults);
            var searchResults = await _qdrantService.SearchSimilarAsync(
                questionEmbedding, 
                maxResults, 
                new Dictionary<string, object> { { "repo_name", repoName } },
                cancellationToken);

            _logger.LogInformation("Retrieved {ResultCount} code chunks from vector database", searchResults.Count);

            if (!searchResults.Any())
            {
                _logger.LogWarning("No code chunks found for repository '{RepoName}'", repoName);
                stopwatch.Stop();
                
                return new QueryResult
                {
                    Answer = $"I couldn't find any relevant code in the repository '{repoName}' to answer your question. " +
                            "Please make sure the repository has been indexed and try rephrasing your question.",
                    References = new List<CodeReference>(),
                    Duration = stopwatch.Elapsed
                };
            }

            // Step 3: Build context from retrieved chunks
            _logger.LogDebug("Building context from retrieved code chunks");
            var context = BuildContextFromSearchResults(searchResults);

            // Step 4: Create prompt and get LLM response
            _logger.LogDebug("Generating AI response using chat completion service");
            var llmStopwatch = Stopwatch.StartNew();
            
            var chatHistory = CreateChatHistory(context, question);
            var response = await _chatCompletionService.GetChatMessageContentAsync(
                chatHistory, 
                cancellationToken: cancellationToken);
            
            llmStopwatch.Stop();
            _logger.LogInformation("LLM response generated in {LlmDuration}ms", llmStopwatch.ElapsedMilliseconds);

            if (response?.Content == null)
            {
                throw new InvalidOperationException("Failed to get a response from the chat completion service.");
            }

            // Step 5: Extract references from search results
            var references = ExtractReferencesFromSearchResults(searchResults);

            stopwatch.Stop();
            
            _logger.LogInformation("Question processed successfully in {TotalDuration}ms. Answer length: {AnswerLength} characters, References: {ReferenceCount}", 
                stopwatch.ElapsedMilliseconds, response.Content.Length, references.Count);

            return new QueryResult
            {
                Answer = response.Content,
                References = references,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Question processing was cancelled for repository '{RepoName}'", repoName);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error occurred while processing question for repository '{RepoName}': {Question}", repoName, question);
            
            return new QueryResult
            {
                Answer = "I encountered an error while processing your question. Please try again later or rephrase your question.",
                References = new List<CodeReference>(),
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// Builds a formatted context string from search results.
    /// </summary>
    /// <param name="searchResults">The search results from the vector database.</param>
    /// <returns>A formatted context string for the LLM prompt.</returns>
    private static string BuildContextFromSearchResults(List<QdrantSearchResult> searchResults)
    {
        var contextParts = new List<string>();

        foreach (var result in searchResults)
        {
            if (result.Metadata.TryGetValue("file_path", out var filePathObj) &&
                result.Metadata.TryGetValue("start_line", out var startLineObj) &&
                result.Metadata.TryGetValue("end_line", out var endLineObj) &&
                !string.IsNullOrEmpty(result.Content))
            {
                var filePath = filePathObj?.ToString() ?? "unknown";
                var startLine = Convert.ToInt32(startLineObj);
                var endLine = Convert.ToInt32(endLineObj);

                var contextPart = $"File: {filePath} (lines {startLine}-{endLine})\n{result.Content}\n";
                contextParts.Add(contextPart);
            }
        }

        return string.Join("\n---\n\n", contextParts);
    }

    /// <summary>
    /// Creates a chat history with system and user messages for the LLM.
    /// </summary>
    /// <param name="context">The code context retrieved from the vector database.</param>
    /// <param name="question">The user's question.</param>
    /// <returns>A ChatHistory object ready for the chat completion service.</returns>
    private static ChatHistory CreateChatHistory(string context, string question)
    {
        var chatHistory = new ChatHistory();

        // System message
        chatHistory.AddSystemMessage(
            "You are an expert code assistant. Answer questions based on the provided code context. " +
            "Be specific and reference file names and line numbers when relevant. " +
            "If the answer isn't clearly supported by the provided context, say so clearly. " +
            "Focus on accuracy and provide concrete examples from the code when possible.");

        // User message with context and question
        var userMessage = $"Context from codebase:\n{context}\n\n" +
                         $"Question: {question}\n\n" +
                         "Provide a clear answer based on the code above.";
        
        chatHistory.AddUserMessage(userMessage);

        return chatHistory;
    }

    /// <summary>
    /// Extracts code references from search results.
    /// </summary>
    /// <param name="searchResults">The search results from the vector database.</param>
    /// <returns>A list of CodeReference objects.</returns>
    private static List<CodeReference> ExtractReferencesFromSearchResults(List<QdrantSearchResult> searchResults)
    {
        var references = new List<CodeReference>();

        foreach (var result in searchResults)
        {
            if (result.Metadata.TryGetValue("file_path", out var filePathObj) &&
                result.Metadata.TryGetValue("start_line", out var startLineObj) &&
                result.Metadata.TryGetValue("end_line", out var endLineObj))
            {
                var reference = new CodeReference
                {
                    FilePath = filePathObj?.ToString() ?? "",
                    StartLine = Convert.ToInt32(startLineObj),
                    EndLine = Convert.ToInt32(endLineObj),
                    SimilarityScore = result.Score
                };

                references.Add(reference);
            }
        }

        // Sort by similarity score (highest first)
        return references.OrderByDescending(r => r.SimilarityScore).ToList();
    }

    /// <summary>
    /// Validates that the required services are properly configured.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>True if all services are available and configured correctly.</returns>
    /// <exception cref="InvalidOperationException">Thrown when services are not properly configured.</exception>
    public async Task<bool> ValidateServicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Validating QueryService dependencies");

            // Test embedding service
            var testEmbedding = await _embeddingService.GenerateEmbeddingAsync("test", cancellationToken);
            if (testEmbedding == null || testEmbedding.Length == 0)
            {
                throw new InvalidOperationException("EmbeddingService is not properly configured.");
            }

            // Test vector database service
            var collections = await _qdrantService.ListCollectionsAsync(cancellationToken);
            if (collections == null)
            {
                throw new InvalidOperationException("QdrantService is not properly configured.");
            }

            // Test chat completion service - this is basic validation
            if (_chatCompletionService == null)
            {
                throw new InvalidOperationException("ChatCompletionService is not properly configured.");
            }

            _logger.LogInformation("All QueryService dependencies are properly configured");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service validation failed");
            throw;
        }
    }

    /// <summary>
    /// Gets statistics about the available repositories and their indexed content.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A dictionary containing repository statistics.</returns>
    public async Task<Dictionary<string, object>> GetRepositoryStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving repository statistics");

            var collections = await _qdrantService.ListCollectionsAsync(cancellationToken);
            var stats = new Dictionary<string, object>
            {
                { "available_collections", collections?.Count ?? 0 },
                { "timestamp", DateTime.UtcNow }
            };

            // If we have a default collection, get point count
            if (collections?.Any() == true)
            {
                // Note: This would need to be implemented in QdrantService
                // stats["total_indexed_chunks"] = await _qdrantService.GetPointCountAsync();
            }

            _logger.LogDebug("Repository statistics retrieved successfully");
            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving repository statistics");
            return new Dictionary<string, object>
            {
                { "error", ex.Message },
                { "timestamp", DateTime.UtcNow }
            };
        }
    }
}