using System.Diagnostics;
using Microsoft.Extensions.Logging;
using CodeSleuth.Core.Models;
using CodeSleuth.Infrastructure.Git;
using CodeSleuth.Infrastructure.AI;
using CodeSleuth.Infrastructure.VectorDatabase;

namespace CodeSleuth.Core.Services;

/// <summary>
/// Represents the progress of repository indexing.
/// </summary>
public class IndexingProgress
{
    /// <summary>
    /// Gets or sets the total number of files to process.
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Gets or sets the number of files processed so far.
    /// </summary>
    public int ProcessedFiles { get; set; }

    /// <summary>
    /// Gets or sets the total number of chunks indexed so far.
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// Gets or sets the path of the currently processing file.
    /// </summary>
    public string CurrentFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of errors encountered during indexing.
    /// </summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Represents the summary of repository indexing operation.
/// </summary>
public class IndexingSummary
{
    /// <summary>
    /// Gets or sets the number of files processed.
    /// </summary>
    public int FilesProcessed { get; set; }

    /// <summary>
    /// Gets or sets the number of chunks indexed.
    /// </summary>
    public int ChunksIndexed { get; set; }

    /// <summary>
    /// Gets or sets the duration of the indexing operation.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets the list of errors encountered during indexing.
    /// </summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Service for orchestrating the repository indexing process.
/// </summary>
public class IndexingService
{
    private readonly GitService _gitService;
    private readonly CodeParsingService _codeParsingService;
    private readonly EmbeddingService _embeddingService;
    private readonly QdrantService _qdrantService;
    private readonly ILogger<IndexingService> _logger;

