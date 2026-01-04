// MyPortfolio.Core/Features/RAG/Services/Strategies/HyDERAGStrategy.cs

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyPortfolio.Core.Features.Prioritizer.Services;
using MyPortfolio.Core.Features.RAG.Abstractions;
using MyPortfolio.Core.Features.RAG.Models;
using MyPortfolio.Core.Shared;

namespace MyPortfolio.Core.Features.RAG.Services.Strategies;

/// <summary>
/// HyDE (Hypothetical Document Embeddings) RAG Strategy.
/// 
/// Based on the paper: "Precise Zero-Shot Dense Retrieval without Relevance Labels"
/// https://arxiv.org/abs/2212.10496
/// 
/// Algorithm:
/// 1. Generate a hypothetical answer to the query (without any context)
/// 2. Embed the hypothetical answer instead of the query
/// 3. Search for chunks similar to the hypothetical answer
/// 4. Generate final answer with retrieved context
/// 
/// Why it works:
/// - Hypothetical answers are in "document space" rather than "query space"
/// - Better semantic alignment between search vector and stored chunks
/// - Especially effective for questions where the answer style differs from query style
/// 
/// Pros: Best retrieval quality for complex questions
/// Cons: Extra LLM call for hypothesis generation, slower
/// </summary>
public sealed class HyDERAGStrategy : IRAGStrategy
{
    private readonly HttpClient _httpClient;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<HyDERAGStrategy> _logger;

    public RAGStrategy StrategyType => RAGStrategy.HyDE;

    private const string ModelId = "gemini-2.5-flash";
    private const string ApiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{ModelId}:generateContent";

    public HyDERAGStrategy(
        HttpClient httpClient,
        IOptions<GeminiOptions> options,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ILogger<HyDERAGStrategy> logger)
    {
        _httpClient = httpClient;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _logger = logger;

        // Configure HTTP client with Gemini API key
        _httpClient.DefaultRequestHeaders.Add("X-Goog-Api-Key", options.Value.ApiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<RetrievalResult>>> RetrieveAsync(
        RAGSession session,
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "HyDERAG: Generating hypothetical answer for: {Query}",
            query.Length > 100 ? query[..100] + "..." : query);

        // Step 1: Generate hypothetical answer
        var hypothesisResult = await GenerateHypothesisAsync(query, cancellationToken);
        if (!hypothesisResult.IsSuccess)
        {
            _logger.LogWarning(
                "HyDERAG: Hypothesis generation failed, falling back to query embedding. Error: {Error}",
                hypothesisResult.Error);
            
            // Fallback: use original query if hypothesis fails
            return await FallbackRetrieveAsync(session, query, topK, cancellationToken);
        }

        var hypothesis = hypothesisResult.Value!;
        _logger.LogDebug("HyDERAG: Generated hypothesis ({Length} chars)", hypothesis.Length);

        // Step 2: Embed the hypothetical answer (not the query!)
        var embeddingResult = await _embeddingService.GetEmbeddingAsync(hypothesis, cancellationToken);
        if (!embeddingResult.IsSuccess)
        {
            _logger.LogWarning(
                "HyDERAG: Hypothesis embedding failed, falling back to query. Error: {Error}",
                embeddingResult.Error);
            return await FallbackRetrieveAsync(session, query, topK, cancellationToken);
        }

        // Step 3: Search using hypothetical answer embedding
        var results = _vectorStore.Search(
            session,
            embeddingResult.Value!,
            topK,
            session.Config.MinSimilarityScore);

        _logger.LogInformation(
            "HyDERAG: Retrieved {Count} chunks using hypothetical document embedding",
            results.Count);

        return Result<IReadOnlyList<RetrievalResult>>.Success(results);
    }

    /// <summary>
    /// Generates a hypothetical answer to the query without any context.
    /// This answer will be embedded and used for similarity search.
    /// </summary>
    private async Task<Result<string>> GenerateHypothesisAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var prompt = $"""
            You are a helpful assistant. Answer the following question with a detailed, 
            informative response. Write as if you are providing content that would appear 
            in a document or knowledge base. Do not mention that you don't have specific context.
            Provide a general, well-structured answer that covers the topic comprehensively.

            Question: {query}

            Answer:
            """;

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
                maxOutputTokens = 500,
                temperature = 0.3 // Lower temperature for more factual hypothesis
            }
        };

        try
        {
            var requestJson = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync(ApiUrl, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return Result<string>.Failure($"Hypothesis generation failed: {response.StatusCode} - {errorBody}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseNode = JsonNode.Parse(responseJson);

            var text = responseNode?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();

            if (string.IsNullOrEmpty(text))
            {
                return Result<string>.Failure("Hypothesis generation returned empty response");
            }

            return Result<string>.Success(text);
        }
        catch (OperationCanceledException)
        {
            return Result<string>.Failure("Hypothesis generation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating hypothesis");
            return Result<string>.Failure($"Hypothesis generation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Fallback to naive retrieval if hypothesis generation fails.
    /// </summary>
    private async Task<Result<IReadOnlyList<RetrievalResult>>> FallbackRetrieveAsync(
        RAGSession session,
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        var embeddingResult = await _embeddingService.GetEmbeddingAsync(query, cancellationToken);
        if (!embeddingResult.IsSuccess)
        {
            return Result<IReadOnlyList<RetrievalResult>>.Failure(
                $"Fallback embedding failed: {embeddingResult.Error}");
        }

        var results = _vectorStore.Search(
            session,
            embeddingResult.Value!,
            topK,
            session.Config.MinSimilarityScore);

        return Result<IReadOnlyList<RetrievalResult>>.Success(results);
    }
}
