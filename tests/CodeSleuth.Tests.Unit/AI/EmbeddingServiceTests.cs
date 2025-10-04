using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using CodeSleuth.Infrastructure.AI;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CodeSleuth.Tests.Unit.AI;

public class EmbeddingServiceTests : IDisposable
{
    private readonly Mock<ILogger<EmbeddingService>> _mockLogger;
    private const string TestEndpoint = "https://test.openai.azure.com/";
    private const string TestApiKey = "test-api-key";
    private const string TestModel = "text-embedding-3-small";

    public EmbeddingServiceTests()
    {
        _mockLogger = new Mock<ILogger<EmbeddingService>>();
    }

    [Fact]
    public void Constructor_WithAzureEndpoint_ShouldDetectAzureCorrectly()
    {
        // Arrange & Act
        var service = new EmbeddingService(TestEndpoint, TestApiKey, TestModel, _mockLogger.Object);

        // Assert
        Assert.NotNull(service);
        var configInfo = service.GetConfigurationInfo();
        Assert.Contains("Azure OpenAI", configInfo);
        Assert.Contains(TestModel, configInfo);
        
        service.Dispose();
    }

    [Fact]
    public void Constructor_WithOpenAIDirectEndpoint_ShouldDetectOpenAICorrectly()
    {
        // Arrange
        const string openAiEndpoint = "https://api.openai.com/v1/";
        
        // Act
        var service = new EmbeddingService(openAiEndpoint, TestApiKey, TestModel, _mockLogger.Object);

        // Assert
        Assert.NotNull(service);
        var configInfo = service.GetConfigurationInfo();
        Assert.Contains("OpenAI Direct", configInfo);
        Assert.Contains(TestModel, configInfo);
        
        service.Dispose();
    }

    [Fact]
    public void Constructor_WithNullEndpoint_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EmbeddingService(null!, TestApiKey, TestModel, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithEmptyEndpoint_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EmbeddingService("", TestApiKey, TestModel, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullApiKey_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EmbeddingService(TestEndpoint, null!, TestModel, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithEmptyApiKey_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EmbeddingService(TestEndpoint, "", TestModel, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullModel_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EmbeddingService(TestEndpoint, TestApiKey, null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithEmptyModel_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EmbeddingService(TestEndpoint, TestApiKey, "", _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EmbeddingService(TestEndpoint, TestApiKey, TestModel, null!));
    }

    [Fact]
    public void Constructor_WithCustomRetrySettings_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var service = new EmbeddingService(TestEndpoint, TestApiKey, TestModel, _mockLogger.Object, 5, 2000);

        // Assert
        Assert.NotNull(service);
        var configInfo = service.GetConfigurationInfo();
        Assert.Contains("Max Retries: 5", configInfo);
        Assert.Contains("Base Delay: 2000ms", configInfo);
        
        service.Dispose();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithNullText_ShouldThrowArgumentNullException()
    {
        // Arrange
        var service = new EmbeddingService(TestEndpoint, TestApiKey, TestModel, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            service.GenerateEmbeddingAsync(null!));
        
        service.Dispose();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithEmptyText_ShouldThrowArgumentNullException()
    {
        // Arrange
        var service = new EmbeddingService(TestEndpoint, TestApiKey, TestModel, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            service.GenerateEmbeddingAsync(""));
        
        service.Dispose();
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_WithNullTexts_ShouldThrowArgumentNullException()
    {
        // Arrange
        var service = new EmbeddingService(TestEndpoint, TestApiKey, TestModel, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            service.GenerateEmbeddingsAsync(null!));
        
        service.Dispose();
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_WithEmptyList_ShouldThrowArgumentException()
    {
        // Arrange
        var service = new EmbeddingService(TestEndpoint, TestApiKey, TestModel, _mockLogger.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            service.GenerateEmbeddingsAsync(new List<string>()));
        
        Assert.Contains("Text list cannot be empty", exception.Message);
        service.Dispose();
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_WithNullTextInList_ShouldThrowArgumentException()
    {
        // Arrange
        var service = new EmbeddingService(TestEndpoint, TestApiKey, TestModel, _mockLogger.Object);
        var texts = new List<string> { "valid text", null!, "another valid text" };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            service.GenerateEmbeddingsAsync(texts));
        
        Assert.Contains("Text list cannot contain null or empty strings", exception.Message);
        service.Dispose();
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_WithEmptyTextInList_ShouldThrowArgumentException()
    {
        // Arrange
        var service = new EmbeddingService(TestEndpoint, TestApiKey, TestModel, _mockLogger.Object);
        var texts = new List<string> { "valid text", "", "another valid text" };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            service.GenerateEmbeddingsAsync(texts));
        
        Assert.Contains("Text list cannot contain null or empty strings", exception.Message);
        service.Dispose();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithValidText_ShouldNotThrowArgumentExceptions()
    {
        // Arrange
        var service = new EmbeddingService(TestEndpoint, TestApiKey, TestModel, _mockLogger.Object);

        // Act & Assert - Should not throw ArgumentException for valid input
        // Note: This will likely fail with connection error in unit test environment,
        // but validates parameter validation logic
        var exception = await Record.ExceptionAsync(() => 
            service.GenerateEmbeddingAsync("This is a test text for embedding generation"));
        
        // We expect either success or a connection-related EmbeddingGenerationException,
        // but NOT an ArgumentException about input validation
        if (exception != null)
        {
            Assert.IsNotType<ArgumentException>(exception);
            Assert.IsType<EmbeddingGenerationException>(exception);
        }
        
        service.Dispose();
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_WithValidTexts_ShouldNotThrowArgumentExceptions()
    {
        // Arrange
        var service = new EmbeddingService(TestEndpoint, TestApiKey, TestModel, _mockLogger.Object);
        var texts = new List<string> 
        { 
            "First test text", 
            "Second test text", 
            "Third test text with more content to test batch processing" 
        };

        // Act & Assert - Should not throw ArgumentException for valid input
        // Note: This will likely fail with connection error in unit test environment,
        // but validates parameter validation logic
        var exception = await Record.ExceptionAsync(() => 
            service.GenerateEmbeddingsAsync(texts));
        
        // We expect either success or a connection-related EmbeddingGenerationException,
        // but NOT an ArgumentException about input validation
        if (exception != null)
        {
            Assert.IsNotType<ArgumentException>(exception);
            Assert.IsType<EmbeddingGenerationException>(exception);
        }
        
        service.Dispose();
    }

    [Fact]
    public void GetConfigurationInfo_ShouldReturnCorrectInformation()
    {
        // Arrange
        var service = new EmbeddingService(TestEndpoint, TestApiKey, TestModel, _mockLogger.Object, 3, 1500);

        // Act
        var configInfo = service.GetConfigurationInfo();

        // Assert
        Assert.Contains($"Model: {TestModel}", configInfo);
        Assert.Contains("Endpoint Type: Azure OpenAI", configInfo);
        Assert.Contains("Max Retries: 3", configInfo);
        Assert.Contains("Base Delay: 1500ms", configInfo);
        
        service.Dispose();
    }

    [Fact]
    public void Dispose_ShouldLogDisposalMessage()
    {
        // Arrange
        var service = new EmbeddingService(TestEndpoint, TestApiKey, TestModel, _mockLogger.Object);

        // Act
        service.Dispose();

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("EmbeddingService disposed")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    public void Dispose()
    {
        // Clean up if needed
    }
}