// MyPortfolio.Core/Features/RAG/Services/Strategies/SemanticRAGStrategy.cs

using Microsoft.Extensions.Logging;
using MyPortfolio.Core.Features.RAG.Abstractions;
using MyPortfolio.Core.Features.RAG.Models;
using MyPortfolio.Core.Shared;

namespace MyPortfolio.Core.Features.RAG.Services.Strategies;

/// <summary>
/// Semantic RAG Strategy - Query expansion with reranking.
/// 
/// Algorithm:
/// 1. Expand query into multiple variations (synonyms, related concepts)
/// 2. Run parallel searches for each variation
/// 3. Merge and deduplicate results
/// 4. Rerank by aggregate relevance score
/// 
/// Pros: Better recall, handles vocabulary mismatch
/// Cons: More API calls (embeddings), slightly slower
/// </summary>
public sealed class SemanticRAGStrategy : IRAGStrategy
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<SemanticRAGStrategy> _logger;

    public RAGStrategy StrategyType => RAGStrategy.Semantic;

    // Query expansion patterns
    private static readonly string[] ExpansionTemplates =
    [
        "{0}",                                    // Original query
        "What is {0}?",                           // Definition form
        "How does {0} work?",                     // Explanation form
        "Examples of {0}",                        // Example form
    ];

    public SemanticRAGStrategy(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ILogger<SemanticRAGStrategy> logger)
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
            "SemanticRAG: Retrieving with query expansion for: {Query}",
            query.Length > 100 ? query[..100] + "..." : query);

        // Step 1: Expand query into variations
        var expandedQueries = ExpandQuery(query);
        _logger.LogDebug("SemanticRAG: Expanded to {Count} query variations", expandedQueries.Count);

        // Step 2: Embed all query variations in parallel
        var embeddingTasks = expandedQueries
            .Select(q => _embeddingService.GetEmbeddingAsync(q, cancellationToken))
            .ToList();

        var embeddingResults = await Task.WhenAll(embeddingTasks);

        // Check for failures
        var failedCount = embeddingResults.Count(r => !r.IsSuccess);
        if (failedCount == embeddingResults.Length)
        {
            return Result<IReadOnlyList<RetrievalResult>>.Failure(
                "All query embedding requests failed");
        }

        if (failedCount > 0)
        {
            _logger.LogWarning(
                "SemanticRAG: {FailedCount}/{TotalCount} query embeddings failed",
                failedCount,
                embeddingResults.Length);
        }

        // Step 3: Search with each embedding and collect results
        var allResults = new Dictionary<string, (RetrievalResult Result, float MaxScore, int HitCount)>();

        foreach (var embedding in embeddingResults.Where(r => r.IsSuccess))
        {
            var searchResults = _vectorStore.Search(
                session,
                embedding.Value!,
                topK * 2, // Fetch more candidates for reranking
                session.Config.MinSimilarityScore * 0.8f); // Lower threshold for expansion

            foreach (var result in searchResults)
            {
                var key = result.Chunk.Id;
                if (allResults.TryGetValue(key, out var existing))
                {
                    // Update with max score and increment hit count
                    allResults[key] = (
                        result,
                        Math.Max(existing.MaxScore, result.SimilarityScore),
                        existing.HitCount + 1
                    );
                }
                else
                {
                    allResults[key] = (result, result.SimilarityScore, 1);
                }
            }
        }

        // Step 4: Rerank by combined score (max similarity + hit frequency bonus)
        var rerankedResults = allResults.Values
            .Select(x => new RetrievalResult
            {
                Chunk = x.Result.Chunk,
                // Combined score: max similarity + bonus for appearing in multiple queries
                SimilarityScore = x.MaxScore + (x.HitCount - 1) * 0.05f,
                Rank = 0 // Will be set after sorting
            })
            .OrderByDescending(r => r.SimilarityScore)
            .Take(topK)
            .Select((r, i) => new RetrievalResult
            {
                Chunk = r.Chunk,
                SimilarityScore = r.SimilarityScore,
                Rank = i + 1
            })
            .ToList();

        _logger.LogInformation(
            "SemanticRAG: Retrieved {Count} chunks after expansion and reranking",
            rerankedResults.Count);

        return Result<IReadOnlyList<RetrievalResult>>.Success(rerankedResults);
    }

    /// <summary>
    /// Expands a query into multiple variations for better recall.
    /// </summary>
    private static List<string> ExpandQuery(string query)
    {
        var expanded = new List<string>();
        var normalizedQuery = query.Trim();

        foreach (var template in ExpansionTemplates)
        {
            var variation = string.Format(template, normalizedQuery);
            if (!expanded.Contains(variation, StringComparer.OrdinalIgnoreCase))
            {
                expanded.Add(variation);
            }
        }

        return expanded;
    }
}