    private const int ProgressReportInterval = 10;
    private const int EmbeddingBatchSize = 50;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexingService"/> class.
    /// </summary>
    /// <param name="gitService">The Git service for repository operations.</param>
    /// <param name="codeParsingService">The code parsing service for extracting code chunks.</param>
    /// <param name="embeddingService">The embedding service for generating text embeddings.</param>
    /// <param name="qdrantService">The Qdrant service for vector database operations.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required service is null.</exception>
    public IndexingService(
        GitService gitService,
        CodeParsingService codeParsingService,
        EmbeddingService embeddingService,
        QdrantService qdrantService,
        ILogger<IndexingService> logger)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _codeParsingService = codeParsingService ?? throw new ArgumentNullException(nameof(codeParsingService));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _qdrantService = qdrantService ?? throw new ArgumentNullException(nameof(qdrantService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Indexes a repository by cloning it, parsing code files, generating embeddings, and storing in vector database.
    /// </summary>
    /// <param name="repoUrl">The URL of the repository to index.</param>
    /// <param name="repoName">The name of the repository.</param>
    /// <param name="progress">Optional progress reporter for tracking indexing progress.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the indexing operation with summary information.</returns>
    /// <exception cref="ArgumentException">Thrown when repository URL or name is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when indexing fails due to service issues.</exception>
    public async Task<IndexingSummary> IndexRepositoryAsync(
        string repoUrl,
        string repoName,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repoUrl))
            throw new ArgumentException("Repository URL cannot be null or empty.", nameof(repoUrl));
        if (string.IsNullOrWhiteSpace(repoName))
            throw new ArgumentException("Repository name cannot be null or empty.", nameof(repoName));

        _logger.LogInformation("Starting indexing for repository: {RepoName} from {RepoUrl}", repoName, repoUrl);
        
        var stopwatch = Stopwatch.StartNew();
        var indexingProgress = new IndexingProgress();
        var summary = new IndexingSummary();

        try
        {
            // Step 1: Clone repository
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Cloning repository: {RepoUrl}", repoUrl);
            
            var localPath = await _gitService.CloneRepositoryAsync(repoUrl, repoName);
            _logger.LogInformation("Repository cloned to: {LocalPath}", localPath);

            // Step 2: Get all code files
            cancellationToken.ThrowIfCancellationRequested();
            var codeFiles = _gitService.GetCodeFiles(localPath);
            _logger.LogInformation("Found {FileCount} code files to process", codeFiles.Count);

            indexingProgress.TotalFiles = codeFiles.Count;
            progress?.Report(indexingProgress);

            // Step 3: Process files and chunks in batches
            var allChunks = new List<(CodeChunk chunk, string searchableText)>();
            
            for (int i = 0; i < codeFiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var file = codeFiles[i];
                indexingProgress.CurrentFile = file;
                indexingProgress.ProcessedFiles = i + 1;

                try
                {
                    _logger.LogDebug("Processing file: {FilePath}", file);
                    
                    // Parse file into chunks
                    var chunks = _codeParsingService.ParseCSharpFile(file);
                    
                    foreach (var chunk in chunks)
                    {
                        // Create searchable text
                        var searchableText = CreateSearchableText(chunk);
                        allChunks.Add((chunk, searchableText));
                        indexingProgress.TotalChunks++;
                    }

                    summary.FilesProcessed++;
                    _logger.LogDebug("Processed {ChunkCount} chunks from file: {FilePath}", chunks.Count, file);
                }
                catch (Exception ex)
                {
                    var errorMessage = $"Failed to process file {file}: {ex.Message}";
                    _logger.LogError(ex, "Error processing file: {FilePath}", file);
                    indexingProgress.Errors.Add(errorMessage);
                    summary.Errors.Add(errorMessage);
                }

                // Report progress every 10 files
                if ((i + 1) % ProgressReportInterval == 0 || i == codeFiles.Count - 1)
                {
                    progress?.Report(indexingProgress);
                    _logger.LogInformation("Progress: {ProcessedFiles}/{TotalFiles} files, {TotalChunks} chunks", 
                        indexingProgress.ProcessedFiles, indexingProgress.TotalFiles, indexingProgress.TotalChunks);
                }
            }

            // Step 4: Generate embeddings and store in batches
            if (allChunks.Count > 0)
            {
                _logger.LogInformation("Generating embeddings and storing {ChunkCount} chunks", allChunks.Count);
                await ProcessChunksInBatchesAsync(allChunks, repoName, cancellationToken);
                summary.ChunksIndexed = allChunks.Count;
            }

            stopwatch.Stop();
            summary.Duration = stopwatch.Elapsed;
            summary.Errors = indexingProgress.Errors;

            _logger.LogInformation(
                "Repository indexing completed. Files: {FilesProcessed}, Chunks: {ChunksIndexed}, Duration: {Duration}, Errors: {ErrorCount}",
                summary.FilesProcessed, summary.ChunksIndexed, summary.Duration, summary.Errors.Count);

            return summary;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Repository indexing was cancelled for: {RepoName}", repoName);
            stopwatch.Stop();
            summary.Duration = stopwatch.Elapsed;
            summary.Errors = indexingProgress.Errors;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index repository: {RepoName}", repoName);
            stopwatch.Stop();
            summary.Duration = stopwatch.Elapsed;
            summary.Errors = indexingProgress.Errors;
            summary.Errors.Add($"Critical indexing failure: {ex.Message}");
            throw new InvalidOperationException($"Failed to index repository {repoName}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates searchable text from a code chunk.
    /// </summary>
    /// <param name="chunk">The code chunk to create searchable text for.</param>
    /// <returns>The searchable text combining chunk name, namespace, and content.</returns>
    private static string CreateSearchableText(CodeChunk chunk)
    {
        var parts = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(chunk.Name))
            parts.Add(chunk.Name);
        
        if (!string.IsNullOrWhiteSpace(chunk.Namespace))
            parts.Add(chunk.Namespace);
        
        if (!string.IsNullOrWhiteSpace(chunk.Content))
            parts.Add(chunk.Content);

        return string.Join("\n", parts);
    }

    /// <summary>
    /// Processes chunks in batches for embedding generation and storage.
    /// </summary>
    /// <param name="chunks">The chunks to process with their searchable text.</param>
    /// <param name="repoName">The repository name for metadata.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    private async Task ProcessChunksInBatchesAsync(
        List<(CodeChunk chunk, string searchableText)> chunks,
        string repoName,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < chunks.Count; i += EmbeddingBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var batch = chunks.Skip(i).Take(EmbeddingBatchSize).ToList();
            var searchableTexts = batch.Select(x => x.searchableText).ToList();
            
            try
            {
                _logger.LogDebug("Processing batch {BatchNumber}: {BatchSize} chunks", 
                    (i / EmbeddingBatchSize) + 1, batch.Count);

                // Generate embeddings for the batch
                var embeddings = await _embeddingService.GenerateEmbeddingsAsync(searchableTexts, cancellationToken);
                
                // Store each chunk with its embedding
                var upsertTasks = new List<Task>();
                
                for (int j = 0; j < batch.Count && j < embeddings.Count; j++)
                {
                    var (chunk, _) = batch[j];
                    var embedding = embeddings[j];
                    
                    var metadata = CreateMetadata(chunk, repoName);
                    var id = GenerateChunkId(chunk);
                    
                    upsertTasks.Add(_qdrantService.UpsertAsync(id, embedding, metadata));
                }
                
                await Task.WhenAll(upsertTasks);
                
                _logger.LogDebug("Successfully stored batch {BatchNumber}", (i / EmbeddingBatchSize) + 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process batch starting at index {StartIndex}", i);
                
                // Continue with next batch rather than failing the entire operation
                var errorMessage = $"Failed to process embedding batch starting at index {i}: {ex.Message}";
                throw new InvalidOperationException(errorMessage, ex);
            }
        }
    }

    /// <summary>
    /// Creates metadata dictionary for a code chunk.
    /// </summary>
    /// <param name="chunk">The code chunk to create metadata for.</param>
    /// <param name="repoName">The repository name.</param>
    /// <returns>The metadata dictionary for Qdrant storage.</returns>
    private static Dictionary<string, object> CreateMetadata(CodeChunk chunk, string repoName)
    {
        var metadata = new Dictionary<string, object>
        {
            ["type"] = chunk.Type,
            ["name"] = chunk.Name,
            ["content"] = chunk.Content,
            ["file_path"] = chunk.FilePath,
            ["start_line"] = chunk.StartLine,
            ["end_line"] = chunk.EndLine,
            ["repo_name"] = repoName
        };

        if (!string.IsNullOrWhiteSpace(chunk.ParentName))
            metadata["parent_name"] = chunk.ParentName;

        if (!string.IsNullOrWhiteSpace(chunk.Namespace))
            metadata["namespace"] = chunk.Namespace;

        return metadata;
    }

    /// <summary>
    /// Generates a unique ID for a code chunk.
    /// </summary>
    /// <param name="chunk">The code chunk to generate an ID for.</param>
    /// <returns>A unique identifier for the chunk.</returns>
    private static Guid GenerateChunkId(CodeChunk chunk)
    {
        var idBase = $"{chunk.FilePath}:{chunk.StartLine}:{chunk.EndLine}:{chunk.Name}";
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(idBase));
        return new Guid(hash);
    }
}
