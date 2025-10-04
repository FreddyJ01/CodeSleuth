using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using Xunit;
using CodeSleuth.API.Controllers;
using CodeSleuth.Core.Models;
using CodeSleuth.Core.Services;
using CodeSleuth.Infrastructure.AI;
using CodeSleuth.Infrastructure.VectorDatabase;

namespace CodeSleuth.Tests.Unit.API.Controllers;

/// <summary>
/// Unit tests for RepositoryController.
/// These tests focus on the controller logic without requiring actual service implementations.
/// </summary>
public class RepositoryControllerTests : IDisposable
{
    private readonly Mock<ILogger<RepositoryController>> _mockLogger;
    private readonly RepositoryController _controller;

    public RepositoryControllerTests()
    {
        _mockLogger = new Mock<ILogger<RepositoryController>>();
        
        // Clear any static state from previous tests
        ClearStaticState();

        // Create mock dependencies for IndexingService
        var mockGitLogger = Mock.Of<ILogger<CodeSleuth.Infrastructure.Git.GitService>>();
        var mockCodeParsingLogger = Mock.Of<ILogger<CodeParsingService>>();
        var mockEmbeddingLogger = Mock.Of<ILogger<CodeSleuth.Infrastructure.AI.EmbeddingService>>();
        var mockQdrantLogger = Mock.Of<ILogger<CodeSleuth.Infrastructure.VectorDatabase.QdrantService>>();

        // Create real service instances for testing (they won't be called in these unit tests)
        var gitService = new CodeSleuth.Infrastructure.Git.GitService(mockGitLogger, "./test-repos");
        var codeParsingService = new CodeParsingService(mockCodeParsingLogger);
        var embeddingService = new CodeSleuth.Infrastructure.AI.EmbeddingService(
            "https://test.example.com", "dummy-key", "test-model", mockEmbeddingLogger);
        var qdrantService = new CodeSleuth.Infrastructure.VectorDatabase.QdrantService(mockQdrantLogger, "localhost");

        var indexingService = new IndexingService(gitService, codeParsingService, embeddingService, qdrantService, Mock.Of<ILogger<IndexingService>>());
        
        // Create mock QueryService for testing
        var mockQueryService = new Mock<QueryService>(
            embeddingService, 
            qdrantService, 
            Mock.Of<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>(),
            Mock.Of<ILogger<QueryService>>()) { CallBase = false };
        
        _controller = new RepositoryController(indexingService, mockQueryService.Object, _mockLogger.Object);
    }

