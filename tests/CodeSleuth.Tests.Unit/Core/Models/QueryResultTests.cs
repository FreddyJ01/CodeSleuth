using System;
using System.Collections.Generic;
using Xunit;
using CodeSleuth.Core.Models;

namespace CodeSleuth.Tests.Unit.Core.Models;

/// <summary>
/// Unit tests for QueryResult model class.
/// </summary>
public class QueryResultTests
{
    [Fact]
    public void QueryResult_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var queryResult = new QueryResult();

        // Assert
        Assert.Equal(string.Empty, queryResult.Answer);
        Assert.NotNull(queryResult.References);
        Assert.Empty(queryResult.References);
        Assert.Equal(TimeSpan.Zero, queryResult.Duration);
    }

    [Fact]
    public void QueryResult_CanSetAnswer()
    {
        // Arrange
        var queryResult = new QueryResult();
        var answer = "This is the answer to the query.";

        // Act
        queryResult.Answer = answer;

        // Assert
        Assert.Equal(answer, queryResult.Answer);
    }

    [Fact]
    public void QueryResult_CanSetReferences()
    {
        // Arrange
        var queryResult = new QueryResult();
        var references = new List<CodeReference>
        {
            new CodeReference { FilePath = "file1.cs", StartLine = 1, EndLine = 10, SimilarityScore = 0.9f },
            new CodeReference { FilePath = "file2.cs", StartLine = 15, EndLine = 25, SimilarityScore = 0.8f }
        };

        // Act
        queryResult.References = references;

        // Assert
        Assert.Equal(references, queryResult.References);
        Assert.Equal(2, queryResult.References.Count);
    }

    [Fact]
    public void QueryResult_CanSetDuration()
    {
        // Arrange
        var queryResult = new QueryResult();
        var duration = TimeSpan.FromMilliseconds(1500);

        // Act
        queryResult.Duration = duration;

        // Assert
        Assert.Equal(duration, queryResult.Duration);
    }

    [Fact]
    public void QueryResult_CanAddReferencesToList()
    {
        // Arrange
        var queryResult = new QueryResult();
        var reference1 = new CodeReference { FilePath = "file1.cs", StartLine = 1, EndLine = 10, SimilarityScore = 0.9f };
        var reference2 = new CodeReference { FilePath = "file2.cs", StartLine = 15, EndLine = 25, SimilarityScore = 0.8f };

        // Act
        queryResult.References.Add(reference1);
        queryResult.References.Add(reference2);

        // Assert
        Assert.Equal(2, queryResult.References.Count);
        Assert.Contains(reference1, queryResult.References);
        Assert.Contains(reference2, queryResult.References);
    }

    [Fact]
    public void QueryResult_WithCompleteData_ShouldMaintainAllProperties()
    {
        // Arrange & Act
        var queryResult = new QueryResult
        {
            Answer = "The method calculates the sum of two integers.",
            References = new List<CodeReference>
            {
                new CodeReference { FilePath = "Calculator.cs", StartLine = 10, EndLine = 15, SimilarityScore = 0.95f }
            },
            Duration = TimeSpan.FromSeconds(2.5)
        };

        // Assert
        Assert.Equal("The method calculates the sum of two integers.", queryResult.Answer);
        Assert.Single(queryResult.References);
        Assert.Equal("Calculator.cs", queryResult.References[0].FilePath);
        Assert.Equal(TimeSpan.FromSeconds(2.5), queryResult.Duration);
    }
}

/// <summary>
/// Unit tests for CodeReference model class.
/// </summary>
public class CodeReferenceTests
{
    [Fact]
    public void CodeReference_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var codeReference = new CodeReference();

