// MyPortfolio.Core/Features/RAG/Services/GeminiEmbeddingService.cs

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyPortfolio.Core.Features.Prioritizer.Services;
using MyPortfolio.Core.Features.RAG.Abstractions;
using MyPortfolio.Core.Shared;

namespace MyPortfolio.Core.Features.RAG.Services;

/// <summary>
/// Configuration options for the Gemini Embedding Service.
/// </summary>
public sealed class GeminiEmbeddingOptions
{
    public const string Section = "GeminiEmbedding";

    /// <summary>
    /// Maximum concurrent embedding requests. Default: 5.
    /// Demonstrates rate limiting with SemaphoreSlim.
    /// </summary>
    public int MaxConcurrentRequests { get; init; } = 5;

    /// <summary>
    /// Cache duration for embeddings. Default: 30 minutes.
    /// Demonstrates IMemoryCache for deduplication.
    /// </summary>
    public int CacheDurationMinutes { get; init; } = 30;

    /// <summary>
    /// Request timeout in seconds. Default: 30.
    /// </summary>
    public int RequestTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Maximum retry attempts. Default: 3.
    /// </summary>
    public int MaxRetries { get; init; } = 3;
}

/// <summary>
/// Generates embeddings using Gemini API with caching and rate limiting.
/// Demonstrates:
/// - IMemoryCache for embedding deduplication (avoids redundant API calls)
/// - SemaphoreSlim for rate limiting (prevents API throttling)
/// - Parallel batch processing with Task.WhenAll
/// - Progress reporting via IProgress<T>
/// </summary>
public sealed class GeminiEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GeminiEmbeddingService> _logger;
    private readonly GeminiEmbeddingOptions _options;
    private readonly SemaphoreSlim _rateLimiter;

    // Embedding model - text-embedding-004 is the latest Gemini embedding model
    private const string ModelId = "text-embedding-004";
    private const string ApiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{ModelId}:embedContent";

    // Cache statistics for monitoring
    private int _cacheHits;
    private int _cacheMisses;

    public GeminiEmbeddingService(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<GeminiEmbeddingOptions> embeddingOptions,
        ILogger<GeminiEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
        _options = embeddingOptions.Value;

        // Reuse Gemini API key from main service
        var apiKey = geminiOptions.Value.ApiKey;
        _httpClient.DefaultRequestHeaders.Add("X-Goog-Api-Key", apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);

        // Initialize rate limiter (SemaphoreSlim) for concurrent request control
        _rateLimiter = new SemaphoreSlim(_options.MaxConcurrentRequests);

        _logger.LogInformation(
            "GeminiEmbeddingService initialized. MaxConcurrent={Max}, CacheDuration={Duration}min",
            _options.MaxConcurrentRequests,
            _options.CacheDurationMinutes);
    }

    /// <inheritdoc/>
    public async Task<Result<float[]>> GetEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        // Generate cache key from text hash
        var cacheKey = GenerateCacheKey(text);

        // Check cache first (deduplication)
        if (_cache.TryGetValue<float[]>(cacheKey, out var cachedEmbedding))
        {
            Interlocked.Increment(ref _cacheHits);
            _logger.LogDebug("Cache HIT for embedding (hash: {Hash})", cacheKey[..12]);
            return Result<float[]>.Success(cachedEmbedding!);
        }

        Interlocked.Increment(ref _cacheMisses);
        _logger.LogDebug("Cache MISS for embedding (hash: {Hash})", cacheKey[..12]);

        // Acquire rate limiter slot
        await _rateLimiter.WaitAsync(cancellationToken);
        try
        {
            // Double-check cache after acquiring semaphore (another request might have populated it)
            if (_cache.TryGetValue<float[]>(cacheKey, out cachedEmbedding))
            {
                Interlocked.Increment(ref _cacheHits);
                return Result<float[]>.Success(cachedEmbedding!);
            }

            // Make API request
            var result = await CallEmbeddingApiAsync(text, cancellationToken);

            if (result.IsSuccess)
            {
                // Cache the result
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(_options.CacheDurationMinutes))
                    .SetSize(result.Value!.Length * sizeof(float)); // Memory-aware caching

                _cache.Set(cacheKey, result.Value, cacheOptions);
                _logger.LogDebug("Cached embedding (hash: {Hash}, dim: {Dim})", cacheKey[..12], result.Value.Length);
            }

            return result;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<float[]>>> GetEmbeddingsBatchAsync(
        IReadOnlyList<string> texts,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
            return Result<IReadOnlyList<float[]>>.Success(Array.Empty<float[]>());

        _logger.LogInformation("Starting batch embedding for {Count} texts", texts.Count);
        var sw = Stopwatch.StartNew();

        // Process all texts in parallel with rate limiting
        var completed = 0;
        var results = new float[texts.Count][];
        var errors = new List<string>();

        // Use Parallel.ForEachAsync for controlled parallelism
        await Parallel.ForEachAsync(
            texts.Select((text, index) => (text, index)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.MaxConcurrentRequests,
                CancellationToken = cancellationToken
            },
            async (item, ct) =>
            {
                var result = await GetEmbeddingAsync(item.text, ct);

                if (result.IsSuccess)
                {
                    results[item.index] = result.Value!;
                }
                else
                {
                    lock (errors)
                    {
                        errors.Add($"Text {item.index}: {result.Error}");
                    }
                }

                var current = Interlocked.Increment(ref completed);
                progress?.Report(current);
            });

        sw.Stop();

        if (errors.Count > 0)
        {
            _logger.LogWarning(
                "Batch embedding completed with {ErrorCount} errors in {ElapsedMs}ms",
                errors.Count,
                sw.ElapsedMilliseconds);

            if (errors.Count == texts.Count)
            {
                return Result<IReadOnlyList<float[]>>.Failure(
                    $"All {errors.Count} embedding requests failed. First error: {errors[0]}");
            }
        }

        _logger.LogInformation(
            "Batch embedding completed: {Success}/{Total} in {ElapsedMs}ms. Cache: {Hits} hits, {Misses} misses",
            texts.Count - errors.Count,
            texts.Count,
            sw.ElapsedMilliseconds,
            _cacheHits,
            _cacheMisses);

        return Result<IReadOnlyList<float[]>>.Success(results);
    }

    /// <inheritdoc/>
    public (int Hits, int Misses) GetCacheStats() => (_cacheHits, _cacheMisses);

    /// <inheritdoc/>
    public void ClearCache()
    {
        // IMemoryCache doesn't have a Clear method, but we reset stats
        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
        _logger.LogInformation("Embedding cache stats reset");
    }

    private async Task<Result<float[]>> CallEmbeddingApiAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            model = $"models/{ModelId}",
            content = new
            {
                parts = new[]
                {
                    new { text }
                }
            }
        };

        var requestJson = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        for (int attempt = 0; attempt < _options.MaxRetries; attempt++)
        {
            try
            {
                using var response = await _httpClient.PostAsync(ApiUrl, content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && 
                        attempt < _options.MaxRetries - 1)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                        _logger.LogWarning(
                            "Rate limited (429). Retrying in {Delay}s (attempt {Attempt}/{Max})",
                            delay.TotalSeconds, attempt + 1, _options.MaxRetries);
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    return Result<float[]>.Failure($"Embedding API failed: {response.StatusCode} - {errorBody}");
                }

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var responseNode = JsonNode.Parse(responseJson);

                var embeddingArray = responseNode?["embedding"]?["values"]?.AsArray();
                if (embeddingArray == null)
                {
                    return Result<float[]>.Failure("Embedding response missing 'embedding.values' field");
                }

                var embedding = embeddingArray
                    .Select(v => v?.GetValue<float>() ?? 0f)
                    .ToArray();

                return Result<float[]>.Success(embedding);
            }
            catch (OperationCanceledException)
            {
                return Result<float[]>.Failure("Embedding request was cancelled");
            }
            catch (HttpRequestException ex) when (attempt < _options.MaxRetries - 1)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning(ex,
                    "HTTP error during embedding. Retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds, attempt + 1, _options.MaxRetries);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during embedding request");
                return Result<float[]>.Failure($"Embedding failed: {ex.Message}");
            }
        }

        return Result<float[]>.Failure($"Embedding failed after {_options.MaxRetries} attempts");
    }

    /// <summary>
    /// Generates a deterministic cache key from text content using SHA256.
    /// </summary>
    private static string GenerateCacheKey(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return $"emb_{Convert.ToHexString(bytes)}";
    }

    public void Dispose()
    {
        _rateLimiter.Dispose();
    }
}
