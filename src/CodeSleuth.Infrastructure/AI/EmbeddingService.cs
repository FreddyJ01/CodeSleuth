using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeSleuth.Infrastructure.AI;

/// <summary>
/// Custom exception for embedding generation failures.
/// </summary>
public class EmbeddingGenerationException : Exception
{
    public EmbeddingGenerationException(string message) : base(message) { }
    public EmbeddingGenerationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Service for generating embeddings using Azure OpenAI or OpenAI direct endpoints.
/// Supports both single text and batch processing with retry logic.
/// </summary>
public class EmbeddingService : IEmbeddingService
{
    private readonly AzureOpenAIClient _client;
    private readonly ILogger<EmbeddingService> _logger;
    private const int MaxTokensPerRequest = 6000; // Much more conservative limit below 8192
    private const int EstimatedCharsPerToken = 3; // More aggressive estimate for code (typically 2-4 chars/token)
    private readonly string _embeddingModel;
    private readonly bool _isAzureEndpoint;
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;

    /// <summary>
    /// Initializes a new instance of the EmbeddingService class.
    /// </summary>
    /// <param name="endpoint">The OpenAI or Azure OpenAI endpoint URL.</param>
    /// <param name="apiKey">The API key for authentication.</param>
    /// <param name="embeddingModel">The embedding model to use (e.g., "text-embedding-3-small").</param>
    /// <param name="logger">The logger instance for logging operations.</param>
    /// <param name="maxRetries">Maximum number of retry attempts. Defaults to 3.</param>
    /// <param name="baseDelayMs">Base delay in milliseconds for exponential backoff. Defaults to 1000ms.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null or empty.</exception>
    public EmbeddingService(
        string endpoint, 
        string apiKey, 
        string embeddingModel, 
        ILogger<EmbeddingService> logger,
        int maxRetries = 3,
        int baseDelayMs = 1000)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentNullException(nameof(endpoint));
        
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentNullException(nameof(apiKey));
        
        if (string.IsNullOrWhiteSpace(embeddingModel))
            throw new ArgumentNullException(nameof(embeddingModel));

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _embeddingModel = embeddingModel;
        _maxRetries = Math.Max(0, maxRetries);
        _baseDelay = TimeSpan.FromMilliseconds(Math.Max(100, baseDelayMs));

        // Auto-detect if using Azure OpenAI or OpenAI direct
        _isAzureEndpoint = endpoint.Contains("azure.com", StringComparison.OrdinalIgnoreCase);

