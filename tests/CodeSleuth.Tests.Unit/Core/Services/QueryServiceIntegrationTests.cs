using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using Xunit;
using CodeSleuth.Core.Models;
using CodeSleuth.Core.Services;
using CodeSleuth.Infrastructure.AI;
using CodeSleuth.Infrastructure.VectorDatabase;

namespace CodeSleuth.Tests.Unit.Core.Services;

/// <summary>
/// Integration tests for QueryService that demonstrate the complete RAG pipeline.
/// </summary>
public class QueryServiceIntegrationTests
{
    /// <summary>
    /// Test that demonstrates asking a question about the indexed repository
    /// and getting a meaningful answer with code references.
    /// </summary>
    [Fact]
    public async Task AskQuestionAsync_AboutCodeSleuthProject_ShouldReturnAnswerWithReferences()
    {
        // Arrange
        var mockEmbeddingService = CreateMockEmbeddingService();
        var mockQdrantService = CreateMockQdrantService();
        var mockChatService = CreateMockChatCompletionService();
        var mockLogger = new Mock<ILogger<QueryService>>();

        var queryService = new QueryService(
            mockEmbeddingService.Object,
            mockQdrantService.Object,
            mockChatService.Object,
            mockLogger.Object);

        var question = "How does the QueryService implement RAG (Retrieval Augmented Generation)?";
        var repositoryName = "CodeSleuth";

        // Act
        var result = await queryService.AskQuestionAsync(
            question, 
            repositoryName, 
            maxResults: 5);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Answer);
        Assert.NotEmpty(result.Answer);
        Assert.NotNull(result.References);
        Assert.NotEmpty(result.References);
        Assert.True(result.Duration > TimeSpan.Zero);

