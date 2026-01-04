// MyPortfolio.Core/Features/RAG/Models/RAGSession.cs

using System.Collections.Concurrent;

namespace MyPortfolio.Core.Features.RAG.Models;

/// <summary>
/// Represents an in-memory RAG session with document store and vector index.
/// Session-based architecture eliminates database costs on Fly.io.
/// </summary>
public sealed class RAGSession
{
    public string SessionId { get; }
    public DateTime CreatedAt { get; }
    public DateTime ExpiresAt { get; private set; }
    
    /// <summary>
    /// Thread-safe collection of uploaded documents.
    /// </summary>
    public ConcurrentBag<DocumentInfo> Documents { get; } = [];
    
    /// <summary>
    /// Thread-safe collection of chunks with embeddings (vector index).
    /// </summary>
    public ConcurrentBag<ChunkWithEmbedding> VectorIndex { get; } = [];
    
    /// <summary>
    /// Aggregated metrics across all operations in this session.
    /// </summary>
    public RAGMetrics SessionMetrics { get; } = new();
    
    /// <summary>
    /// Configuration for this session.
    /// </summary>
    public RAGSessionConfig Config { get; }

    public RAGSession(string sessionId, RAGSessionConfig config)
    {
        SessionId = sessionId;
        Config = config;
        CreatedAt = DateTime.UtcNow;
        ExpiresAt = CreatedAt.Add(config.SessionTTL);
    }

    /// <summary>
    /// Extends the session expiration (sliding window).
    /// </summary>
    public void Touch()
    {
        ExpiresAt = DateTime.UtcNow.Add(Config.SessionTTL);
    }

    /// <summary>
    /// Checks if the session has expired.
    /// </summary>
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;

    /// <summary>
    /// Gets the total number of documents in the session.
    /// </summary>
    public int DocumentCount => Documents.Count;

    /// <summary>
    /// Gets the total number of chunks in the vector index.
    /// </summary>
    public int ChunkCount => VectorIndex.Count;
}

/// <summary>
/// Configuration options for RAG sessions.
/// </summary>
public sealed record RAGSessionConfig
{
    /// <summary>
    /// Time-to-live for sessions. Default: 15 minutes.
    /// </summary>
    public TimeSpan SessionTTL { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Maximum number of documents per session. Default: 2 (demo limit).
    /// </summary>
    public int MaxDocuments { get; init; } = 2;

    /// <summary>
    /// Maximum file size in bytes. Default: 50KB (demo limit).
    /// </summary>
    public int MaxFileSizeBytes { get; init; } = 50 * 1024;

    /// <summary>
    /// Chunk size in characters. Default: 512.
    /// </summary>
    public int ChunkSize { get; init; } = 512;

    /// <summary>
    /// Overlap between chunks in characters. Default: 50.
    /// </summary>
    public int ChunkOverlap { get; init; } = 50;

    /// <summary>
    /// Number of top results to retrieve. Default: 5.
    /// </summary>
    public int TopK { get; init; } = 5;

    /// <summary>
    /// Minimum similarity score to include in results. Default: 0.3.
    /// </summary>
    public float MinSimilarityScore { get; init; } = 0.3f;

    /// <summary>
    /// Default RAG strategy. Default: Naive.
    /// </summary>
    public RAGStrategy DefaultStrategy { get; init; } = RAGStrategy.Naive;

    /// <summary>
    /// Default chunking strategy. Default: FixedSize.
    /// </summary>
    public ChunkingStrategy DefaultChunkingStrategy { get; init; } = ChunkingStrategy.FixedSize;

    /// <summary>
    /// Maximum concurrent embedding requests. Default: 5.
    /// </summary>
    public int MaxConcurrentEmbeddings { get; init; } = 5;
}