        // Assert
        Assert.Equal(string.Empty, codeReference.FilePath);
        Assert.Equal(0, codeReference.StartLine);
        Assert.Equal(0, codeReference.EndLine);
        Assert.Equal(0.0f, codeReference.SimilarityScore);
    }

    [Fact]
    public void CodeReference_CanSetFilePath()
    {
        // Arrange
        var codeReference = new CodeReference();
        var filePath = "/path/to/file.cs";

        // Act
        codeReference.FilePath = filePath;

        // Assert
        Assert.Equal(filePath, codeReference.FilePath);
    }

    [Fact]
    public void CodeReference_CanSetStartLine()
    {
        // Arrange
        var codeReference = new CodeReference();
        var startLine = 42;

        // Act
        codeReference.StartLine = startLine;

        // Assert
        Assert.Equal(startLine, codeReference.StartLine);
    }

    [Fact]
    public void CodeReference_CanSetEndLine()
    {
        // Arrange
        var codeReference = new CodeReference();
        var endLine = 100;

        // Act
        codeReference.EndLine = endLine;

        // Assert
        Assert.Equal(endLine, codeReference.EndLine);
    }

    [Fact]
    public void CodeReference_CanSetSimilarityScore()
    {
        // Arrange
        var codeReference = new CodeReference();
        var similarityScore = 0.85f;

        // Act
        codeReference.SimilarityScore = similarityScore;

        // Assert
        Assert.Equal(similarityScore, codeReference.SimilarityScore);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    public void CodeReference_SimilarityScore_ShouldAcceptValidRange(float score)
    {
        // Arrange
        var codeReference = new CodeReference();

        // Act
        codeReference.SimilarityScore = score;

        // Assert
        Assert.Equal(score, codeReference.SimilarityScore);
    }

    [Fact]
    public void CodeReference_WithCompleteData_ShouldMaintainAllProperties()
    {
        // Arrange & Act
        var codeReference = new CodeReference
        {
            FilePath = "src/Services/UserService.cs",
            StartLine = 25,
            EndLine = 40,
            SimilarityScore = 0.92f
        };

        // Assert
        Assert.Equal("src/Services/UserService.cs", codeReference.FilePath);
        Assert.Equal(25, codeReference.StartLine);
        Assert.Equal(40, codeReference.EndLine);
        Assert.Equal(0.92f, codeReference.SimilarityScore);
    }

    [Fact]
    public void CodeReference_CanRepresentSingleLine()
    {
        // Arrange & Act
        var codeReference = new CodeReference
        {
            FilePath = "Program.cs",
            StartLine = 5,
            EndLine = 5,
            SimilarityScore = 0.7f
        };

        // Assert
        Assert.Equal(5, codeReference.StartLine);
        Assert.Equal(5, codeReference.EndLine);
        Assert.True(codeReference.StartLine == codeReference.EndLine);
    }

    [Fact]
    public void CodeReference_CanRepresentMultipleLines()
    {
        // Arrange & Act
        var codeReference = new CodeReference
        {
            FilePath = "LargeFile.cs",
            StartLine = 100,
            EndLine = 150,
            SimilarityScore = 0.88f
        };

        // Assert
        Assert.Equal(100, codeReference.StartLine);
        Assert.Equal(150, codeReference.EndLine);
        Assert.True(codeReference.EndLine > codeReference.StartLine);
        Assert.Equal(51, codeReference.EndLine - codeReference.StartLine + 1); // Line count
    }

    [Fact]
    public void CodeReference_SimilarityScore_CanBePrecise()
    {
        // Arrange & Act
        var codeReference = new CodeReference
        {
            SimilarityScore = 0.123456f
        };

        // Assert
        Assert.Equal(0.123456f, codeReference.SimilarityScore, precision: 6);
    }
}

/// <summary>
/// Integration tests for QueryResult and CodeReference working together.
/// </summary>
public class QueryResultIntegrationTests
{
    [Fact]
    public void QueryResult_WithMultipleReferences_ShouldOrderByScore()
    {
        // Arrange
        var queryResult = new QueryResult
        {
            Answer = "Multiple code segments implement this functionality.",
            References = new List<CodeReference>
            {
                new CodeReference { FilePath = "file1.cs", StartLine = 1, EndLine = 10, SimilarityScore = 0.7f },
                new CodeReference { FilePath = "file2.cs", StartLine = 20, EndLine = 30, SimilarityScore = 0.9f },
                new CodeReference { FilePath = "file3.cs", StartLine = 50, EndLine = 60, SimilarityScore = 0.8f }
            }
        };

        // Act
        var orderedReferences = queryResult.References.OrderByDescending(r => r.SimilarityScore).ToList();

        // Assert
        Assert.Equal(3, orderedReferences.Count);
        Assert.Equal(0.9f, orderedReferences[0].SimilarityScore);
        Assert.Equal(0.8f, orderedReferences[1].SimilarityScore);
        Assert.Equal(0.7f, orderedReferences[2].SimilarityScore);
        Assert.Equal("file2.cs", orderedReferences[0].FilePath);
    }

    [Fact]
    public void QueryResult_CanCalculateAverageScore()
    {
        // Arrange
        var queryResult = new QueryResult
        {
            References = new List<CodeReference>
            {
                new CodeReference { SimilarityScore = 0.8f },
                new CodeReference { SimilarityScore = 0.9f },
                new CodeReference { SimilarityScore = 0.7f }
            }
        };

        // Act
        var averageScore = queryResult.References.Average(r => r.SimilarityScore);

        // Assert
        Assert.Equal(0.8f, averageScore, precision: 1);
    }

    [Fact]
    public void QueryResult_CanFilterReferencesByScore()
    {
        // Arrange
        var queryResult = new QueryResult
        {
            References = new List<CodeReference>
            {
                new CodeReference { FilePath = "high1.cs", SimilarityScore = 0.9f },
                new CodeReference { FilePath = "low1.cs", SimilarityScore = 0.3f },
                new CodeReference { FilePath = "high2.cs", SimilarityScore = 0.8f },
                new CodeReference { FilePath = "low2.cs", SimilarityScore = 0.4f }
            }
        };

        // Act
        var highQualityReferences = queryResult.References.Where(r => r.SimilarityScore >= 0.7f).ToList();

        // Assert
        Assert.Equal(2, highQualityReferences.Count);
        Assert.All(highQualityReferences, r => Assert.True(r.SimilarityScore >= 0.7f));
        Assert.Contains("high1.cs", highQualityReferences.Select(r => r.FilePath));
        Assert.Contains("high2.cs", highQualityReferences.Select(r => r.FilePath));
    }

    [Fact]
    public void QueryResult_CanCalculateTotalLinesReferenced()
    {
        // Arrange
        var queryResult = new QueryResult
        {
            References = new List<CodeReference>
            {
                new CodeReference { StartLine = 1, EndLine = 10 }, // 10 lines
                new CodeReference { StartLine = 20, EndLine = 25 }, // 6 lines
                new CodeReference { StartLine = 50, EndLine = 50 } // 1 line
            }
        };

        // Act
        var totalLines = queryResult.References.Sum(r => r.EndLine - r.StartLine + 1);

        // Assert
        Assert.Equal(17, totalLines);
    }
}