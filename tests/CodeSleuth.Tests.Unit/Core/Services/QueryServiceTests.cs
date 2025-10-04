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
/// Unit tests for QueryService.
/// Tests the RAG (Retrieval Augmented Generation) functionality.
/// </summary>
public class QueryServiceTests
{
    [Fact]
    public void Constructor_WithNullEmbeddingService_ShouldThrowArgumentNullException()
    {
        // Arrange
        var mockQdrantService = CreateMockQdrantService();
        var mockChatService = new Mock<IChatCompletionService>();
        var mockLogger = new Mock<ILogger<QueryService>>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new QueryService(
            null!,
            mockQdrantService.Object,
            mockChatService.Object,
            mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullQdrantService_ShouldThrowArgumentNullException()
    {
        // Arrange
        var mockEmbeddingService = new Mock<IEmbeddingService>();
        var mockChatService = new Mock<IChatCompletionService>();
        var mockLogger = new Mock<ILogger<QueryService>>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new QueryService(
            mockEmbeddingService.Object,
            null!,
            mockChatService.Object,
            mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullChatCompletionService_ShouldThrowArgumentNullException()
    {
        // Arrange
        var mockEmbeddingService = new Mock<IEmbeddingService>();
        var mockQdrantService = CreateMockQdrantService();
        var mockLogger = new Mock<ILogger<QueryService>>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new QueryService(
            mockEmbeddingService.Object,
            mockQdrantService.Object,
            null!,
            mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange
        var mockEmbeddingService = new Mock<IEmbeddingService>();
        var mockQdrantService = CreateMockQdrantService();
        var mockChatService = new Mock<IChatCompletionService>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new QueryService(
            mockEmbeddingService.Object,
            mockQdrantService.Object,
            mockChatService.Object,
            null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AskQuestionAsync_WithInvalidQuestion_ShouldThrowArgumentException(string? question)
    {
        // Arrange
        var queryService = CreateQueryService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            queryService.AskQuestionAsync(question!, "test-repo"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AskQuestionAsync_WithInvalidRepoName_ShouldThrowArgumentException(string? repoName)
    {
        // Arrange
        var queryService = CreateQueryService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            queryService.AskQuestionAsync("test question", repoName!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public async Task AskQuestionAsync_WithInvalidMaxResults_ShouldThrowArgumentException(int maxResults)
    {
        // Arrange
        var queryService = CreateQueryService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            queryService.AskQuestionAsync("test question", "test-repo", maxResults));
    }

    [Fact]
    public void QueryService_CanBeConstructed_WithValidDependencies()
    {
        // Arrange & Act
        var queryService = CreateQueryService();

        // Assert
        Assert.NotNull(queryService);
    }

    [Fact]
    public async Task AskQuestionAsync_WithCancellation_ShouldThrowOperationCancelledException()
    {
        // Arrange
        var mockEmbeddingService = new Mock<IEmbeddingService>();
        var mockQdrantService = CreateMockQdrantService();
        var mockChatService = new Mock<IChatCompletionService>();
        var mockLogger = new Mock<ILogger<QueryService>>();

        // Configure mock to throw OperationCanceledException when cancellation token is cancelled
        mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var queryService = new QueryService(
            mockEmbeddingService.Object,
            mockQdrantService.Object,
            mockChatService.Object,
            mockLogger.Object);

        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            queryService.AskQuestionAsync("test question", "test-repo", cancellationToken: cancellationToken));
    }

    private static QueryService CreateQueryService()
    {
        var mockEmbeddingService = new Mock<IEmbeddingService>();
        var mockQdrantService = CreateMockQdrantService();
        var mockChatCompletionService = new Mock<IChatCompletionService>();
        var mockLogger = new Mock<ILogger<QueryService>>();

        return new QueryService(
            mockEmbeddingService.Object,
            mockQdrantService.Object,
            mockChatCompletionService.Object,
            mockLogger.Object);
    }

    private static Mock<IEmbeddingService> CreateMockEmbeddingService()
    {
        return new Mock<IEmbeddingService>();
    }

    private static Mock<IQdrantService> CreateMockQdrantService()
    {
        return new Mock<IQdrantService>();
    }
}