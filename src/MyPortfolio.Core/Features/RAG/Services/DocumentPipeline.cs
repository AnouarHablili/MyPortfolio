// MyPortfolio.Core/Features/RAG/Services/DocumentPipeline.cs

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MyPortfolio.Core.Features.RAG.Abstractions;
using MyPortfolio.Core.Features.RAG.Models;
using MyPortfolio.Core.Shared;

namespace MyPortfolio.Core.Features.RAG.Services;

/// <summary>
/// Document processing pipeline using producer-consumer pattern.
/// 
/// Demonstrates:
/// - Channel<T> for high-throughput parallel processing
/// - Pipeline stages (chunking ? embedding ? indexing)
/// - Bounded channels for backpressure management
/// - IProgress<T> for real-time progress reporting
/// - CancellationToken propagation throughout
/// 
/// Architecture:
/// ????????????    ???????????????    ????????????????    ????????????
/// ? Document ? ? ?  Chunker    ? ? ?  Embedder    ? ? ?  Indexer ?
/// ?  Input   ?    ? (Producer)  ?    ? (Transform)  ?    ?(Consumer)?
/// ????????????    ???????????????    ????????????????    ????????????
///                      ?                   ?                  ?
///                 Channel<Chunk>    Channel<Embedding>   VectorStore
/// </summary>
public sealed class DocumentPipeline : IDocumentPipeline
{
    private readonly IChunkingService _chunkingService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<DocumentPipeline> _logger;

    // Channel capacity for backpressure
    private const int ChunkChannelCapacity = 50;
    private const int EmbeddingChannelCapacity = 20;

    public DocumentPipeline(
        IChunkingService chunkingService,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ILogger<DocumentPipeline> logger)
    {
        _chunkingService = chunkingService;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<RAGMetrics>> ProcessDocumentAsync(
        RAGSession session,
        DocumentInfo document,
        RAGSessionConfig config,
        IProgress<IngestProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var metrics = new RAGMetrics();
        var totalStopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Starting pipeline for document {FileName} ({CharCount} chars)",
            document.FileName,
            document.CharacterCount);

        try
        {
            // Report: Starting
            progress?.Report(new IngestProgressUpdate
            {
                Phase = "Starting",
                CurrentStep = 0,
                TotalSteps = 4,
                Message = $"Processing {document.FileName}...",
                PercentComplete = 0
            });

            // ===== STAGE 1: Chunking =====
            var chunkStopwatch = Stopwatch.StartNew();
            
            progress?.Report(new IngestProgressUpdate
            {
                Phase = "Chunking",
                CurrentStep = 1,
                TotalSteps = 4,
                Message = $"Splitting document into chunks (strategy: {config.DefaultChunkingStrategy})...",
                PercentComplete = 10
            });

            var chunks = await _chunkingService.ChunkDocumentAsync(
                document,
                config.DefaultChunkingStrategy,
                config.ChunkSize,
                config.ChunkOverlap,
                cancellationToken);

            chunkStopwatch.Stop();
            metrics.ChunkingTimeMs = chunkStopwatch.ElapsedMilliseconds;
            metrics.TotalChunks = chunks.Count;

            _logger.LogInformation(
                "Chunking complete: {ChunkCount} chunks in {ElapsedMs}ms",
                chunks.Count,
                metrics.ChunkingTimeMs);

            if (chunks.Count == 0)
            {
                return Result<RAGMetrics>.Failure("Document produced no chunks");
            }

            // ===== STAGE 2: Create Pipeline Channels =====
            var chunkChannel = Channel.CreateBounded<TextChunk>(
                new BoundedChannelOptions(ChunkChannelCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleWriter = true,
                    SingleReader = false
                });

            var embeddingChannel = Channel.CreateBounded<ChunkWithEmbedding>(
                new BoundedChannelOptions(EmbeddingChannelCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleWriter = false,
                    SingleReader = true
                });

            // ===== STAGE 3: Run Pipeline Stages in Parallel =====
            var embeddingStopwatch = Stopwatch.StartNew();

            progress?.Report(new IngestProgressUpdate
            {
                Phase = "Embedding",
                CurrentStep = 2,
                TotalSteps = 4,
                Message = $"Generating embeddings for {chunks.Count} chunks...",
                PercentComplete = 30
            });

            // Track embedding progress
            var embeddedCount = 0;
            var embeddingProgress = new Progress<int>(count =>
            {
                embeddedCount = count;
                var percent = 30 + (int)(count / (double)chunks.Count * 50);
                progress?.Report(new IngestProgressUpdate
                {
                    Phase = "Embedding",
                    CurrentStep = 2,
                    TotalSteps = 4,
                    Message = $"Embedded {count}/{chunks.Count} chunks...",
                    PercentComplete = percent
                });
            });

            // Start all pipeline stages
            var producerTask = ProduceChunksAsync(chunks, chunkChannel.Writer, cancellationToken);
            var transformerTask = TransformChunksAsync(
                chunkChannel.Reader, 
                embeddingChannel.Writer, 
                embeddingProgress,
                metrics,
                cancellationToken);
            var consumerTask = ConsumeEmbeddingsAsync(
                session, 
                embeddingChannel.Reader, 
                cancellationToken);

            // Wait for all stages to complete
            await Task.WhenAll(producerTask, transformerTask, consumerTask);

            embeddingStopwatch.Stop();
            metrics.EmbeddingTimeMs = embeddingStopwatch.ElapsedMilliseconds;

            _logger.LogInformation(
                "Embedding complete: {Count} embeddings in {ElapsedMs}ms (Cache: {Hits} hits, {Misses} misses)",
                chunks.Count,
                metrics.EmbeddingTimeMs,
                metrics.EmbeddingCacheHits,
                metrics.EmbeddingCacheMisses);

            // ===== STAGE 4: Finalize =====
            progress?.Report(new IngestProgressUpdate
            {
                Phase = "Indexing",
                CurrentStep = 3,
                TotalSteps = 4,
                Message = "Adding to vector index...",
                PercentComplete = 90
            });

            // Add document to session
            session.Documents.Add(document);
            session.Touch(); // Extend session TTL

            totalStopwatch.Stop();
            metrics.TotalTimeMs = totalStopwatch.ElapsedMilliseconds;
            metrics.MemoryUsedBytes = GC.GetTotalMemory(false);

            // Update session metrics
            session.SessionMetrics.TotalChunks += metrics.TotalChunks;
            session.SessionMetrics.ChunkingTimeMs += metrics.ChunkingTimeMs;
            session.SessionMetrics.EmbeddingTimeMs += metrics.EmbeddingTimeMs;
            session.SessionMetrics.EmbeddingCacheHits += metrics.EmbeddingCacheHits;
            session.SessionMetrics.EmbeddingCacheMisses += metrics.EmbeddingCacheMisses;

            progress?.Report(new IngestProgressUpdate
            {
                Phase = "Complete",
                CurrentStep = 4,
                TotalSteps = 4,
                Message = $"Processed {document.FileName}: {chunks.Count} chunks in {metrics.TotalTimeMs}ms",
                PercentComplete = 100
            });

            _logger.LogInformation(
                "Pipeline complete for {FileName}: {ChunkCount} chunks, {TotalMs}ms total",
                document.FileName,
                chunks.Count,
                metrics.TotalTimeMs);

            return Result<RAGMetrics>.Success(metrics);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Pipeline cancelled for document {FileName}", document.FileName);
            return Result<RAGMetrics>.Failure("Document processing was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed for document {FileName}", document.FileName);
            return Result<RAGMetrics>.Failure($"Pipeline error: {ex.Message}");
        }
    }

