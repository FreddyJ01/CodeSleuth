using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CodeSleuth.Infrastructure.Git;

/// <summary>
/// Custom exception for Git operations.
/// </summary>
public class GitOperationException : Exception
{
    public GitOperationException(string message) : base(message) { }
    public GitOperationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Service for cloning and managing Git repositories.
/// Provides methods for repository management and code file discovery.
/// </summary>
public class GitService : IDisposable
{
    private readonly ILogger<GitService> _logger;
    private readonly string _storagePath;
    
    /// <summary>
    /// Supported code file extensions for discovery.
    /// </summary>
    private static readonly HashSet<string> CodeFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".java", ".py", ".js", ".ts", ".go", ".cpp", ".c", ".h", ".hpp",
        ".php", ".rb", ".rs", ".kt", ".scala", ".swift", ".dart", ".vue", ".jsx", ".tsx"
    };

    /// <summary>
    /// Directories to exclude from code file discovery.
    /// </summary>
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", ".git", "packages", "target", "build", 
        "dist", ".next", ".nuxt", "vendor", "__pycache__", ".pytest_cache",
        "coverage", ".coverage", ".nyc_output", "bower_components"
    };

    /// <summary>
    /// Initializes a new instance of the GitService class.
    /// </summary>
    /// <param name="logger">The logger instance for logging operations.</param>
    /// <param name="storagePath">The base path where repositories will be stored. Defaults to "./repositories".</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public GitService(ILogger<GitService> logger, string? storagePath = "./repositories")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storagePath = Path.GetFullPath(storagePath ?? "./repositories");
        
        // Ensure storage directory exists
        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
            _logger.LogInformation("Created storage directory: {StoragePath}", _storagePath);
        }
        
        _logger.LogInformation("GitService initialized with storage path: {StoragePath}", _storagePath);
    }

    /// <summary>
    /// Clones a repository or pulls latest changes if it already exists locally.
    /// </summary>
    /// <param name="repoUrl">The URL of the repository to clone.</param>
    /// <param name="repoName">The local name for the repository directory.</param>
    /// <returns>The local path where the repository is stored.</returns>
    /// <exception cref="ArgumentNullException">Thrown when repoUrl or repoName is null or empty.</exception>
    /// <exception cref="GitOperationException">Thrown when Git operations fail.</exception>
    public async Task<string> CloneRepositoryAsync(string repoUrl, string repoName)
    {
        if (string.IsNullOrWhiteSpace(repoUrl))
            throw new ArgumentNullException(nameof(repoUrl));
        
        if (string.IsNullOrWhiteSpace(repoName))
            throw new ArgumentNullException(nameof(repoName));

        // Sanitize repo name for file system
        var sanitizedRepoName = SanitizeDirectoryName(repoName);
        var localPath = Path.Combine(_storagePath, sanitizedRepoName);

        try
        {
            if (Directory.Exists(localPath) && IsGitRepository(localPath))
            {
                _logger.LogInformation("Repository already exists at {LocalPath}, pulling latest changes", localPath);
                await PullLatestChangesAsync(localPath);
            }
            else
            {
                _logger.LogInformation("Cloning repository {RepoUrl} to {LocalPath}", repoUrl, localPath);
                await CloneRepositoryInternalAsync(repoUrl, localPath);
            }

            _logger.LogInformation("Repository operation completed successfully for {RepoName}", repoName);
            return localPath;
        }
        catch (Exception ex) when (!(ex is ArgumentNullException))
        {
            _logger.LogError(ex, "Failed to clone/update repository {RepoUrl}", repoUrl);
            throw new GitOperationException($"Failed to clone/update repository '{repoUrl}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Recursively finds all code files in the specified repository path.
    /// </summary>
    /// <param name="repoPath">The path to the repository directory.</param>
    /// <returns>A list of full file paths for all discovered code files.</returns>
    /// <exception cref="ArgumentNullException">Thrown when repoPath is null or empty.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the repository path doesn't exist.</exception>
    /// <exception cref="GitOperationException">Thrown when file discovery fails.</exception>
    public List<string> GetCodeFiles(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath))
            throw new ArgumentNullException(nameof(repoPath));

        if (!Directory.Exists(repoPath))
            throw new DirectoryNotFoundException($"Repository path does not exist: {repoPath}");

        try
        {
            _logger.LogDebug("Discovering code files in repository: {RepoPath}", repoPath);

            var codeFiles = new List<string>();
            DiscoverCodeFilesRecursive(repoPath, codeFiles);

            _logger.LogInformation("Discovered {FileCount} code files in {RepoPath}", codeFiles.Count, repoPath);
            return codeFiles;
        }
        catch (Exception ex) when (!(ex is ArgumentNullException or DirectoryNotFoundException))
        {
            _logger.LogError(ex, "Failed to discover code files in {RepoPath}", repoPath);
            throw new GitOperationException($"Failed to discover code files in '{repoPath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reads the content of the specified file.
    /// </summary>
    /// <param name="filePath">The path to the file to read.</param>
    /// <returns>The content of the file as a string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when filePath is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file doesn't exist.</exception>
    /// <exception cref="GitOperationException">Thrown when file reading fails.</exception>
    public string GetFileContent(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File does not exist: {filePath}");

        try
        {
            _logger.LogDebug("Reading file content: {FilePath}", filePath);
            var content = File.ReadAllText(filePath);
            _logger.LogDebug("Successfully read {CharCount} characters from {FilePath}", content.Length, filePath);
            return content;
        }
        catch (Exception ex) when (!(ex is ArgumentNullException or FileNotFoundException))
        {
            _logger.LogError(ex, "Failed to read file content: {FilePath}", filePath);
            throw new GitOperationException($"Failed to read file '{filePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets information about the current Git service configuration.
    /// </summary>
    /// <returns>Configuration information for debugging purposes.</returns>
    public string GetConfigurationInfo()
    {
        return $"GitService - Storage Path: {_storagePath}, " +
               $"Supported Extensions: {string.Join(", ", CodeFileExtensions)}, " +
               $"Excluded Directories: {string.Join(", ", ExcludedDirectories)}";
    }

    /// <summary>
    /// Clones a repository to the specified local path.
    /// </summary>
    /// <param name="repoUrl">The repository URL to clone.</param>
    /// <param name="localPath">The local path where the repository will be cloned.</param>
    private async Task CloneRepositoryInternalAsync(string repoUrl, string localPath)
    {
        await Task.Run(() =>
        {
            var cloneOptions = new CloneOptions
            {
                IsBare = false,
                Checkout = true,
                RecurseSubmodules = true
            };

            // Remove existing directory if it exists but is not a git repo
            if (Directory.Exists(localPath))
            {
                Directory.Delete(localPath, true);
            }

            Repository.Clone(repoUrl, localPath, cloneOptions);
        });
    }

    /// <summary>
    /// Pulls the latest changes from the remote repository.
    /// </summary>
    /// <param name="localPath">The local path of the repository.</param>
    private async Task PullLatestChangesAsync(string localPath)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(localPath);
            
            // Get the default branch (usually main or master)
            var defaultBranch = repo.Head;
            var remote = repo.Network.Remotes["origin"];
            
            if (remote == null)
            {
                _logger.LogWarning("No origin remote found for repository at {LocalPath}", localPath);
                return;
            }

            // Fetch latest changes
            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
            Commands.Fetch(repo, remote.Name, refSpecs, null, "Fetching latest changes");

            // Get the tracking branch
            var trackingBranch = defaultBranch.TrackedBranch;
            if (trackingBranch != null)
            {
                // Create signature for merge
                var signature = new Signature("GitService", "gitservice@codesleuth.local", DateTimeOffset.Now);
                
                // Merge changes
                var mergeResult = repo.Merge(trackingBranch, signature);
                _logger.LogDebug("Merge result: {MergeStatus}", mergeResult.Status);
            }
        });
    }

    /// <summary>
    /// Checks if the specified directory is a Git repository.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if the path is a Git repository, false otherwise.</returns>
    private static bool IsGitRepository(string path)
    {
        try
        {
            using var repo = new Repository(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Recursively discovers code files in the specified directory.
    /// </summary>
    /// <param name="directoryPath">The directory path to search.</param>
    /// <param name="codeFiles">The list to add discovered code files to.</param>
    private void DiscoverCodeFilesRecursive(string directoryPath, List<string> codeFiles)
    {
        try
        {
            var directoryInfo = new DirectoryInfo(directoryPath);
            
            // Skip excluded directories
            if (ExcludedDirectories.Contains(directoryInfo.Name))
            {
                _logger.LogDebug("Skipping excluded directory: {DirectoryPath}", directoryPath);
                return;
            }

            // Add code files from current directory
            var files = Directory.GetFiles(directoryPath);
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (CodeFileExtensions.Contains(extension))
                {
                    codeFiles.Add(file);
                    _logger.LogTrace("Found code file: {FilePath}", file);
                }
            }

            // Recursively search subdirectories
            var subdirectories = Directory.GetDirectories(directoryPath);
            foreach (var subdirectory in subdirectories)
            {
                DiscoverCodeFilesRecursive(subdirectory, codeFiles);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied to directory: {DirectoryPath}", directoryPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error accessing directory: {DirectoryPath}", directoryPath);
        }
    }

    /// <summary>
    /// Sanitizes a directory name by removing invalid characters.
    /// </summary>
    /// <param name="name">The name to sanitize.</param>
    /// <returns>A sanitized directory name.</returns>
    private static string SanitizeDirectoryName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        
        // Limit length to avoid file system issues
        if (sanitized.Length > 100)
        {
            sanitized = sanitized[..100];
        }
        
        return sanitized;
    }

    /// <summary>
    /// Disposes the GitService resources.
    /// </summary>
    public void Dispose()
    {
        // Currently no resources to dispose, but implementing IDisposable for future use
        GC.SuppressFinalize(this);
    }
}
