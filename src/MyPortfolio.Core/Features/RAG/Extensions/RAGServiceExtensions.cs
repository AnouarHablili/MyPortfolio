// MyPortfolio.Core/Features/RAG/Extensions/RAGServiceExtensions.cs

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyPortfolio.Core.Features.RAG.Abstractions;
using MyPortfolio.Core.Features.RAG.Services;
using MyPortfolio.Core.Features.RAG.Services.Strategies;

namespace MyPortfolio.Core.Features.RAG.Extensions;

/// <summary>
/// Extension methods for registering RAG services.
/// </summary>
public static class RAGServiceExtensions
{
    /// <summary>
    /// Registers all RAG services with the dependency injection container.
    /// </summary>
    public static IServiceCollection AddRAGServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register embedding options (uses Gemini API key)
        services.Configure<GeminiEmbeddingOptions>(
            configuration.GetSection(GeminiEmbeddingOptions.Section));

        // Core services (singletons for session management and caching)
        services.AddSingleton<IRAGSessionManager, RAGSessionManager>();
        services.AddSingleton<IVectorStore, VectorStore>();

        // Processing services (scoped for per-request lifetime)
        services.AddScoped<IChunkingService, ChunkingService>();
        
        // Embedding service (with HttpClient)
        services.AddHttpClient<IEmbeddingService, GeminiEmbeddingService>();

        // Document pipeline
        services.AddScoped<IDocumentPipeline, DocumentPipeline>();

        // RAG strategies
        services.AddScoped<NaiveRAGStrategy>();
        services.AddScoped<SemanticRAGStrategy>();
        services.AddHttpClient<HyDERAGStrategy>();

        // Strategy factory
        services.AddScoped<IRAGStrategyFactory, RAGStrategyFactory>();

        // Orchestrator (with HttpClient for generation)
        services.AddHttpClient<IRAGOrchestrator, RAGOrchestrator>();

        return services;
    }
}