        // Verify the answer contains relevant information about RAG
        Assert.Contains("RAG", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("retrieval", result.Answer, StringComparison.OrdinalIgnoreCase);
        
        // Verify we have code references
        foreach (var reference in result.References)
        {
            Assert.NotNull(reference.FilePath);
            Assert.NotEmpty(reference.FilePath);
            Assert.True(reference.SimilarityScore >= 0 && reference.SimilarityScore <= 1);
        }

        // Verify the mocks were called correctly
        mockEmbeddingService.Verify(
            x => x.GenerateEmbeddingAsync(question, It.IsAny<CancellationToken>()), 
            Times.Once);
        
        mockQdrantService.Verify(
            x => x.SearchSimilarAsync(
                It.IsAny<float[]>(), 
                It.IsAny<int>(), 
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        mockChatService.Verify(
            x => x.GetChatMessageContentsAsync(
                It.IsAny<ChatHistory>(), 
                It.IsAny<PromptExecutionSettings>(), 
                It.IsAny<Kernel>(), 
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Test asking a specific question about the EmbeddingService implementation.
    /// </summary>
    [Fact]
    public async Task AskQuestionAsync_AboutEmbeddingService_ShouldReturnRelevantCodeReferences()
    {
        // Arrange
        var mockEmbeddingService = CreateMockEmbeddingService();
        var mockQdrantService = CreateMockQdrantServiceWithEmbeddingResults();
        var mockChatService = CreateMockChatCompletionService();
        var mockLogger = new Mock<ILogger<QueryService>>();

        var queryService = new QueryService(
            mockEmbeddingService.Object,
            mockQdrantService.Object,
            mockChatService.Object,
            mockLogger.Object);

        var question = "How does the EmbeddingService handle retries and error handling?";
        var repositoryName = "CodeSleuth";

        // Act
        var result = await queryService.AskQuestionAsync(question, repositoryName);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("embedding", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("retry", result.Answer, StringComparison.OrdinalIgnoreCase);
        
        // Should have references to EmbeddingService files
        Assert.Contains(result.References, r => r.FilePath.Contains("EmbeddingService"));
    }

    private static Mock<IEmbeddingService> CreateMockEmbeddingService()
    {
        var mock = new Mock<IEmbeddingService>();
        
        // Return a sample embedding vector
        var sampleEmbedding = new float[1536]; // Typical OpenAI embedding size
        for (int i = 0; i < sampleEmbedding.Length; i++)
        {
            sampleEmbedding[i] = (float)(0.1 * Math.Sin(i * 0.1));
        }

        mock.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleEmbedding);

        return mock;
    }

    private static Mock<IQdrantService> CreateMockQdrantService()
    {
        var mock = new Mock<IQdrantService>();

        // Return sample search results for RAG-related queries
        var searchResults = new List<QdrantSearchResult>
        {
            new QdrantSearchResult
            {
                Id = "query-service-1",
                Score = 0.95f,
                Content = @"/// <summary>
/// Handles natural language questions about code repositories using RAG.
/// This service implements a complete Retrieval Augmented Generation pipeline:
/// 1. Generate embedding for the user's question
/// 2. Search for similar code chunks in vector database
/// 3. Build context from retrieved code
/// 4. Generate AI response using the context
/// </summary>
public class QueryService",
                Metadata = new Dictionary<string, object>
                {
                    ["file_path"] = "src/CodeSleuth.Core/Services/QueryService.cs",
                    ["repository"] = "CodeSleuth",
                    ["class_name"] = "QueryService",
                    ["start_line"] = 1,
                    ["end_line"] = 10
                }
            },
            new QdrantSearchResult
            {
                Id = "query-service-2", 
                Score = 0.88f,
                Content = @"public async Task<QueryResult> AskQuestionAsync(
    string question,
    string repositoryName,
    int maxResults = 10,
    CancellationToken cancellationToken = default)
{
    // Step 1: Generate embedding for the question
    var questionEmbedding = await _embeddingService.GenerateEmbeddingAsync(question, cancellationToken);
    
    // Step 2: Search for similar code chunks
    var searchResults = await _qdrantService.SearchSimilarAsync(questionEmbedding, maxResults);",
                Metadata = new Dictionary<string, object>
                {
                    ["file_path"] = "src/CodeSleuth.Core/Services/QueryService.cs",
                    ["repository"] = "CodeSleuth",
                    ["method_name"] = "AskQuestionAsync",
                    ["start_line"] = 50,
                    ["end_line"] = 70
                }
            }
        };

        mock.Setup(x => x.SearchSimilarAsync(
                It.IsAny<float[]>(), 
                It.IsAny<int>(), 
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        return mock;
    }

    private static Mock<IQdrantService> CreateMockQdrantServiceWithEmbeddingResults()
    {
        var mock = new Mock<IQdrantService>();

        var searchResults = new List<QdrantSearchResult>
        {
            new QdrantSearchResult
            {
                Id = "embedding-service-1",
                Score = 0.92f,
                Content = @"/// <summary>
/// Service for generating embeddings using Azure OpenAI or OpenAI direct endpoints.
/// Supports both single text and batch processing with retry logic.
/// </summary>
public class EmbeddingService : IEmbeddingService
{
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(async () => {
            // Implementation with retry logic
        });
    }",
                Metadata = new Dictionary<string, object>
                {
                    ["file_path"] = "src/CodeSleuth.Infrastructure/AI/EmbeddingService.cs",
                    ["repository"] = "CodeSleuth",
                    ["start_line"] = 25,
                    ["end_line"] = 45
                }
            }
        };

        mock.Setup(x => x.SearchSimilarAsync(
                It.IsAny<float[]>(), 
                It.IsAny<int>(), 
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        return mock;
    }

    private static Mock<IChatCompletionService> CreateMockChatCompletionService()
    {
        var mock = new Mock<IChatCompletionService>();

        mock.Setup(x => x.GetChatMessageContentsAsync(
                It.IsAny<ChatHistory>(),
                It.IsAny<PromptExecutionSettings>(),
                It.IsAny<Kernel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatHistory chatHistory, PromptExecutionSettings settings, Kernel kernel, CancellationToken ct) =>
            {
                // Generate a contextual response based on the user's question
                var userMessage = "";
                foreach (var message in chatHistory)
                {
                    if (message.Role == AuthorRole.User)
                    {
                        userMessage = message.Content ?? "";
                        break;
                    }
                }

                string response;
                if (userMessage.Contains("RAG", StringComparison.OrdinalIgnoreCase))
                {
                    response = @"The QueryService implements RAG (Retrieval Augmented Generation) through a comprehensive pipeline:

1. **Question Embedding**: First, it generates an embedding vector for the user's question using the EmbeddingService
2. **Vector Retrieval**: It searches the Qdrant vector database for similar code chunks using vector similarity
3. **Context Building**: Retrieved code chunks are assembled into a context string with file paths and content
4. **Augmented Generation**: The context is provided to an LLM along with the original question to generate an informed response

The implementation includes proper error handling, cancellation token support, and metadata filtering for repository-specific searches.";
                }
                else if (userMessage.Contains("embedding", StringComparison.OrdinalIgnoreCase))
                {
                    response = @"The EmbeddingService handles retries and error handling through:

1. **Exponential Backoff**: Uses configurable retry attempts with exponential backoff delays
2. **Exception Wrapping**: Wraps Azure OpenAI exceptions in custom EmbeddingGenerationException
3. **Validation**: Validates input parameters and throws appropriate ArgumentExceptions
4. **Logging**: Comprehensive logging for debugging and monitoring
5. **Cancellation Support**: Respects cancellation tokens throughout the operation

The service supports both Azure OpenAI and direct OpenAI endpoints with automatic endpoint detection.";
                }
                else
                {
                    response = "Based on the retrieved code context, I can provide information about the CodeSleuth project implementation.";
                }

                return new List<ChatMessageContent>
                {
                    new ChatMessageContent(AuthorRole.Assistant, response)
                };
            });

        return mock;
    }
}