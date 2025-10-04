using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using CodeSleuth.Infrastructure.VectorDatabase;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CodeSleuth.Tests.Unit.VectorDatabase;

public class QdrantServiceTests : IDisposable
{
    private readonly Mock<ILogger<QdrantService>> _mockLogger;
    private readonly QdrantService _qdrantService;
    private const string TestHost = "localhost";
    private const int TestPort = 6334;

    public QdrantServiceTests()
    {
        _mockLogger = new Mock<ILogger<QdrantService>>();
        _qdrantService = new QdrantService(_mockLogger.Object, TestHost, TestPort);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldInitializeCorrectly()
    {
        // Arrange & Act - Already done in test constructor

        // Assert
        Assert.NotNull(_qdrantService);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"QdrantService initialized with host: {TestHost}, port: {TestPort}")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new QdrantService(null!));
    }

    [Fact]
    public void Constructor_WithDefaultParameters_ShouldUseDefaultHostAndPort()
    {
        // Arrange & Act
        var mockLogger = new Mock<ILogger<QdrantService>>();
        var service = new QdrantService(mockLogger.Object);

        // Assert
        Assert.NotNull(service);
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("QdrantService initialized with host: localhost, port: 6334")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        
        service.Dispose();
    }

    [Fact]
    public async Task UpsertAsync_WithNullVector_ShouldThrowArgumentNullException()
    {
        // Arrange
        var id = Guid.NewGuid();
        var metadata = new Dictionary<string, object> { ["test"] = "value" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _qdrantService.UpsertAsync(id, null!, metadata));
    }

    [Fact]
    public async Task UpsertAsync_WithNullMetadata_ShouldThrowArgumentNullException()
    {
        // Arrange
        var id = Guid.NewGuid();
        var vector = new float[1536];

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _qdrantService.UpsertAsync(id, vector, null!));
    }

    [Fact]
    public async Task UpsertAsync_WithInvalidVectorSize_ShouldThrowArgumentException()
    {
        // Arrange
        var id = Guid.NewGuid();
        var vector = new float[100]; // Wrong size
        var metadata = new Dictionary<string, object> { ["test"] = "value" };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            _qdrantService.UpsertAsync(id, vector, metadata));
        
        Assert.Contains("Vector must have 1536 dimensions", exception.Message);
    }

    [Fact]
    public async Task SearchAsync_WithNullQueryVector_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _qdrantService.SearchAsync(null!));
    }

    [Fact]
    public async Task SearchAsync_WithInvalidVectorSize_ShouldThrowArgumentException()
    {
        // Arrange
        var queryVector = new float[100]; // Wrong size

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            _qdrantService.SearchAsync(queryVector));
        
        Assert.Contains("Query vector must have 1536 dimensions", exception.Message);
    }

    [Fact]
    public async Task SearchAsync_WithInvalidLimit_ShouldThrowArgumentException()
    {
        // Arrange
        var queryVector = new float[1536];

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            _qdrantService.SearchAsync(queryVector, 0));
        
        Assert.Contains("Limit must be greater than 0", exception.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task SearchAsync_WithNonPositiveLimit_ShouldThrowArgumentException(int limit)
    {
        // Arrange
        var queryVector = new float[1536];

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            _qdrantService.SearchAsync(queryVector, limit));
        
        Assert.Contains("Limit must be greater than 0", exception.Message);
    }

    [Fact]
    public void Dispose_ShouldLogDisposalMessage()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<QdrantService>>();
        var service = new QdrantService(mockLogger.Object);

        // Act
        service.Dispose();

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("QdrantService disposed")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task UpsertAsync_WithValidParameters_ShouldNotThrowForCorrectVectorSize()
    {
        // Arrange
        var id = Guid.NewGuid();
        var vector = new float[1536]; // Correct size
        var metadata = new Dictionary<string, object> 
        { 
            ["fileName"] = "test.cs",
            ["lineNumber"] = 42,
            ["content"] = "public class Test",
            ["isMethod"] = true,
            ["score"] = 0.95f
        };

        // Act & Assert - Should not throw ArgumentException for correct vector size
        // Note: This will likely fail with connection error in unit test environment,
        // but validates parameter validation logic
        var exception = await Record.ExceptionAsync(() => _qdrantService.UpsertAsync(id, vector, metadata));
        
        // We expect either success or a connection-related InvalidOperationException,
        // but NOT an ArgumentException about vector size
        if (exception != null)
        {
            Assert.IsNotType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception);
        }
    }

    [Fact]
    public async Task SearchAsync_WithValidParameters_ShouldNotThrowForCorrectVectorSize()
    {
        // Arrange
        var queryVector = new float[1536]; // Correct size
        const int limit = 5;

        // Act & Assert - Should not throw ArgumentException for correct vector size
        // Note: This will likely fail with connection error in unit test environment,
        // but validates parameter validation logic
        var exception = await Record.ExceptionAsync(() => _qdrantService.SearchAsync(queryVector, limit));
        
        // We expect either success or a connection-related InvalidOperationException,
        // but NOT an ArgumentException about vector size
        if (exception != null)
        {
            Assert.IsNotType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception);
        }
    }

    [Fact]
    public async Task InitializeCollectionAsync_ShouldNotThrowArgumentExceptions()
    {
        // Act & Assert - This will likely fail with connection error in unit test environment,
        // but we can verify it doesn't throw ArgumentExceptions
        var exception = await Record.ExceptionAsync(() => _qdrantService.InitializeCollectionAsync());
        
        // We expect either success or a connection-related InvalidOperationException
        if (exception != null)
        {
            Assert.IsNotType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception);
        }
    }

    [Fact]
    public async Task DeleteCollectionAsync_ShouldNotThrowArgumentExceptions()
    {
        // Act & Assert - This will likely fail with connection error in unit test environment,
        // but we can verify it doesn't throw ArgumentExceptions
        var exception = await Record.ExceptionAsync(() => _qdrantService.DeleteCollectionAsync());
        
        // We expect either success or a connection-related InvalidOperationException
        if (exception != null)
        {
            Assert.IsNotType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception);
        }
    }

    [Theory]
    [InlineData("test string")]
    [InlineData(42)]
    [InlineData(42L)]
    [InlineData(3.14f)]
    [InlineData(3.14159)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpsertAsync_WithVariousMetadataTypes_ShouldNotThrowTypeExceptions(object input)
    {
        // This tests the private ConvertToValue method indirectly through UpsertAsync
        // Arrange
        var id = Guid.NewGuid();
        var vector = new float[1536];
        var metadata = new Dictionary<string, object> { ["testValue"] = input };

        // Act & Assert - Should not throw for supported types
        var exception = await Record.ExceptionAsync(() => _qdrantService.UpsertAsync(id, vector, metadata));
        
        // We expect either success or a connection-related InvalidOperationException,
        // but NOT an exception related to type conversion
        if (exception != null)
        {
            Assert.IsNotType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception);
        }
    }

    public void Dispose()
    {
        _qdrantService?.Dispose();
    }
}