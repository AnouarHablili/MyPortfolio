// MyPortfolio.Core/Features/RAG/Models/RAGRequests.cs

namespace MyPortfolio.Core.Features.RAG.Models;

/// <summary>
/// Request to create a new RAG session.
/// </summary>
public sealed record CreateSessionRequest
{
    /// <summary>
    /// Optional custom configuration for the session.
    /// If not provided, uses default demo limits.
    /// </summary>
    public RAGSessionConfig? Config { get; init; }
}

/// <summary>
/// Response after creating a RAG session.
/// </summary>
public sealed record CreateSessionResponse
{
    public required string SessionId { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required int MaxDocuments { get; init; }
    public required int MaxFileSizeBytes { get; init; }
}

/// <summary>
/// Request to ingest a document into the RAG session.
/// </summary>
public sealed record IngestDocumentRequest
{
    public required string SessionId { get; init; }
    public required string FileName { get; init; }
    public required string Content { get; init; }
    public ChunkingStrategy? ChunkingStrategy { get; init; }
}

/// <summary>
/// Progress update during document ingestion.
/// </summary>
public sealed record IngestProgressUpdate
{
    public required string Phase { get; init; }
    public required int CurrentStep { get; init; }
    public required int TotalSteps { get; init; }
    public required string Message { get; init; }
    public double? PercentComplete { get; init; }
}

/// <summary>
/// Response after document ingestion completes.
/// </summary>
public sealed record IngestDocumentResponse
{
    public required string DocumentId { get; init; }
    public required int ChunksCreated { get; init; }
    public required RAGMetrics Metrics { get; init; }
}

/// <summary>
/// Request to query the RAG system.
/// </summary>
public sealed record RAGQueryRequest
{
    public required string SessionId { get; init; }
    public required string Query { get; init; }
    public RAGStrategy? Strategy { get; init; }
    public int? TopK { get; init; }
}

/// <summary>
/// Streaming chunk during RAG response generation.
/// </summary>
public sealed record RAGStreamChunk
{
    public required string Type { get; init; } // "retrieval", "generation", "citation", "metrics", "done"
    public string? Content { get; init; }
    public IReadOnlyList<RetrievalResult>? RetrievedChunks { get; init; }
    public Citation? Citation { get; init; }
    public RAGMetrics? Metrics { get; init; }
}

/// <summary>
/// Session statistics for monitoring/debugging.
/// </summary>
public sealed record SessionStatsResponse
{
    public required string SessionId { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required int DocumentCount { get; init; }
    public required int ChunkCount { get; init; }
    public required RAGMetrics SessionMetrics { get; init; }
    public required IReadOnlyList<DocumentSummary> Documents { get; init; }
}

/// <summary>
/// Summary of a document in the session.
/// </summary>
public sealed record DocumentSummary
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required int CharacterCount { get; init; }
    public required int ChunkCount { get; init; }
    public required DateTime UploadedAt { get; init; }
}