        if (_isAzureEndpoint)
        {
            _logger.LogInformation("Initializing EmbeddingService with Azure OpenAI endpoint: {Endpoint}", endpoint);
            _client = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));
        }
        else
        {
            _logger.LogInformation("Initializing EmbeddingService with OpenAI direct endpoint: {Endpoint}", endpoint);
            // For OpenAI direct, we don't use AzureOpenAIClient, we'll use the OpenAI SDK
            // For now, treat it as Azure with the OpenAI endpoint
            _client = new AzureOpenAIClient(new Uri("https://api.openai.com/v1/"), new System.ClientModel.ApiKeyCredential(apiKey));
        }

        _logger.LogInformation("EmbeddingService initialized with model: {Model}, maxRetries: {MaxRetries}", 
            _embeddingModel, _maxRetries);
    }

    /// <summary>
    /// Generates an embedding for a single text input.
    /// </summary>
    /// <param name="text">The text to generate an embedding for.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A float array representing the embedding vector.</returns>
    /// <exception cref="ArgumentNullException">Thrown when text is null or empty.</exception>
    /// <exception cref="EmbeddingGenerationException">Thrown when embedding generation fails after all retries.</exception>
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentNullException(nameof(text));

        _logger.LogDebug("Generating embedding for single text of length: {Length}", text.Length);

        var embeddings = await GenerateEmbeddingsAsync(new List<string> { text }, cancellationToken);
        return embeddings.First();
    }

    /// <summary>
    /// Generates embeddings for multiple text inputs in a batch.
    /// </summary>
    /// <param name="texts">The list of texts to generate embeddings for.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A list of float arrays representing the embedding vectors.</returns>
    /// <exception cref="ArgumentNullException">Thrown when texts is null.</exception>
    /// <exception cref="ArgumentException">Thrown when texts list is empty or contains null/empty strings.</exception>
    /// <exception cref="EmbeddingGenerationException">Thrown when embedding generation fails after all retries.</exception>
    public async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts == null)
            throw new ArgumentNullException(nameof(texts));

        if (texts.Count == 0)
            throw new ArgumentException("Text list cannot be empty", nameof(texts));

        if (texts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Text list cannot contain null or empty strings", nameof(texts));

        _logger.LogDebug("Generating embeddings for {Count} texts", texts.Count);

        // Process each text and handle chunking if needed
        var allEmbeddings = new List<float[]>();
        var processedTexts = new List<string>();
        
        // Split texts that are too long into chunks
        foreach (var text in texts)
        {
            var estimatedTokens = EstimateTokenCount(text);
            if (estimatedTokens > MaxTokensPerRequest)
            {
                _logger.LogWarning("Text exceeds token limit ({EstimatedTokens} > {MaxTokens}), splitting into chunks", 
                    estimatedTokens, MaxTokensPerRequest);
                
                var chunks = SplitTextIntoChunks(text);
                processedTexts.AddRange(chunks);
            }
            else
            {
                processedTexts.Add(text);
            }
        }

        if (processedTexts.Count != texts.Count)
        {
            _logger.LogInformation("Split {OriginalCount} texts into {ProcessedCount} chunks due to token limits", 
                texts.Count, processedTexts.Count);
        }

        // Process texts in batches to stay within API limits
        const int batchSize = 100; // Conservative batch size for API
        for (int i = 0; i < processedTexts.Count; i += batchSize)
        {
            var batch = processedTexts.Skip(i).Take(batchSize).ToList();
            
            var batchEmbeddings = await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    var embeddingClient = _client.GetEmbeddingClient(_embeddingModel);
                    var response = await embeddingClient.GenerateEmbeddingsAsync(batch, new OpenAI.Embeddings.EmbeddingGenerationOptions { });

                    var embeddings = new List<float[]>();
                    foreach (var embeddingItem in response.Value)
                    {
                        embeddings.Add(embeddingItem.ToFloats().ToArray());
                    }

                    _logger.LogDebug("Successfully generated {Count} embeddings for batch starting at index {StartIndex}", 
                        embeddings.Count, i);
                    return embeddings;
                }
                catch (RequestFailedException ex)
                {
                    _logger.LogError(ex, "Azure OpenAI request failed for batch starting at index {StartIndex}: {Message}", i, ex.Message);
                    throw new EmbeddingGenerationException($"Failed to process embedding batch starting at index {i}: {ex.Message}", ex);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error during embedding generation for batch starting at index {StartIndex}: {Message}", i, ex.Message);
                    throw new EmbeddingGenerationException($"Failed to process embedding batch starting at index {i}: Unexpected error during embedding generation: {ex.Message}", ex);
                }
            }, cancellationToken);
            
            allEmbeddings.AddRange(batchEmbeddings);
        }

        _logger.LogDebug("Successfully generated {Count} total embeddings from {OriginalCount} original texts", 
            allEmbeddings.Count, texts.Count);
        
        return allEmbeddings;
    }

    /// <summary>
    /// Executes an operation with exponential backoff retry logic.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of the operation.</returns>
    /// <exception cref="EmbeddingGenerationException">Thrown when all retry attempts fail.</exception>
    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await operation();
            }
            catch (EmbeddingGenerationException ex) when (attempt < _maxRetries && IsRetryable(ex))
            {
                lastException = ex;
                var delay = CalculateDelay(attempt);
                
                _logger.LogWarning(ex, 
                    "Embedding generation attempt {Attempt} failed, retrying in {Delay}ms: {Message}", 
                    attempt + 1, delay.TotalMilliseconds, ex.Message);

                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Non-retryable error during embedding generation: {Message}", ex.Message);
                throw;
            }
        }

        _logger.LogError("All {MaxRetries} retry attempts failed for embedding generation", _maxRetries + 1);
        throw new EmbeddingGenerationException(
            $"Failed to generate embeddings after {_maxRetries + 1} attempts", 
            lastException!);
    }

    /// <summary>
    /// Determines if an exception is retryable based on its type and properties.
    /// </summary>
    /// <param name="exception">The exception to evaluate.</param>
    /// <returns>True if the exception is retryable, false otherwise.</returns>
    private static bool IsRetryable(Exception exception)
    {
        // Retry on specific Azure/OpenAI errors that are typically transient
        if (exception.InnerException is RequestFailedException requestEx)
        {
            // Retry on rate limiting, service unavailable, and timeout errors
            return requestEx.Status is 429 or 503 or 502 or 504;
        }

        // Retry on timeout exceptions
        if (exception.InnerException is TaskCanceledException or TimeoutException)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Calculates the delay for the next retry attempt using exponential backoff.
    /// </summary>
    /// <param name="attempt">The current attempt number (0-based).</param>
    /// <returns>The delay before the next retry attempt.</returns>
    private TimeSpan CalculateDelay(int attempt)
    {
        // Exponential backoff with jitter: baseDelay * 2^attempt + random(0, baseDelay/2)
        var exponentialDelay = _baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var jitter = Random.Shared.NextDouble() * (_baseDelay.TotalMilliseconds / 2);
        var totalDelay = exponentialDelay + jitter;

        // Cap the delay at 30 seconds
        return TimeSpan.FromMilliseconds(Math.Min(totalDelay, 30_000));
    }

    /// <summary>
    /// Estimates the number of tokens in a text string.
    /// Uses a conservative character-to-token ratio for estimation.
    /// </summary>
    /// <param name="text">The text to estimate tokens for.</param>
    /// <returns>Estimated number of tokens.</returns>
    private int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
            
        // Conservative estimate: ~4 characters per token for English text
        return (int)Math.Ceiling((double)text.Length / EstimatedCharsPerToken);
    }

    /// <summary>
    /// Splits a text into smaller chunks that fit within the token limit.
    /// </summary>
    /// <param name="text">The text to split.</param>
    /// <returns>A list of text chunks that each fit within the token limit.</returns>
    private List<string> SplitTextIntoChunks(string text)
    {
        var chunks = new List<string>();
        
        if (string.IsNullOrEmpty(text))
            return chunks;

        var estimatedTokens = EstimateTokenCount(text);
        
        // If the text is already within limits, return as-is
        if (estimatedTokens <= MaxTokensPerRequest)
        {
            chunks.Add(text);
            return chunks;
        }

        // Calculate approximate character limit per chunk
        var maxCharsPerChunk = MaxTokensPerRequest * EstimatedCharsPerToken;
        
        // Split by lines first to preserve code structure
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
        var currentChunk = new List<string>();
        var currentChunkLength = 0;

        foreach (var line in lines)
        {
            var lineLength = line.Length + 1; // +1 for newline
            
            // If adding this line would exceed the limit, save current chunk and start new one
            if (currentChunkLength + lineLength > maxCharsPerChunk && currentChunk.Count > 0)
            {
                chunks.Add(string.Join("\n", currentChunk));
                currentChunk.Clear();
                currentChunkLength = 0;
            }
            
            // If a single line is too long, split it by sentences or characters
            if (lineLength > maxCharsPerChunk)
            {
                var lineParts = SplitLongLine(line, maxCharsPerChunk);
                chunks.AddRange(lineParts);
            }
            else
            {
                currentChunk.Add(line);
                currentChunkLength += lineLength;
            }
        }

        // Add remaining lines as the last chunk
        if (currentChunk.Count > 0)
        {
            chunks.Add(string.Join("\n", currentChunk));
        }

        _logger.LogDebug("Split text of {OriginalTokens} estimated tokens into {ChunkCount} chunks", 
            estimatedTokens, chunks.Count);

        return chunks;
    }

    /// <summary>
    /// Splits a long line into smaller parts that fit within the character limit.
    /// </summary>
    /// <param name="line">The line to split.</param>
    /// <param name="maxChars">Maximum characters per part.</param>
    /// <returns>List of line parts.</returns>
    private List<string> SplitLongLine(string line, int maxChars)
    {
        var parts = new List<string>();
        
        if (line.Length <= maxChars)
        {
            parts.Add(line);
            return parts;
        }

        // Try to split by sentences first
        var sentences = line.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length > 1)
        {
            var currentPart = "";
            foreach (var sentence in sentences)
            {
                var sentenceWithPunctuation = sentence + ".";
                if (currentPart.Length + sentenceWithPunctuation.Length <= maxChars)
                {
                    currentPart += sentenceWithPunctuation;
                }
                else
                {
                    if (!string.IsNullOrEmpty(currentPart))
                    {
                        parts.Add(currentPart);
                        currentPart = "";
                    }
                    
                    if (sentenceWithPunctuation.Length <= maxChars)
                    {
                        currentPart = sentenceWithPunctuation;
                    }
                    else
                    {
                        // Split by character if sentence is too long
                        for (int i = 0; i < sentenceWithPunctuation.Length; i += maxChars)
                        {
                            var end = Math.Min(i + maxChars, sentenceWithPunctuation.Length);
                            parts.Add(sentenceWithPunctuation.Substring(i, end - i));
                        }
                    }
                }
            }
            
            if (!string.IsNullOrEmpty(currentPart))
            {
                parts.Add(currentPart);
            }
        }
        else
        {
            // Split by character as last resort
            for (int i = 0; i < line.Length; i += maxChars)
            {
                var end = Math.Min(i + maxChars, line.Length);
                parts.Add(line.Substring(i, end - i));
            }
        }

        return parts;
    }

    /// <summary>
    /// Gets information about the current configuration.
    /// </summary>
    /// <returns>Configuration information for debugging purposes.</returns>
    public string GetConfigurationInfo()
    {
        return $"EmbeddingService - Model: {_embeddingModel}, " +
               $"Endpoint Type: {(_isAzureEndpoint ? "Azure OpenAI" : "OpenAI Direct")}, " +
               $"Max Retries: {_maxRetries}, " +
               $"Base Delay: {_baseDelay.TotalMilliseconds}ms";
    }

    /// <summary>
    /// Disposes the OpenAI client resources.
    /// </summary>
    public void Dispose()
    {
        // AzureOpenAIClient doesn't implement IDisposable in version 2.1.0
        _logger.LogDebug("EmbeddingService disposed");
    }
}
