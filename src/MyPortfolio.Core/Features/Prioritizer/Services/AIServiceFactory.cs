using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyPortfolio.Core.Abstractions;

namespace MyPortfolio.Core.Features.Prioritizer.Services;

/// <summary>
/// Factory to resolve AI services by provider ID.
/// This allows dynamic selection of AI providers at runtime.
/// </summary>
public interface IAIServiceFactory
{
    /// <summary>
    /// Gets an AI service by provider ID.
    /// </summary>
    /// <param name="providerId">The provider ID (e.g., "gemini", "openai", "azure-openai", "anthropic", "cohere")</param>
    /// <returns>The corresponding AI service, or null if not found.</returns>
    IAIService? GetService(string providerId);

    /// <summary>
    /// Gets all available provider IDs.
    /// </summary>
    IEnumerable<string> GetAvailableProviders();

    /// <summary>
    /// Checks if a provider is configured and available.
    /// </summary>
    bool IsProviderAvailable(string providerId);
}

/// <summary>
/// Implementation of IAIServiceFactory that resolves services from DI container.
/// </summary>
public class AIServiceFactory : IAIServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AIServiceFactory> _logger;

    // Map of provider IDs to service types
    private static readonly Dictionary<string, Type> ProviderTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "gemini", typeof(GeminiService) },
        { "openai", typeof(OpenAIService) },
        { "azure-openai", typeof(AzureOpenAIService) },
        { "anthropic", typeof(AnthropicService) },
        { "cohere", typeof(CohereService) }
    };

    public AIServiceFactory(IServiceProvider serviceProvider, ILogger<AIServiceFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public IAIService? GetService(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            _logger.LogWarning("Provider ID is null or empty, returning default (Gemini)");
            providerId = "gemini";
        }

        if (!ProviderTypes.TryGetValue(providerId, out var serviceType))
        {
            _logger.LogWarning("Unknown provider ID: {ProviderId}", providerId);
            return null;
        }

        try
        {
            var service = _serviceProvider.GetService(serviceType) as IAIService;
            
            if (service == null)
            {
                _logger.LogWarning("Service not registered for provider: {ProviderId}", providerId);
            }
            else
            {
                _logger.LogDebug("Resolved service for provider: {ProviderId}", providerId);
            }

            return service;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving service for provider: {ProviderId}", providerId);
            return null;
        }
    }

    public IEnumerable<string> GetAvailableProviders()
    {
        return ProviderTypes.Keys;
    }

    public bool IsProviderAvailable(string providerId)
    {
        var service = GetService(providerId);
        return service != null;
    }
}
