// MyPortfolio.Core/Extensions/ServiceCollectionExtensions.cs

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using MyPortfolio.Core.Abstractions;
using MyPortfolio.Core.Features.Prioritizer.Services;
using System.Net.Http.Headers;
using System;

namespace MyPortfolio.Core.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Gemini AI Prioritization Service (IAIService) and configures its dependencies.
    /// This includes binding GeminiOptions and setting up the HttpClient using IHttpClientFactory.
    /// </summary>
    /// <param name="services">The service collection to add the services to.</param>
    /// <param name="configuration">The application configuration source (e.g., appsettings.json).</param>
    /// <returns>The modified service collection.</returns>
    public static IServiceCollection AddGeminiService(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Configure the Options Model
        // Maps the "Gemini" section (e.g., {"Gemini": {"ApiKey": "..."}}) to the GeminiOptions class.
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.Gemini));

        // Optional: Ensure the options are valid at startup (good practice)
        services.AddOptions<GeminiOptions>()
                .Bind(configuration.GetSection(GeminiOptions.Gemini))
                // Note: You might need to add a NuGet package reference to System.ComponentModel.DataAnnotations 
                // for ValidateDataAnnotations() if you start using [Required] attributes on GeminiOptions.
                // For now, we omit it to keep dependencies minimal.
                .Validate(options => !string.IsNullOrEmpty(options.ApiKey), "The GeminiOptions:ApiKey setting cannot be empty.")
                .ValidateOnStart();

        // 2. Register the Service using IHttpClientFactory
        // AddHttpClient manages the HttpClient lifetime (prevents socket exhaustion) 
        // and injects it into the GeminiService constructor.
        services.AddHttpClient<IAIService, GeminiService>();

        return services;
    }
}