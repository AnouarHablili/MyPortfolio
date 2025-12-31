// MyPortfolio.Core/Extensions/ServiceCollectionExtensions.cs

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using MyPortfolio.Core.Abstractions;
using MyPortfolio.Core.Features.Prioritizer.Services;

namespace MyPortfolio.Core.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all AI services and the AI Service Factory.
    /// Services without API keys configured will gracefully handle requests with appropriate error messages.
    /// </summary>
    /// <param name="services">The service collection to add the services to.</param>
    /// <param name="configuration">The application configuration source (e.g., appsettings.json).</param>
    /// <returns>The modified service collection.</returns>
    public static IServiceCollection AddAIServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register Gemini (primary service, required)
        services.AddGeminiService(configuration);

        // Register OpenAI (optional - will gracefully fail if not configured)
        services.AddOpenAIService(configuration);

        // Register Azure OpenAI (optional)
        services.AddAzureOpenAIService(configuration);

        // Register Anthropic Claude (optional)
        services.AddAnthropicService(configuration);

        // Register Cohere (optional)
        services.AddCohereService(configuration);

        // Register the AI Service Factory
        services.AddSingleton<IAIServiceFactory, AIServiceFactory>();

        return services;
    }

    /// <summary>
    /// Registers the Gemini AI Prioritization Service (IAIService) and configures its dependencies.
    /// This includes binding GeminiOptions and setting up the HttpClient using IHttpClientFactory.
    /// </summary>
    public static IServiceCollection AddGeminiService(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure the Options Model
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.Gemini));

        // Validate options at startup
        services.AddOptions<GeminiOptions>()
                .Bind(configuration.GetSection(GeminiOptions.Gemini))
                .Validate(options => !string.IsNullOrEmpty(options.ApiKey), "The Gemini:ApiKey setting cannot be empty.")
                .ValidateOnStart();

        // Register as both the interface and concrete type (for factory resolution)
        services.AddHttpClient<GeminiService>();
        services.AddHttpClient<IAIService, GeminiService>();

        return services;
    }

    /// <summary>
    /// Registers the OpenAI Service.
    /// If API key is not configured, the service will return appropriate error messages.
    /// </summary>
    public static IServiceCollection AddOpenAIService(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure options (may be empty)
        services.Configure<OpenAIOptions>(configuration.GetSection(OpenAIOptions.OpenAI));

        // Register with HttpClient using IHttpClientFactory pattern
        services.AddHttpClient<OpenAIService>();

        return services;
    }

    /// <summary>
    /// Registers the Azure OpenAI Service.
    /// If not fully configured, the service will return appropriate error messages.
    /// </summary>
    public static IServiceCollection AddAzureOpenAIService(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure options (may be empty)
        services.Configure<AzureOpenAIOptions>(configuration.GetSection(AzureOpenAIOptions.AzureOpenAI));

        // Register with HttpClient using IHttpClientFactory pattern
        services.AddHttpClient<AzureOpenAIService>();

        return services;
    }

    /// <summary>
    /// Registers the Anthropic Claude Service.
    /// If API key is not configured, the service will return appropriate error messages.
    /// </summary>
    public static IServiceCollection AddAnthropicService(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure options (may be empty)
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.Anthropic));

        // Register with HttpClient
        services.AddHttpClient<AnthropicService>();

        return services;
    }

    /// <summary>
    /// Registers the Cohere Service.
    /// If API key is not configured, the service will return appropriate error messages.
    /// </summary>
    public static IServiceCollection AddCohereService(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure options (may be empty)
        services.Configure<CohereOptions>(configuration.GetSection(CohereOptions.Cohere));

        // Register with HttpClient
        services.AddHttpClient<CohereService>();

        return services;
    }
}