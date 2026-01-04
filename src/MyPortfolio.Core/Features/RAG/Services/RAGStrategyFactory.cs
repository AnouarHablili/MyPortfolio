// MyPortfolio.Core/Features/RAG/Services/RAGStrategyFactory.cs

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyPortfolio.Core.Features.RAG.Abstractions;
using MyPortfolio.Core.Features.RAG.Models;
using MyPortfolio.Core.Features.RAG.Services.Strategies;

namespace MyPortfolio.Core.Features.RAG.Services;

/// <summary>
/// Factory for creating RAG strategy instances.
/// Demonstrates the Factory pattern for strategy selection.
/// </summary>
public interface IRAGStrategyFactory
{
    /// <summary>
    /// Gets the appropriate RAG strategy implementation.
    /// </summary>
    IRAGStrategy GetStrategy(RAGStrategy strategy);

    /// <summary>
    /// Gets all available strategies.
    /// </summary>
    IReadOnlyList<RAGStrategy> GetAvailableStrategies();
}

public sealed class RAGStrategyFactory : IRAGStrategyFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RAGStrategyFactory> _logger;

    public RAGStrategyFactory(
        IServiceProvider serviceProvider,
        ILogger<RAGStrategyFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public IRAGStrategy GetStrategy(RAGStrategy strategy)
    {
        _logger.LogDebug("Resolving RAG strategy: {Strategy}", strategy);

        IRAGStrategy strategyImpl = strategy switch
        {
            RAGStrategy.Naive => _serviceProvider.GetRequiredService<NaiveRAGStrategy>(),
            RAGStrategy.Semantic => _serviceProvider.GetRequiredService<SemanticRAGStrategy>(),
            RAGStrategy.HyDE => _serviceProvider.GetRequiredService<HyDERAGStrategy>(),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), $"Unknown strategy: {strategy}")
        };

        _logger.LogInformation("Using RAG strategy: {Strategy}", strategy);
        return strategyImpl;
    }

    /// <inheritdoc/>
    public IReadOnlyList<RAGStrategy> GetAvailableStrategies()
    {
        return
        [
            RAGStrategy.Naive,
            RAGStrategy.Semantic,
            RAGStrategy.HyDE
        ];
    }
}
