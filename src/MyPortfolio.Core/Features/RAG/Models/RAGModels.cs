// MyPortfolio.Core/Features/RAG/Models/RAGModels.cs

namespace MyPortfolio.Core.Features.RAG.Models;

/// <summary>
/// Represents a document uploaded by the user for RAG processing.
/// </summary>
public sealed class DocumentInfo
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required string Content { get; init; }
    public required int CharacterCount { get; init; }
    public required DateTime UploadedAt { get; init; }
}

/// <summary>
/// Represents a chunk of text extracted from a document.
/// </summary>
public sealed class TextChunk
{
    public required string Id { get; init; }
    public required string DocumentId { get; init; }
    public required string DocumentName { get; init; }
    public required string Content { get; init; }
    public required int StartIndex { get; init; }
    public required int EndIndex { get; init; }
    public required int ChunkIndex { get; init; }
}

/// <summary>
/// A chunk paired with its embedding vector.
/// </summary>
public sealed class ChunkWithEmbedding
{
    public required TextChunk Chunk { get; init; }
    public required float[] Embedding { get; init; }
}

/// <summary>
/// Result of a similarity search operation.
/// </summary>
public sealed class RetrievalResult
{
    public required TextChunk Chunk { get; init; }
    public required float SimilarityScore { get; init; }
    public required int Rank { get; init; }
}

/// <summary>
/// The final RAG response with answer and citations.
/// </summary>
public sealed class RAGResponse
{
    public required string Answer { get; init; }
    public required IReadOnlyList<Citation> Citations { get; init; }
    public required RAGMetrics Metrics { get; init; }
    public required string StrategyUsed { get; init; }
}

/// <summary>
/// Citation linking answer content to source document chunks.
/// </summary>
public sealed class Citation
{
    public required string DocumentName { get; init; }
    public required string ChunkPreview { get; init; }
    public required float RelevanceScore { get; init; }
    public required int ChunkIndex { get; init; }
}

/// <summary>
/// Performance metrics for RAG operations.
/// </summary>
public sealed class RAGMetrics
{
    public long ChunkingTimeMs { get; set; }
    public long EmbeddingTimeMs { get; set; }
    public long RetrievalTimeMs { get; set; }
    public long GenerationTimeMs { get; set; }
    public long TotalTimeMs { get; set; }
    public int TotalChunks { get; set; }
    public int ChunksRetrieved { get; set; }
    public int EmbeddingCacheHits { get; set; }
    public int EmbeddingCacheMisses { get; set; }
    public int TotalTokensUsed { get; set; }
    public long MemoryUsedBytes { get; set; }
}

/// <summary>
/// Available RAG strategies demonstrating different algorithmic approaches.
/// </summary>
public enum RAGStrategy
{
    /// <summary>
    /// Simple direct similarity search - baseline approach.
    /// </summary>
    Naive = 0,

    /// <summary>
    /// Query expansion with reranking - enhanced relevance.
    /// </summary>
    Semantic = 1,

    /// <summary>
    /// Hypothetical Document Embeddings - advanced technique.
    /// </summary>
    HyDE = 2
}

/// <summary>
/// Available chunking strategies for document processing.
/// </summary>
public enum ChunkingStrategy
{
    /// <summary>
    /// Fixed-size chunks with overlap.
    /// </summary>
    FixedSize = 0,

    /// <summary>
    /// Sentence-aware chunking preserving natural boundaries.
    /// </summary>
    Sentence = 1,

    /// <summary>
    /// Paragraph-based chunking for semantic coherence.
    /// </summary>
    Paragraph = 2
}
