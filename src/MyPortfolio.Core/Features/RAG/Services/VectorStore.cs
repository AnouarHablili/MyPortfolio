// MyPortfolio.Core/Features/RAG/Services/VectorStore.cs

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MyPortfolio.Core.Features.RAG.Abstractions;
using MyPortfolio.Core.Features.RAG.Models;

namespace MyPortfolio.Core.Features.RAG.Services;

/// <summary>
/// In-memory vector store with SIMD-accelerated similarity search.
/// Demonstrates low-level optimization using Vector<T> for hardware acceleration.
/// 
/// Key optimizations:
/// - SIMD (Single Instruction Multiple Data) for parallel float operations
/// - Span<T> for zero-allocation memory access
/// - Parallel search using Parallel.ForEach for large indices
/// </summary>
public sealed class VectorStore : IVectorStore
{
    private readonly ILogger<VectorStore> _logger;

    // Threshold for switching to parallel search
    private const int ParallelSearchThreshold = 100;

    public VectorStore(ILogger<VectorStore> logger)
    {
        _logger = logger;
        _logger.LogInformation(
            "VectorStore initialized. SIMD available: {SimdAvailable}, Vector size: {VectorSize} bytes",
            Vector.IsHardwareAccelerated,
            Vector<float>.Count * sizeof(float));
    }

    /// <inheritdoc/>
    public void AddChunk(RAGSession session, ChunkWithEmbedding chunk)
    {
        session.VectorIndex.Add(chunk);
        _logger.LogDebug(
            "Added chunk {ChunkId} to session {SessionId}. Total chunks: {Count}",
            chunk.Chunk.Id,
            session.SessionId,
            session.ChunkCount);
    }

    /// <inheritdoc/>
    public void AddChunks(RAGSession session, IEnumerable<ChunkWithEmbedding> chunks)
    {
        foreach (var chunk in chunks)
        {
            session.VectorIndex.Add(chunk);
        }

        _logger.LogInformation(
            "Added chunks to session {SessionId}. Total chunks: {Count}",
            session.SessionId,
            session.ChunkCount);
    }

    /// <inheritdoc/>
    public IReadOnlyList<RetrievalResult> Search(
        RAGSession session,
        float[] queryEmbedding,
        int topK,
        float minScore = 0.0f)
    {
        var sw = Stopwatch.StartNew();
        var chunks = session.VectorIndex.ToArray(); // Snapshot for thread safety

        if (chunks.Length == 0)
        {
            _logger.LogWarning("Search called on empty vector index for session {SessionId}", session.SessionId);
            return Array.Empty<RetrievalResult>();
        }

        _logger.LogDebug(
            "Searching {Count} chunks in session {SessionId} for top {TopK}",
            chunks.Length,
            session.SessionId,
            topK);

        // Choose search strategy based on index size
        var scoredChunks = chunks.Length >= ParallelSearchThreshold
            ? SearchParallel(chunks, queryEmbedding)
            : SearchSequential(chunks, queryEmbedding);

        // Filter by minimum score, sort by similarity, take top K
        var results = scoredChunks
            .Where(x => x.Score >= minScore)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select((x, rank) => new RetrievalResult
            {
                Chunk = x.Chunk.Chunk,
                SimilarityScore = x.Score,
                Rank = rank + 1
            })
            .ToList();

        sw.Stop();

        _logger.LogInformation(
            "Search completed in {ElapsedMs}ms. Found {ResultCount} results above threshold {MinScore:F2}",
            sw.ElapsedMilliseconds,
            results.Count,
            minScore);

        return results;
    }

    /// <summary>
    /// Sequential search for small indices.
    /// </summary>
    private IEnumerable<(ChunkWithEmbedding Chunk, float Score)> SearchSequential(
        ChunkWithEmbedding[] chunks,
        float[] queryEmbedding)
    {
        foreach (var chunk in chunks)
        {
            var score = CosineSimilaritySIMD(queryEmbedding, chunk.Embedding);
            yield return (chunk, score);
        }
    }