    private static void ClearStaticState()
    {
        // Use reflection to clear the static dictionaries in RepositoryController
        var controllerType = typeof(RepositoryController);
        var indexingStatusField = controllerType.GetField("_indexingStatus", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var completedRepositoriesField = controllerType.GetField("_completedRepositories", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var cancellationTokensField = controllerType.GetField("_cancellationTokens", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        if (indexingStatusField?.GetValue(null) is System.Collections.Concurrent.ConcurrentDictionary<string, IndexingProgress> indexingStatus)
        {
            indexingStatus.Clear();
        }
        
        if (completedRepositoriesField?.GetValue(null) is System.Collections.Concurrent.ConcurrentDictionary<string, string> completedRepositories)
        {
            completedRepositories.Clear();
        }
        
        if (cancellationTokensField?.GetValue(null) is System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> cancellationTokens)
        {
            foreach (var kvp in cancellationTokens)
            {
                kvp.Value.Dispose();
            }
            cancellationTokens.Clear();
        }
    }

    public void Dispose()
    {
        ClearStaticState();
    }

        [Fact]
        public void Constructor_WithNullIndexingService_ShouldThrowArgumentNullException()
        {
            // Arrange
            var mockEmbeddingService = Mock.Of<IEmbeddingService>();
            var mockQdrantService = Mock.Of<IQdrantService>();
            var mockChatCompletionService = Mock.Of<IChatCompletionService>();
            var mockQueryLogger = Mock.Of<ILogger<QueryService>>();
            var mockQueryService = new QueryService(mockEmbeddingService, mockQdrantService, mockChatCompletionService, mockQueryLogger);
            var mockLogger = Mock.Of<ILogger<RepositoryController>>();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new RepositoryController(null!, mockQueryService, mockLogger));
        }    [Fact]
    public void Constructor_WithNullQueryService_ShouldThrowArgumentNullException()
    {
        // Arrange
        var mockGitLogger = Mock.Of<ILogger<CodeSleuth.Infrastructure.Git.GitService>>();
        var mockCodeParsingLogger = Mock.Of<ILogger<CodeParsingService>>();
        var mockEmbeddingLogger = Mock.Of<ILogger<CodeSleuth.Infrastructure.AI.EmbeddingService>>();
        var mockQdrantLogger = Mock.Of<ILogger<CodeSleuth.Infrastructure.VectorDatabase.QdrantService>>();

        var gitService = new CodeSleuth.Infrastructure.Git.GitService(mockGitLogger, "./test-repos");
        var codeParsingService = new CodeParsingService(mockCodeParsingLogger);
        var embeddingService = new CodeSleuth.Infrastructure.AI.EmbeddingService(
            "https://test.example.com", "dummy-key", "test-model", mockEmbeddingLogger);
        var qdrantService = new CodeSleuth.Infrastructure.VectorDatabase.QdrantService(mockQdrantLogger, "localhost");
        var indexingService = new IndexingService(gitService, codeParsingService, embeddingService, qdrantService, Mock.Of<ILogger<IndexingService>>());

                // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new RepositoryController(indexingService, null!, _mockLogger.Object));
    }

    [Fact]
    public void IndexRepository_WithEmptyRepoUrl_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new IndexRequest("", "test-repo");

        // Act
        var result = _controller.IndexRepository(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Invalid Request", problemDetails.Title);
        Assert.Contains("Repository URL and name are required", problemDetails.Detail);
    }

    [Fact]
    public void IndexRepository_WithEmptyRepoName_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new IndexRequest("https://github.com/test/repo.git", "");

        // Act
        var result = _controller.IndexRepository(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Invalid Request", problemDetails.Title);
        Assert.Contains("Repository URL and name are required", problemDetails.Detail);
    }

    [Fact]
    public void IndexRepository_WithWhitespaceRepoUrl_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new IndexRequest("   ", "test-repo");

        // Act
        var result = _controller.IndexRepository(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Invalid Request", problemDetails.Title);
    }

    [Fact]
    public void IndexRepository_WithWhitespaceRepoName_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new IndexRequest("https://github.com/test/repo.git", "   ");

        // Act
        var result = _controller.IndexRepository(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Invalid Request", problemDetails.Title);
    }

    [Fact]
    public void IndexRepository_WithValidRequest_ShouldReturnAccepted()
    {
        // Arrange
        var request = new IndexRequest("https://github.com/test/repo.git", "test-repo");

        // Act
        var result = _controller.IndexRepository(request);

        // Assert
        var acceptedResult = Assert.IsType<AcceptedResult>(result);
        var response = Assert.IsType<IndexResponse>(acceptedResult.Value);
        Assert.Equal("Indexing started", response.Message);
        Assert.Equal("test-repo", response.RepoName);
    }

    [Fact]
    public void IndexRepository_WithSameRepoTwice_ShouldReturnConflict()
    {
        // Arrange
        var request = new IndexRequest("https://github.com/test/repo.git", "test-repo");

        // Act
        var firstResult = _controller.IndexRepository(request);
        var secondResult = _controller.IndexRepository(request);

        // Assert
        Assert.IsType<AcceptedResult>(firstResult);
        
        var conflictResult = Assert.IsType<ConflictObjectResult>(secondResult);
        var problemDetails = Assert.IsType<ProblemDetails>(conflictResult.Value);
        Assert.Equal("Indexing In Progress", problemDetails.Title);
        Assert.Contains("already being indexed", problemDetails.Detail);
    }

    [Fact]
    public void GetIndexingStatus_WithEmptyRepoName_ShouldReturnBadRequest()
    {
        // Arrange & Act
        var result = _controller.GetIndexingStatus("");

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Invalid Request", problemDetails.Title);
        Assert.Contains("Repository name is required", problemDetails.Detail);
    }

    [Fact]
    public void GetIndexingStatus_WithWhitespaceRepoName_ShouldReturnBadRequest()
    {
        // Arrange & Act
        var result = _controller.GetIndexingStatus("   ");

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Invalid Request", problemDetails.Title);
    }

    [Fact]
    public void GetIndexingStatus_WithNonExistentRepo_ShouldReturnNotFound()
    {
        // Arrange & Act
        var result = _controller.GetIndexingStatus("non-existent-repo");

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(notFoundResult.Value);
        Assert.Equal("Repository Not Found", problemDetails.Title);
        Assert.Contains("has not been indexed", problemDetails.Detail);
    }

    [Fact]
    public void GetIndexingStatus_WithIndexingRepo_ShouldReturnIndexingStatus()
    {
        // Arrange
        var request = new IndexRequest("https://github.com/test/repo.git", "test-repo");
        _controller.IndexRepository(request);

        // Act
        var result = _controller.GetIndexingStatus("test-repo");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<StatusResponse>(okResult.Value);
        Assert.Equal("indexing", response.Status);
        Assert.NotNull(response.Progress);
    }

    [Fact]
    public void DeleteRepository_WithEmptyRepoName_ShouldReturnBadRequest()
    {
        // Arrange & Act
        var result = _controller.DeleteRepository("");

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Invalid Request", problemDetails.Title);
        Assert.Contains("Repository name is required", problemDetails.Detail);
    }

    [Fact]
    public void DeleteRepository_WithWhitespaceRepoName_ShouldReturnBadRequest()
    {
        // Arrange & Act
        var result = _controller.DeleteRepository("   ");

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Invalid Request", problemDetails.Title);
    }

    [Fact]
    public void DeleteRepository_WithNonExistentRepo_ShouldReturnNotFound()
    {
        // Arrange & Act
        var result = _controller.DeleteRepository("non-existent-repo");

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(notFoundResult.Value);
        Assert.Equal("Repository Not Found", problemDetails.Title);
        Assert.Contains("has not been indexed", problemDetails.Detail);
    }

    [Fact]
    public void DeleteRepository_WithIndexingRepo_ShouldReturnConflict()
    {
        // Arrange
        var request = new IndexRequest("https://github.com/test/repo.git", "test-repo");
        _controller.IndexRepository(request);

        // Act
        var result = _controller.DeleteRepository("test-repo");

        // Assert
        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(conflictResult.Value);
        Assert.Equal("Indexing In Progress", problemDetails.Title);
        Assert.Contains("while indexing is in progress", problemDetails.Detail);
    }

    [Fact]
    public void GetAllRepositories_ShouldReturnOk()
    {
        // Arrange & Act
        var result = _controller.GetAllRepositories();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public void GetAllRepositories_WithIndexingRepo_ShouldIncludeInResults()
    {
        // Arrange
        var request = new IndexRequest("https://github.com/test/repo.git", "test-repo");
        _controller.IndexRepository(request);

        // Act
        var result = _controller.GetAllRepositories();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var repositories = Assert.IsAssignableFrom<System.Collections.IEnumerable>(okResult.Value);
        Assert.NotEmpty(repositories.Cast<object>());
    }

    [Fact]
    public void CancelIndexing_WithEmptyRepoName_ShouldReturnBadRequest()
    {
        // Arrange & Act
        var result = _controller.CancelIndexing("");

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Invalid Request", problemDetails.Title);
        Assert.Contains("Repository name is required", problemDetails.Detail);
    }

    [Fact]
    public void CancelIndexing_WithWhitespaceRepoName_ShouldReturnBadRequest()
    {
        // Arrange & Act
        var result = _controller.CancelIndexing("   ");

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal("Invalid Request", problemDetails.Title);
    }

    [Fact]
    public void CancelIndexing_WithNonIndexingRepo_ShouldReturnNotFound()
    {
        // Arrange & Act
        var result = _controller.CancelIndexing("non-existent-repo");

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(notFoundResult.Value);
        Assert.Equal("Repository Not Found", problemDetails.Title);
        Assert.Contains("not currently being indexed", problemDetails.Detail);
    }

    [Fact]
    public void CancelIndexing_WithIndexingRepo_ShouldReturnOk()
    {
        // Arrange
        var request = new IndexRequest("https://github.com/test/repo.git", "test-repo");
        _controller.IndexRepository(request);

        // Act
        var result = _controller.CancelIndexing("test-repo");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }
}

/// <summary>
/// Tests for the DTO record types used by the RepositoryController.
/// </summary>
public class RepositoryControllerDtoTests
{
    [Fact]
    public void IndexRequest_CanBeCreated()
    {
        // Arrange & Act
        var request = new IndexRequest("https://github.com/test/repo.git", "test-repo");

        // Assert
        Assert.Equal("https://github.com/test/repo.git", request.RepoUrl);
        Assert.Equal("test-repo", request.RepoName);
    }

    [Fact]
    public void IndexResponse_CanBeCreated()
    {
        // Arrange & Act
        var response = new IndexResponse("Indexing started", "test-repo");

        // Assert
        Assert.Equal("Indexing started", response.Message);
        Assert.Equal("test-repo", response.RepoName);
    }

    [Fact]
    public void StatusResponse_CanBeCreated()
    {
        // Arrange & Act
        var progress = new IndexingProgress { TotalFiles = 10, ProcessedFiles = 5 };
        var response = new StatusResponse("indexing", progress);

        // Assert
        Assert.Equal("indexing", response.Status);
        Assert.Equal(progress, response.Progress);
    }

    [Fact]
    public void StatusResponse_CanBeCreatedWithNullProgress()
    {
        // Arrange & Act
        var response = new StatusResponse("completed", null);

        // Assert
        Assert.Equal("completed", response.Status);
        Assert.Null(response.Progress);
    }

    [Fact]
    public void IndexRequest_Equality_WorksCorrectly()
    {
        // Arrange
        var request1 = new IndexRequest("https://github.com/test/repo.git", "test-repo");
        var request2 = new IndexRequest("https://github.com/test/repo.git", "test-repo");
        var request3 = new IndexRequest("https://github.com/other/repo.git", "other-repo");

        // Act & Assert
        Assert.Equal(request1, request2);
        Assert.NotEqual(request1, request3);
    }

    [Fact]
    public void IndexResponse_Equality_WorksCorrectly()
    {
        // Arrange
        var response1 = new IndexResponse("Indexing started", "test-repo");
        var response2 = new IndexResponse("Indexing started", "test-repo");
        var response3 = new IndexResponse("Indexing failed", "test-repo");

        // Act & Assert
        Assert.Equal(response1, response2);
        Assert.NotEqual(response1, response3);
    }

    [Fact]
    public void StatusResponse_Equality_WorksCorrectly()
    {
        // Arrange
        var progress = new IndexingProgress { TotalFiles = 10 };
        var response1 = new StatusResponse("indexing", progress);
        var response2 = new StatusResponse("indexing", progress);
        var response3 = new StatusResponse("completed", null);

        // Act & Assert
        Assert.Equal(response1, response2);
        Assert.NotEqual(response1, response3);
    }
}