    /// <summary>
    /// Producer stage: Writes chunks to the channel.
    /// </summary>
    private async Task ProduceChunksAsync(
        IReadOnlyList<TextChunk> chunks,
        ChannelWriter<TextChunk> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var chunk in chunks)
            {
                await writer.WriteAsync(chunk, cancellationToken);
            }
        }
        finally
        {
            writer.Complete();
        }
    }

    /// <summary>
    /// Transformer stage: Reads chunks, generates embeddings, writes to output channel.
    /// Uses parallel processing for embedding generation.
    /// </summary>
    private async Task TransformChunksAsync(
        ChannelReader<TextChunk> reader,
        ChannelWriter<ChunkWithEmbedding> writer,
        IProgress<int> progress,
        RAGMetrics metrics,
        CancellationToken cancellationToken)
    {
        var processedCount = 0;

        try
        {
            // Process chunks in parallel batches
            await foreach (var chunk in reader.ReadAllAsync(cancellationToken))
            {
                var embeddingResult = await _embeddingService.GetEmbeddingAsync(
                    chunk.Content, 
                    cancellationToken);

                if (embeddingResult.IsSuccess)
                {
                    var chunkWithEmbedding = new ChunkWithEmbedding
                    {
                        Chunk = chunk,
                        Embedding = embeddingResult.Value!
                    };

                    await writer.WriteAsync(chunkWithEmbedding, cancellationToken);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to embed chunk {ChunkId}: {Error}",
                        chunk.Id,
                        embeddingResult.Error);
                }

                processedCount++;
                progress.Report(processedCount);
            }

            // Update cache stats
            var (hits, misses) = _embeddingService.GetCacheStats();
            metrics.EmbeddingCacheHits = hits;
            metrics.EmbeddingCacheMisses = misses;
        }
        finally
        {
            writer.Complete();
        }
    }

    /// <summary>
    /// Consumer stage: Reads embedded chunks and adds to vector store.
    /// </summary>
    private async Task ConsumeEmbeddingsAsync(
        RAGSession session,
        ChannelReader<ChunkWithEmbedding> reader,
        CancellationToken cancellationToken)
    {
        await foreach (var chunkWithEmbedding in reader.ReadAllAsync(cancellationToken))
        {
            _vectorStore.AddChunk(session, chunkWithEmbedding);
        }
    }
}
