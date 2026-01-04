// MyPortfolio.Core/Features/RAG/Services/RAGOrchestrator.cs

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyPortfolio.Core.Features.Prioritizer.Services;
using MyPortfolio.Core.Features.RAG.Abstractions;
using MyPortfolio.Core.Features.RAG.Models;
using MyPortfolio.Core.Shared;

namespace MyPortfolio.Core.Features.RAG.Services;

/// <summary>
/// Orchestrates the complete RAG workflow with progress reporting and metrics.
/// 
/// Demonstrates:
/// - IAsyncEnumerable for streaming responses
/// - CancellationToken propagation throughout pipeline
/// - Timeout handling for long operations
/// - Graceful degradation on partial failures
/// - Comprehensive metrics collection
/// </summary>
public sealed class RAGOrchestrator : IRAGOrchestrator
{
    private readonly IDocumentPipeline _documentPipeline;
    private readonly IRAGStrategyFactory _strategyFactory;
    private readonly HttpClient _httpClient;
    private readonly ILogger<RAGOrchestrator> _logger;

    private const string ModelId = "gemini-2.5-flash";
    private const string StreamingApiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{ModelId}:streamGenerateContent";

    public RAGOrchestrator(
        IDocumentPipeline documentPipeline,
        IRAGStrategyFactory strategyFactory,
        HttpClient httpClient,
        IOptions<GeminiOptions> options,
        ILogger<RAGOrchestrator> logger)
    {
        _documentPipeline = documentPipeline;
        _strategyFactory = strategyFactory;
        _httpClient = httpClient;
        _logger = logger;

        // Configure HTTP client
        _httpClient.DefaultRequestHeaders.Add("X-Goog-Api-Key", options.Value.ApiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<IngestProgressUpdate> IngestDocumentAsync(
        RAGSession session,
        IngestDocumentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting document ingestion: {FileName} ({ContentLength} chars) into session {SessionId}",
            request.FileName,
            request.Content.Length,
            session.SessionId);

        // Validate session limits
        if (session.DocumentCount >= session.Config.MaxDocuments)
        {
            yield return new IngestProgressUpdate
            {
                Phase = "Error",
                CurrentStep = 0,
                TotalSteps = 1,
                Message = $"Session document limit reached ({session.Config.MaxDocuments} documents). Create a new session.",
                PercentComplete = 0
            };
            yield break;
        }

        if (request.Content.Length > session.Config.MaxFileSizeBytes)
        {
            yield return new IngestProgressUpdate
            {
                Phase = "Error",
                CurrentStep = 0,
                TotalSteps = 1,
                Message = $"File too large ({request.Content.Length / 1024}KB). Maximum: {session.Config.MaxFileSizeBytes / 1024}KB",
                PercentComplete = 0
            };
            yield break;
        }

        // Create document info
        var document = new DocumentInfo
        {
            Id = $"doc_{Guid.NewGuid():N}"[..16],
            FileName = request.FileName,
            Content = request.Content,
            CharacterCount = request.Content.Length,
            UploadedAt = DateTime.UtcNow
        };

        // Create progress channel for pipeline updates
        var progressQueue = new Queue<IngestProgressUpdate>();
        var progress = new Progress<IngestProgressUpdate>(update => progressQueue.Enqueue(update));

        // Start pipeline in background
        var pipelineTask = _documentPipeline.ProcessDocumentAsync(
            session,
            document,
            session.Config with
            {
                DefaultChunkingStrategy = request.ChunkingStrategy ?? session.Config.DefaultChunkingStrategy
            },
            progress,
            cancellationToken);

        // Yield progress updates as they arrive
        while (!pipelineTask.IsCompleted || progressQueue.Count > 0)
        {
            if (progressQueue.Count > 0)
            {
                yield return progressQueue.Dequeue();
            }
            else
            {
                await Task.Delay(50, cancellationToken);
            }
        }

        // Get final result
        var result = await pipelineTask;
        if (!result.IsSuccess)
        {
            yield return new IngestProgressUpdate
            {
                Phase = "Error",
                CurrentStep = 0,
                TotalSteps = 1,
                Message = result.Error,
                PercentComplete = 0
            };
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<RAGStreamChunk> QueryAsync(
        RAGSession session,
        RAGQueryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var metrics = new RAGMetrics();
        var totalStopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Starting RAG query in session {SessionId}: {Query}",
            session.SessionId,
            request.Query.Length > 100 ? request.Query[..100] + "..." : request.Query);

        // Validate session has documents
        if (session.ChunkCount == 0)
        {
            yield return new RAGStreamChunk
            {
                Type = "error",
                Content = "No documents in session. Please upload documents first."
            };
            yield break;
        }

        // Get strategy
        var strategy = _strategyFactory.GetStrategy(
            request.Strategy ?? session.Config.DefaultStrategy);

        // ===== RETRIEVAL PHASE =====
        var retrievalStopwatch = Stopwatch.StartNew();

        var retrievalResult = await strategy.RetrieveAsync(
            session,
            request.Query,
            request.TopK ?? session.Config.TopK,
            cancellationToken);

        retrievalStopwatch.Stop();
        metrics.RetrievalTimeMs = retrievalStopwatch.ElapsedMilliseconds;

        if (!retrievalResult.IsSuccess)
        {
            yield return new RAGStreamChunk
            {
                Type = "error",
                Content = $"Retrieval failed: {retrievalResult.Error}"
            };
            yield break;
        }

        var retrievedChunks = retrievalResult.Value!;
        metrics.ChunksRetrieved = retrievedChunks.Count;

        // Yield retrieval results
        yield return new RAGStreamChunk
        {
            Type = "retrieval",
            RetrievedChunks = retrievedChunks,
            Content = $"Retrieved {retrievedChunks.Count} relevant chunks using {strategy.StrategyType} strategy"
        };

        if (retrievedChunks.Count == 0)
        {
            yield return new RAGStreamChunk
            {
                Type = "generation",
                Content = "No relevant information found in the uploaded documents for your query."
            };
            
            yield return new RAGStreamChunk
            {
                Type = "done",
                Metrics = metrics
            };
            yield break;
        }

        // ===== GENERATION PHASE =====
        var generationStopwatch = Stopwatch.StartNew();

        // Build context from retrieved chunks
        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine("Context from uploaded documents:");
        contextBuilder.AppendLine();

        foreach (var chunk in retrievedChunks)
        {
            contextBuilder.AppendLine($"[Source: {chunk.Chunk.DocumentName}, Relevance: {chunk.SimilarityScore:P0}]");
            contextBuilder.AppendLine(chunk.Chunk.Content);
            contextBuilder.AppendLine();
        }

        var prompt = $"""
            You are a helpful assistant answering questions based on the provided context.
            Use ONLY the information from the context below to answer the question.
            If the context doesn't contain enough information, say so clearly.
            When referencing information, mention which source document it came from.

            {contextBuilder}

            Question: {request.Query}

            Answer:
            """;

        // Stream the response
        await foreach (var chunk in StreamGenerationAsync(prompt, cancellationToken))
        {
            yield return new RAGStreamChunk
            {
                Type = "generation",
                Content = chunk
            };
        }

        generationStopwatch.Stop();
        metrics.GenerationTimeMs = generationStopwatch.ElapsedMilliseconds;

        // Yield citations
        foreach (var chunk in retrievedChunks)
        {
            yield return new RAGStreamChunk
            {
                Type = "citation",
                Citation = new Citation
                {
                    DocumentName = chunk.Chunk.DocumentName,
                    ChunkPreview = chunk.Chunk.Content.Length > 200 
                        ? chunk.Chunk.Content[..200] + "..." 
                        : chunk.Chunk.Content,
                    RelevanceScore = chunk.SimilarityScore,
                    ChunkIndex = chunk.Chunk.ChunkIndex
                }
            };
        }

        // Final metrics
        totalStopwatch.Stop();
        metrics.TotalTimeMs = totalStopwatch.ElapsedMilliseconds;
        metrics.MemoryUsedBytes = GC.GetTotalMemory(false);

        // Update session metrics
        session.SessionMetrics.RetrievalTimeMs += metrics.RetrievalTimeMs;
        session.SessionMetrics.GenerationTimeMs += metrics.GenerationTimeMs;
        session.SessionMetrics.ChunksRetrieved += metrics.ChunksRetrieved;

        yield return new RAGStreamChunk
        {
            Type = "done",
            Metrics = metrics
        };

        _logger.LogInformation(
            "RAG query complete. Strategy: {Strategy}, Chunks: {Chunks}, Total: {TotalMs}ms",
            strategy.StrategyType,
            metrics.ChunksRetrieved,
            metrics.TotalTimeMs);
    }

    /// <summary>
    /// Streams generation response from Gemini.
    /// </summary>
    private async IAsyncEnumerable<string> StreamGenerationAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                maxOutputTokens = 2048,
                temperature = 0.7
            }
        };

        var requestJson = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, StreamingApiUrl + "?alt=sse")
        {
            Content = content
        };

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Generation API failed: {Status} - {Error}", response.StatusCode, errorBody);
            yield return $"[Error: Generation failed - {response.StatusCode}]";
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("data: "))
            {
                var jsonData = line[6..];
                if (jsonData == "[DONE]")
                    break;

                var textChunk = ExtractTextFromResponse(jsonData);
                if (!string.IsNullOrEmpty(textChunk))
                {
                    yield return textChunk;
                }
            }
        }
    }

    private string? ExtractTextFromResponse(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            return node?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse streaming response chunk");
            return null;
        }
    }
}
