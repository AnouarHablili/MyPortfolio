// MyPortfolio.Core/Features/RAG/Abstractions/IRAGServices.cs

using MyPortfolio.Core.Features.RAG.Models;
using MyPortfolio.Core.Shared;

namespace MyPortfolio.Core.Features.RAG.Abstractions;

/// <summary>
/// Manages RAG sessions stored in-memory with TTL expiration.
/// </summary>
public interface IRAGSessionManager
{
    /// <summary>
    /// Creates a new RAG session with the specified configuration.
    /// </summary>
    RAGSession CreateSession(RAGSessionConfig? config = null);

    /// <summary>
    /// Retrieves an existing session by ID, or null if not found/expired.
    /// </summary>
    RAGSession? GetSession(string sessionId);

    /// <summary>
    /// Removes a session from the store.
    /// </summary>
    bool RemoveSession(string sessionId);

    /// <summary>
    /// Gets statistics about all active sessions.
    /// </summary>
    (int ActiveSessions, int TotalDocuments, int TotalChunks) GetGlobalStats();
}

/// <summary>
/// Splits documents into chunks using various strategies.
/// Demonstrates parallel processing and configurable algorithms.
/// </summary>
public interface IChunkingService
{
    /// <summary>
    /// Splits a document into chunks using the specified strategy.
    /// </summary>
    /// <param name="document">The document to chunk.</param>
    /// <param name="strategy">The chunking strategy to use.</param>
    /// <param name="chunkSize">Target chunk size in characters.</param>
    /// <param name="overlap">Overlap between chunks in characters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of text chunks.</returns>
    Task<IReadOnlyList<TextChunk>> ChunkDocumentAsync(
        DocumentInfo document,
        ChunkingStrategy strategy,
        int chunkSize,
        int overlap,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Generates embeddings using Gemini API with caching and rate limiting.
/// Demonstrates IMemoryCache usage and SemaphoreSlim for rate limiting.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generates an embedding vector for a single text input.
    /// Results are cached to avoid redundant API calls.
    /// </summary>
    Task<Result<float[]>> GetEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embeddings for multiple texts in parallel with rate limiting.
    /// </summary>
    Task<Result<IReadOnlyList<float[]>>> GetEmbeddingsBatchAsync(
        IReadOnlyList<string> texts,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cache statistics for monitoring.
    /// </summary>
    (int Hits, int Misses) GetCacheStats();

    /// <summary>
    /// Clears the embedding cache.
    /// </summary>
    void ClearCache();
}

/// <summary>
/// In-memory vector store with SIMD-accelerated similarity search.
/// Demonstrates low-level optimization using Vector<T>.
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Adds a chunk with its embedding to the session's vector index.
    /// </summary>
    void AddChunk(RAGSession session, ChunkWithEmbedding chunk);

    /// <summary>
    /// Adds multiple chunks to the session's vector index.
    /// </summary>
    void AddChunks(RAGSession session, IEnumerable<ChunkWithEmbedding> chunks);

    /// <summary>
    /// Performs similarity search to find the most relevant chunks.
    /// Uses SIMD-accelerated cosine similarity.
    /// </summary>
    /// <param name="session">The session containing the vector index.</param>
    /// <param name="queryEmbedding">The query embedding vector.</param>
    /// <param name="topK">Number of results to return.</param>
    /// <param name="minScore">Minimum similarity score threshold.</param>
    /// <returns>Ranked retrieval results.</returns>
    IReadOnlyList<RetrievalResult> Search(
        RAGSession session,
        float[] queryEmbedding,
        int topK,
        float minScore = 0.0f);
}

/// <summary>
/// RAG strategy interface for implementing different retrieval approaches.
/// </summary>
public interface IRAGStrategy
{
    /// <summary>
    /// The strategy type identifier.
    /// </summary>
    RAGStrategy StrategyType { get; }

    /// <summary>
    /// Executes the RAG strategy to retrieve relevant chunks for a query.
    /// </summary>
    /// <param name="session">The RAG session with vector index.</param>
    /// <param name="query">The user's query.</param>
    /// <param name="topK">Number of chunks to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Retrieval results with relevance scores.</returns>
    Task<Result<IReadOnlyList<RetrievalResult>>> RetrieveAsync(
        RAGSession session,
        string query,
        int topK,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Orchestrates the complete RAG workflow with progress reporting and metrics.
/// </summary>
public interface IRAGOrchestrator
{
    /// <summary>
    /// Ingests a document into the session, processing chunks and embeddings.
    /// Reports progress via SSE for real-time UI updates.
    /// </summary>
    IAsyncEnumerable<IngestProgressUpdate> IngestDocumentAsync(
        RAGSession session,
        IngestDocumentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a RAG query with the specified strategy, streaming the response.
    /// </summary>
    IAsyncEnumerable<RAGStreamChunk> QueryAsync(
        RAGSession session,
        RAGQueryRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Document processing pipeline using producer-consumer pattern.
/// Demonstrates Channel<T> for high-throughput parallel processing.
/// </summary>
public interface IDocumentPipeline
{
    /// <summary>
    /// Processes a document through the chunking and embedding pipeline.
    /// Uses Channel<T> producer-consumer pattern for parallel processing.
    /// </summary>
    /// <param name="session">The target session.</param>
    /// <param name="document">The document to process.</param>
    /// <param name="config">Processing configuration.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processing metrics.</returns>
    Task<Result<RAGMetrics>> ProcessDocumentAsync(
        RAGSession session,
        DocumentInfo document,
        RAGSessionConfig config,
        IProgress<IngestProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}
