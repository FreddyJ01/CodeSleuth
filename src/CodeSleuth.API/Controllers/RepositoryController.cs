using Microsoft.AspNetCore.Mvc;
using CodeSleuth.Core.Services;
using System.Collections.Concurrent;

namespace CodeSleuth.API.Controllers;

/// <summary>
/// Request model for repository indexing.
/// </summary>
/// <param name="RepoUrl">The URL of the repository to index.</param>
/// <param name="RepoName">The name identifier for the repository.</param>
public record IndexRequest(string RepoUrl, string RepoName);

/// <summary>
/// Response model for repository indexing requests.
/// </summary>
/// <param name="Message">Success or status message.</param>
/// <param name="RepoName">The name identifier for the repository.</param>
public record IndexResponse(string Message, string RepoName);

/// <summary>
/// Response model for repository indexing status.
/// </summary>
/// <param name="Status">Current status of the indexing process.</param>
/// <param name="Progress">Current progress information if indexing is in progress.</param>
public record StatusResponse(string Status, IndexingProgress? Progress);

/// <summary>
/// Controller for managing repository indexing operations.
/// Provides endpoints for starting indexing, checking status, and deleting indexed data.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class RepositoryController : ControllerBase
{
    private readonly IndexingService _indexingService;
    private readonly ILogger<RepositoryController> _logger;
    
    // In-memory storage for indexing status (MVP implementation)
    private static readonly ConcurrentDictionary<string, IndexingProgress> _indexingStatus = new();
    private static readonly ConcurrentDictionary<string, string> _completedRepositories = new();
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();

    /// <summary>
    /// Initializes a new instance of the RepositoryController.
    /// </summary>
    /// <param name="indexingService">The service responsible for repository indexing.</param>
    /// <param name="logger">The logger instance for this controller.</param>
    public RepositoryController(IndexingService indexingService, ILogger<RepositoryController> logger)
    {
        _indexingService = indexingService ?? throw new ArgumentNullException(nameof(indexingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts the indexing process for a repository.
    /// </summary>
    /// <param name="request">The repository indexing request containing URL and name.</param>
    /// <returns>A response indicating that indexing has started.</returns>
    /// <response code="202">Indexing started successfully.</response>
    /// <response code="400">Invalid request data.</response>
    /// <response code="409">Repository is already being indexed.</response>
    /// <response code="500">Internal server error occurred.</response>
    [HttpPost("index")]
    [ProducesResponseType(typeof(IndexResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public IActionResult IndexRepository([FromBody] IndexRequest request)
    {
        try
        {
            _logger.LogInformation("Received indexing request for repository: {RepoName} from URL: {RepoUrl}", 
                request.RepoName, request.RepoUrl);

            // Validate request
            if (string.IsNullOrWhiteSpace(request.RepoUrl) || string.IsNullOrWhiteSpace(request.RepoName))
            {
                _logger.LogWarning("Invalid indexing request: RepoUrl or RepoName is empty");
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Request",
                    Detail = "Repository URL and name are required.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // Check if repository is already being indexed
            if (_indexingStatus.ContainsKey(request.RepoName))
            {
                _logger.LogWarning("Repository {RepoName} is already being indexed", request.RepoName);
                return Conflict(new ProblemDetails
                {
                    Title = "Indexing In Progress",
                    Detail = $"Repository '{request.RepoName}' is already being indexed.",
                    Status = StatusCodes.Status409Conflict
                });
            }

            // Initialize progress tracking
            var progress = new IndexingProgress();
            _indexingStatus[request.RepoName] = progress;
            _completedRepositories.TryRemove(request.RepoName, out _);

            // Create cancellation token for this indexing operation
            var cts = new CancellationTokenSource();
            _cancellationTokens[request.RepoName] = cts;

            // Start background indexing task
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("Starting background indexing for repository: {RepoName}", request.RepoName);

                    var progressReporter = new Progress<IndexingProgress>(p =>
                    {
                        // Update the stored progress
                        _indexingStatus[request.RepoName] = new IndexingProgress
                        {
                            TotalFiles = p.TotalFiles,
                            ProcessedFiles = p.ProcessedFiles,
                            TotalChunks = p.TotalChunks,
                            CurrentFile = p.CurrentFile,
                            Errors = new List<string>(p.Errors)
                        };
                    });

                    var summary = await _indexingService.IndexRepositoryAsync(
                        request.RepoUrl, 
                        request.RepoName, 
                        progressReporter, 
                        cts.Token);

                    // Mark as completed
                    _indexingStatus.TryRemove(request.RepoName, out _);
                    _completedRepositories[request.RepoName] = summary.Errors.Any() ? "failed" : "completed";
                    _cancellationTokens.TryRemove(request.RepoName, out _);

                    _logger.LogInformation("Completed indexing for repository: {RepoName}. Files processed: {FilesProcessed}, Chunks indexed: {ChunksIndexed}, Duration: {Duration}", 
                        request.RepoName, summary.FilesProcessed, summary.ChunksIndexed, summary.Duration);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Indexing was cancelled for repository: {RepoName}", request.RepoName);
                    _indexingStatus.TryRemove(request.RepoName, out _);
                    _completedRepositories[request.RepoName] = "cancelled";
                    _cancellationTokens.TryRemove(request.RepoName, out _);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while indexing repository: {RepoName}", request.RepoName);
                    
                    // Update progress with error information
                    if (_indexingStatus.TryGetValue(request.RepoName, out var currentProgress))
                    {
                        currentProgress.Errors.Add($"Indexing failed: {ex.Message}");
                    }
                    
                    _indexingStatus.TryRemove(request.RepoName, out _);
                    _completedRepositories[request.RepoName] = "failed";
                    _cancellationTokens.TryRemove(request.RepoName, out _);
                }
            }, cts.Token);

            return Accepted(new IndexResponse("Indexing started", request.RepoName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error occurred while starting indexing for repository: {RepoName}", request.RepoName);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while starting the indexing process.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Gets the current indexing status for a repository.
    /// </summary>
    /// <param name="repoName">The name of the repository to check status for.</param>
    /// <returns>The current status and progress information.</returns>
    /// <response code="200">Status retrieved successfully.</response>
    /// <response code="404">Repository not found or never indexed.</response>
    /// <response code="500">Internal server error occurred.</response>
    [HttpGet("{repoName}/status")]
    [ProducesResponseType(typeof(StatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public IActionResult GetIndexingStatus(string repoName)
    {
        try
        {
            _logger.LogInformation("Checking indexing status for repository: {RepoName}", repoName);

            if (string.IsNullOrWhiteSpace(repoName))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Request",
                    Detail = "Repository name is required.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // Check if currently indexing
            if (_indexingStatus.TryGetValue(repoName, out var progress))
            {
                return Ok(new StatusResponse("indexing", progress));
            }

            // Check if completed
            if (_completedRepositories.TryGetValue(repoName, out var status))
            {
                return Ok(new StatusResponse(status, null));
            }

            // Repository not found
            _logger.LogWarning("Repository {RepoName} not found in indexing records", repoName);
            return NotFound(new ProblemDetails
            {
                Title = "Repository Not Found",
                Detail = $"Repository '{repoName}' has not been indexed or does not exist.",
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while checking status for repository: {RepoName}", repoName);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while checking the repository status.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Deletes the indexed data for a repository.
    /// </summary>
    /// <param name="repoName">The name of the repository to delete indexed data for.</param>
    /// <returns>A response indicating the result of the deletion operation.</returns>
    /// <response code="200">Repository data deleted successfully.</response>
    /// <response code="400">Invalid request data.</response>
    /// <response code="404">Repository not found.</response>
    /// <response code="409">Cannot delete repository that is currently being indexed.</response>
    /// <response code="500">Internal server error occurred.</response>
    [HttpDelete("{repoName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public IActionResult DeleteRepository(string repoName)
    {
        try
        {
            _logger.LogInformation("Received request to delete repository: {RepoName}", repoName);

            if (string.IsNullOrWhiteSpace(repoName))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Request",
                    Detail = "Repository name is required.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // Check if repository is currently being indexed
            if (_indexingStatus.ContainsKey(repoName))
            {
                _logger.LogWarning("Cannot delete repository {RepoName} while indexing is in progress", repoName);
                return Conflict(new ProblemDetails
                {
                    Title = "Indexing In Progress",
                    Detail = $"Cannot delete repository '{repoName}' while indexing is in progress. Please wait for indexing to complete or cancel it first.",
                    Status = StatusCodes.Status409Conflict
                });
            }

            // Check if repository exists in our records
            bool repositoryExists = _completedRepositories.ContainsKey(repoName);

            if (!repositoryExists)
            {
                _logger.LogWarning("Repository {RepoName} not found for deletion", repoName);
                return NotFound(new ProblemDetails
                {
                    Title = "Repository Not Found",
                    Detail = $"Repository '{repoName}' has not been indexed or does not exist.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            // TODO: In a real implementation, you would delete from the vector database here
            // For now, we'll just remove from our in-memory tracking
            _completedRepositories.TryRemove(repoName, out _);
            _indexingStatus.TryRemove(repoName, out _);

            // Cancel any associated cancellation token
            if (_cancellationTokens.TryRemove(repoName, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }

            _logger.LogInformation("Successfully deleted repository: {RepoName}", repoName);
            return Ok(new { message = $"Repository '{repoName}' deleted successfully.", repoName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while deleting repository: {RepoName}", repoName);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while deleting the repository.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Gets a list of all repositories and their current status.
    /// </summary>
    /// <returns>A list of all repositories with their status information.</returns>
    /// <response code="200">Repository list retrieved successfully.</response>
    /// <response code="500">Internal server error occurred.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public IActionResult GetAllRepositories()
    {
        try
        {
            _logger.LogInformation("Retrieving all repository statuses");

            var repositories = new List<object>();

            // Add currently indexing repositories
            foreach (var kvp in _indexingStatus)
            {
                repositories.Add(new
                {
                    repoName = kvp.Key,
                    status = "indexing",
                    progress = kvp.Value
                });
            }

            // Add completed repositories
            foreach (var kvp in _completedRepositories)
            {
                repositories.Add(new
                {
                    repoName = kvp.Key,
                    status = kvp.Value,
                    progress = (IndexingProgress?)null
                });
            }

            return Ok(repositories);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while retrieving repository list");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while retrieving the repository list.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Cancels the indexing process for a repository.
    /// </summary>
    /// <param name="repoName">The name of the repository to cancel indexing for.</param>
    /// <returns>A response indicating the result of the cancellation operation.</returns>
    /// <response code="200">Indexing cancelled successfully.</response>
    /// <response code="400">Invalid request data.</response>
    /// <response code="404">Repository not found or not currently being indexed.</response>
    /// <response code="500">Internal server error occurred.</response>
    [HttpPost("{repoName}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public IActionResult CancelIndexing(string repoName)
    {
        try
        {
            _logger.LogInformation("Received request to cancel indexing for repository: {RepoName}", repoName);

            if (string.IsNullOrWhiteSpace(repoName))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Request",
                    Detail = "Repository name is required.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // Check if repository is currently being indexed
            if (!_indexingStatus.ContainsKey(repoName))
            {
                _logger.LogWarning("Repository {RepoName} is not currently being indexed", repoName);
                return NotFound(new ProblemDetails
                {
                    Title = "Repository Not Found",
                    Detail = $"Repository '{repoName}' is not currently being indexed.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            // Cancel the indexing operation
            if (_cancellationTokens.TryGetValue(repoName, out var cts))
            {
                cts.Cancel();
                _logger.LogInformation("Cancellation requested for repository: {RepoName}", repoName);
            }

            return Ok(new { message = $"Indexing cancellation requested for repository '{repoName}'.", repoName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while cancelling indexing for repository: {RepoName}", repoName);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while cancelling the indexing process.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }
}
