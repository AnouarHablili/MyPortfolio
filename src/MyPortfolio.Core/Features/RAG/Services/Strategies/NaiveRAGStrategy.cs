// MyPortfolio.Core/Features/RAG/Services/Strategies/NaiveRAGStrategy.cs

using Microsoft.Extensions.Logging;
using MyPortfolio.Core.Features.RAG.Abstractions;
using MyPortfolio.Core.Features.RAG.Models;
using MyPortfolio.Core.Shared;

namespace MyPortfolio.Core.Features.RAG.Services.Strategies;

/// <summary>
/// Naive RAG Strategy - Direct similarity search baseline.
/// 
/// Algorithm:
/// 1. Embed the user query
/// 2. Find top-K most similar chunks via cosine similarity
/// 3. Return ranked results
/// 
/// Pros: Simple, fast, predictable
/// Cons: Query-document vocabulary mismatch can hurt relevance
/// </summary>
public sealed class NaiveRAGStrategy : IRAGStrategy
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<NaiveRAGStrategy> _logger;

    public RAGStrategy StrategyType => RAGStrategy.Naive;

    public NaiveRAGStrategy(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ILogger<NaiveRAGStrategy> logger)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<RetrievalResult>>> RetrieveAsync(
        RAGSession session,
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "NaiveRAG: Retrieving top {TopK} chunks for query: {Query}",
            topK,
            query.Length > 100 ? query[..100] + "..." : query);

        // Step 1: Embed the query
        var embeddingResult = await _embeddingService.GetEmbeddingAsync(query, cancellationToken);
        if (!embeddingResult.IsSuccess)
        {
            return Result<IReadOnlyList<RetrievalResult>>.Failure(
                $"Failed to embed query: {embeddingResult.Error}");
        }

        // Step 2: Search vector store
        var results = _vectorStore.Search(
            session,
            embeddingResult.Value!,
            topK,
            session.Config.MinSimilarityScore);

        _logger.LogInformation(
            "NaiveRAG: Retrieved {Count} chunks above threshold {MinScore:F2}",
            results.Count,
            session.Config.MinSimilarityScore);

        return Result<IReadOnlyList<RetrievalResult>>.Success(results);
    }
}