    /// <summary>
    /// Parallel search for large indices.
    /// Uses thread-safe collection for results.
    /// </summary>
    private IEnumerable<(ChunkWithEmbedding Chunk, float Score)> SearchParallel(
        ChunkWithEmbedding[] chunks,
        float[] queryEmbedding)
    {
        var results = new (ChunkWithEmbedding Chunk, float Score)[chunks.Length];

        Parallel.For(0, chunks.Length, i =>
        {
            var score = CosineSimilaritySIMD(queryEmbedding, chunks[i].Embedding);
            results[i] = (chunks[i], score);
        });

        return results;
    }

    /// <summary>
    /// SIMD-accelerated cosine similarity calculation.
    /// Uses Vector<float> for hardware-accelerated parallel operations.
    /// 
    /// Cosine Similarity = (A · B) / (||A|| * ||B||)
    /// 
    /// Performance: Processes Vector<float>.Count floats per instruction
    /// (typically 4-8 on modern CPUs with SSE/AVX support)
    /// </summary>
    public static float CosineSimilaritySIMD(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same dimension");

        if (a.Length == 0)
            return 0f;

        var dotProduct = 0f;
        var magnitudeA = 0f;
        var magnitudeB = 0f;

        var vectorSize = Vector<float>.Count;
        var i = 0;

        // Process vectors in SIMD chunks
        if (Vector.IsHardwareAccelerated && a.Length >= vectorSize)
        {
            var vecDot = Vector<float>.Zero;
            var vecMagA = Vector<float>.Zero;
            var vecMagB = Vector<float>.Zero;

            // Process full vector-sized chunks
            for (; i <= a.Length - vectorSize; i += vectorSize)
            {
                var vecA = new Vector<float>(a.Slice(i, vectorSize));
                var vecB = new Vector<float>(b.Slice(i, vectorSize));

                vecDot += vecA * vecB;      // Parallel multiply-add
                vecMagA += vecA * vecA;      // Parallel square
                vecMagB += vecB * vecB;      // Parallel square
            }

            // Horizontal sum of vector lanes
            dotProduct = Vector.Sum(vecDot);
            magnitudeA = Vector.Sum(vecMagA);
            magnitudeB = Vector.Sum(vecMagB);
        }

        // Process remaining elements (tail)
        for (; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        // Avoid division by zero
        var denominator = MathF.Sqrt(magnitudeA) * MathF.Sqrt(magnitudeB);
        if (denominator < float.Epsilon)
            return 0f;

        return dotProduct / denominator;
    }

    /// <summary>
    /// Fallback non-SIMD cosine similarity for comparison/testing.
    /// </summary>
    public static float CosineSimilarityScalar(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same dimension");

        var dotProduct = 0f;
        var magnitudeA = 0f;
        var magnitudeB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(magnitudeA) * MathF.Sqrt(magnitudeB);
        if (denominator < float.Epsilon)
            return 0f;

        return dotProduct / denominator;
    }

    /// <summary>
    /// Euclidean distance between two vectors (alternative similarity metric).
    /// Smaller distance = more similar.
    /// </summary>
    public static float EuclideanDistanceSIMD(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same dimension");

        if (a.Length == 0)
            return 0f;

        var sum = 0f;
        var vectorSize = Vector<float>.Count;
        var i = 0;

        if (Vector.IsHardwareAccelerated && a.Length >= vectorSize)
        {
            var vecSum = Vector<float>.Zero;

            for (; i <= a.Length - vectorSize; i += vectorSize)
            {
                var vecA = new Vector<float>(a.Slice(i, vectorSize));
                var vecB = new Vector<float>(b.Slice(i, vectorSize));
                var diff = vecA - vecB;
                vecSum += diff * diff;
            }

            sum = Vector.Sum(vecSum);
        }

        for (; i < a.Length; i++)
        {
            var diff = a[i] - b[i];
            sum += diff * diff;
        }

        return MathF.Sqrt(sum);
    }
}
