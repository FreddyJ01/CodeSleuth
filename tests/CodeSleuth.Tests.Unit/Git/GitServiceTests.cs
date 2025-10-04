using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using CodeSleuth.Infrastructure.Git;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace CodeSleuth.Tests.Unit.Git;

public class GitServiceTests : IDisposable
{
    private readonly Mock<ILogger<GitService>> _mockLogger;
    private readonly string _testStoragePath;
    private readonly GitService _gitService;

    public GitServiceTests()
    {
        _mockLogger = new Mock<ILogger<GitService>>();
        _testStoragePath = Path.Combine(Path.GetTempPath(), "GitServiceTests", Guid.NewGuid().ToString());
        _gitService = new GitService(_mockLogger.Object, _testStoragePath);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var service = new GitService(_mockLogger.Object, _testStoragePath);

        // Assert
        Assert.NotNull(service);
        Assert.True(Directory.Exists(_testStoragePath));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new GitService(null!, _testStoragePath));
    }

    [Fact]
    public void Constructor_WithDefaultStoragePath_ShouldUseDefaultPath()
    {
        // Arrange & Act
        using var service = new GitService(_mockLogger.Object);

        // Assert
        Assert.NotNull(service);
        var configInfo = service.GetConfigurationInfo();
        Assert.Contains("repositories", configInfo);
    }

    [Fact]
    public void Constructor_WithNullStoragePath_ShouldUseDefaultPath()
    {
        // Arrange & Act
        using var service = new GitService(_mockLogger.Object, null);

        // Assert
        Assert.NotNull(service);
        var configInfo = service.GetConfigurationInfo();
        Assert.Contains("repositories", configInfo);
    }

    [Fact]
    public async Task CloneRepositoryAsync_WithNullRepoUrl_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _gitService.CloneRepositoryAsync(null!, "test-repo"));
    }

    [Fact]
    public async Task CloneRepositoryAsync_WithEmptyRepoUrl_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _gitService.CloneRepositoryAsync("", "test-repo"));
    }

    [Fact]
    public async Task CloneRepositoryAsync_WithNullRepoName_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _gitService.CloneRepositoryAsync("https://github.com/test/repo.git", null!));
    }

    [Fact]
    public async Task CloneRepositoryAsync_WithEmptyRepoName_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _gitService.CloneRepositoryAsync("https://github.com/test/repo.git", ""));
    }

    [Fact]
    public async Task CloneRepositoryAsync_WithInvalidUrl_ShouldThrowGitOperationException()
    {
        // Arrange
        const string invalidUrl = "not-a-valid-git-url";
        const string repoName = "test-repo";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<GitOperationException>(() => 
            _gitService.CloneRepositoryAsync(invalidUrl, repoName));
        
        Assert.Contains("Failed to clone/update repository", exception.Message);
    }

    [Fact]
    public void GetCodeFiles_WithNullRepoPath_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => _gitService.GetCodeFiles(null!));
    }

    [Fact]
    public void GetCodeFiles_WithEmptyRepoPath_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => _gitService.GetCodeFiles(""));
    }

    [Fact]
    public void GetCodeFiles_WithNonExistentPath_ShouldThrowDirectoryNotFoundException()
    {
        // Arrange
        const string nonExistentPath = "/path/that/does/not/exist";

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => _gitService.GetCodeFiles(nonExistentPath));
    }

    [Fact]
    public void GetCodeFiles_WithValidPath_ShouldReturnCodeFiles()
    {
        // Arrange
        var testRepoPath = CreateTestRepository();

        // Act
        var codeFiles = _gitService.GetCodeFiles(testRepoPath);

        // Assert
        Assert.NotNull(codeFiles);
        Assert.Contains(codeFiles, f => f.EndsWith(".cs"));
        Assert.Contains(codeFiles, f => f.EndsWith(".js"));
        Assert.DoesNotContain(codeFiles, f => f.Contains("node_modules"));
        Assert.DoesNotContain(codeFiles, f => f.Contains("bin"));
        Assert.DoesNotContain(codeFiles, f => f.EndsWith(".txt"));
    }

    [Fact]
    public void GetCodeFiles_WithExcludedDirectories_ShouldSkipExcludedDirs()
    {
        // Arrange
        var testRepoPath = CreateTestRepositoryWithExcludedDirs();

        // Act
        var codeFiles = _gitService.GetCodeFiles(testRepoPath);

        // Assert
        Assert.NotNull(codeFiles);
        Assert.DoesNotContain(codeFiles, f => f.Contains("node_modules"));
        Assert.DoesNotContain(codeFiles, f => f.Contains("bin"));
        Assert.DoesNotContain(codeFiles, f => f.Contains("obj"));
        Assert.DoesNotContain(codeFiles, f => f.Contains("packages"));
    }

    [Fact]
    public void GetFileContent_WithNullFilePath_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => _gitService.GetFileContent(null!));
    }

    [Fact]
    public void GetFileContent_WithEmptyFilePath_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => _gitService.GetFileContent(""));
    }

    [Fact]
    public void GetFileContent_WithNonExistentFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        const string nonExistentFile = "/path/that/does/not/exist.cs";

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => _gitService.GetFileContent(nonExistentFile));
    }

    [Fact]
    public void GetFileContent_WithValidFile_ShouldReturnContent()
    {
        // Arrange
        var testFilePath = CreateTestFile("public class TestClass { }");

        // Act
        var content = _gitService.GetFileContent(testFilePath);

        // Assert
        Assert.NotNull(content);
        Assert.Equal("public class TestClass { }", content);
    }

    [Fact]
    public void GetConfigurationInfo_ShouldReturnCorrectInformation()
    {
        // Act
        var configInfo = _gitService.GetConfigurationInfo();

        // Assert
        Assert.Contains("GitService", configInfo);
        Assert.Contains(_testStoragePath, configInfo);
        Assert.Contains("Supported Extensions", configInfo);
        Assert.Contains("Excluded Directories", configInfo);
        Assert.Contains(".cs", configInfo);
        Assert.Contains("node_modules", configInfo);
    }

    [Theory]
    [InlineData(".cs")]
    [InlineData(".java")]
    [InlineData(".py")]
    [InlineData(".js")]
    [InlineData(".ts")]
    [InlineData(".go")]
    [InlineData(".cpp")]
    [InlineData(".php")]
    public void GetCodeFiles_WithSupportedExtensions_ShouldIncludeFiles(string extension)
    {
        // Arrange
        var testRepoPath = CreateTestRepositoryWithFile($"TestFile{extension}", "test content");

        // Act
        var codeFiles = _gitService.GetCodeFiles(testRepoPath);

        // Assert
        Assert.Contains(codeFiles, f => f.EndsWith(extension));
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    [InlineData(".json")]
    [InlineData(".xml")]
    [InlineData(".yml")]
    public void GetCodeFiles_WithUnsupportedExtensions_ShouldExcludeFiles(string extension)
    {
        // Arrange
        var testRepoPath = CreateTestRepositoryWithFile($"TestFile{extension}", "test content");

        // Act
        var codeFiles = _gitService.GetCodeFiles(testRepoPath);

        // Assert
        Assert.DoesNotContain(codeFiles, f => f.EndsWith(extension));
    }

    [Fact]
    public async Task CloneRepositoryAsync_WithSpecialCharactersInRepoName_ShouldSanitizeName()
    {
        // Arrange
        const string invalidUrl = "https://github.com/test/repo.git";
        const string repoNameWithSpecialChars = "repo/with:special*chars?";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<GitOperationException>(() => 
            _gitService.CloneRepositoryAsync(invalidUrl, repoNameWithSpecialChars));
        
        // Should not fail due to invalid characters but due to invalid URL
        Assert.Contains("Failed to clone/update repository", exception.Message);
    }

    [Fact]
    public void GetCodeFiles_WithNestedDirectories_ShouldFindFilesRecursively()
    {
        // Arrange
        var testRepoPath = CreateTestRepositoryWithNestedStructure();

        // Act
        var codeFiles = _gitService.GetCodeFiles(testRepoPath);

        // Assert
        Assert.NotNull(codeFiles);
        Assert.True(codeFiles.Count >= 3); // Should find files in nested directories
        Assert.Contains(codeFiles, f => f.Contains("level1") && f.EndsWith(".cs"));
        Assert.Contains(codeFiles, f => f.Contains("level2") && f.EndsWith(".js"));
    }

    private string CreateTestRepository()
    {
        var testRepoPath = Path.Combine(_testStoragePath, "test-repo");
        Directory.CreateDirectory(testRepoPath);

        // Create test files
        File.WriteAllText(Path.Combine(testRepoPath, "Program.cs"), "public class Program { }");
        File.WriteAllText(Path.Combine(testRepoPath, "script.js"), "console.log('hello');");
        File.WriteAllText(Path.Combine(testRepoPath, "README.txt"), "This is a readme");

        // Create excluded directory with file
        var nodeModulesPath = Path.Combine(testRepoPath, "node_modules");
        Directory.CreateDirectory(nodeModulesPath);
        File.WriteAllText(Path.Combine(nodeModulesPath, "package.js"), "excluded file");

        var binPath = Path.Combine(testRepoPath, "bin");
        Directory.CreateDirectory(binPath);
        File.WriteAllText(Path.Combine(binPath, "app.exe"), "binary file");

        return testRepoPath;
    }

    private string CreateTestRepositoryWithExcludedDirs()
    {
        var testRepoPath = Path.Combine(_testStoragePath, "test-repo-excluded");
        Directory.CreateDirectory(testRepoPath);

        // Create valid code file
        File.WriteAllText(Path.Combine(testRepoPath, "valid.cs"), "public class Valid { }");

        // Create excluded directories with code files
        var excludedDirs = new[] { "node_modules", "bin", "obj", "packages" };
        foreach (var dir in excludedDirs)
        {
            var dirPath = Path.Combine(testRepoPath, dir);
            Directory.CreateDirectory(dirPath);
            File.WriteAllText(Path.Combine(dirPath, "excluded.cs"), "excluded content");
        }

        return testRepoPath;
    }

    private string CreateTestRepositoryWithFile(string fileName, string content)
    {
        var testRepoPath = Path.Combine(_testStoragePath, "test-repo-single-file");
        Directory.CreateDirectory(testRepoPath);
        
        var filePath = Path.Combine(testRepoPath, fileName);
        File.WriteAllText(filePath, content);
        
        return testRepoPath;
    }

    private string CreateTestRepositoryWithNestedStructure()
    {
        var testRepoPath = Path.Combine(_testStoragePath, "test-repo-nested");
        Directory.CreateDirectory(testRepoPath);

        // Root level
        File.WriteAllText(Path.Combine(testRepoPath, "root.cs"), "root content");

        // Level 1
        var level1Path = Path.Combine(testRepoPath, "level1");
        Directory.CreateDirectory(level1Path);
        File.WriteAllText(Path.Combine(level1Path, "level1.cs"), "level1 content");

        // Level 2
        var level2Path = Path.Combine(level1Path, "level2");
        Directory.CreateDirectory(level2Path);
        File.WriteAllText(Path.Combine(level2Path, "level2.js"), "level2 content");

        return testRepoPath;
    }

    private string CreateTestFile(string content)
    {
        var testFilePath = Path.Combine(_testStoragePath, "test-file.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(testFilePath)!);
        File.WriteAllText(testFilePath, content);
        return testFilePath;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testStoragePath))
            {
                Directory.Delete(_testStoragePath, true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }
}