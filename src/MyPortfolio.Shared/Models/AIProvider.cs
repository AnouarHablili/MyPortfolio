namespace MyPortfolio.Shared.Models;

/// <summary>
/// Represents an AI provider that can be used in POCs.
/// </summary>
public record AIProvider
{
    /// <summary>Unique identifier for the provider.</summary>
    public required string Id { get; init; }

    /// <summary>Display name of the provider.</summary>
    public required string Name { get; init; }

    /// <summary>Short description of the provider.</summary>
    public string? Description { get; init; }

    /// <summary>Bootstrap icon class for the provider.</summary>
    public string IconClass { get; init; } = "bi-robot";

    /// <summary>CSS class for the provider badge color.</summary>
    public string BadgeClass { get; init; } = "bg-secondary";

    /// <summary>Whether this provider is currently enabled/available.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>Reason why the provider is disabled (shown in tooltip).</summary>
    public string? DisabledReason { get; init; }

    /// <summary>URL to the provider's website/documentation.</summary>
    public string? ProviderUrl { get; init; }
}

/// <summary>
/// Provides a registry of available AI providers.
/// </summary>
public static class AIProviders
{
    public static AIProvider Gemini { get; } = new AIProvider
    {
        Id = "gemini",
        Name = "Gemini",
        Description = "Gemini 2.5 Flash - Fast and capable multimodal AI",
        IconClass = "bi-stars",
        BadgeClass = "bg-primary",
        IsEnabled = true,
        ProviderUrl = "https://ai.google.dev/"
    };

    public static AIProvider AzureOpenAI { get; } = new AIProvider
    {
        Id = "azure-openai",
        Name = "Azure OpenAI",
        Description = "GPT-4 and GPT-3.5 Turbo via Azure",
        IconClass = "bi-microsoft",
        BadgeClass = "bg-info",
        IsEnabled = false,
        DisabledReason = "API key not configured",
        ProviderUrl = "https://azure.microsoft.com/en-us/products/ai-services/openai-service"
    };

    public static AIProvider OpenAI { get; } = new AIProvider
    {
        Id = "openai",
        Name = "OpenAI",
        Description = "GPT-4, GPT-3.5 Turbo, and DALL-E",
        IconClass = "bi-chat-dots-fill",
        BadgeClass = "bg-success",
        IsEnabled = false,
        DisabledReason = "API key not configured",
        ProviderUrl = "https://openai.com/"
    };

    public static AIProvider Anthropic { get; } = new AIProvider
    {
        Id = "anthropic",
        Name = "Claude",
        Description = "Claude 3 Opus, Sonnet, and Haiku",
        IconClass = "bi-brilliance",
        BadgeClass = "bg-warning text-dark",
        IsEnabled = false,
        DisabledReason = "API key not configured",
        ProviderUrl = "https://www.anthropic.com/"
    };

    public static AIProvider Cohere { get; } = new AIProvider
    {
        Id = "cohere",
        Name = "Cohere",
        Description = "Command and Embed models",
        IconClass = "bi-diagram-3-fill",
        BadgeClass = "bg-danger",
        IsEnabled = false,
        DisabledReason = "API key not configured",
        ProviderUrl = "https://cohere.com/"
    };

    /// <summary>
    /// Gets all available AI providers.
    /// </summary>
    public static IReadOnlyList<AIProvider> All { get; } = new[]
    {
        Gemini,
        AzureOpenAI,
        OpenAI,
        Anthropic,
        Cohere
    };

    /// <summary>
    /// Gets only enabled AI providers.
    /// </summary>
    public static IReadOnlyList<AIProvider> Enabled => All.Where(p => p.IsEnabled).ToList();

    /// <summary>
    /// Gets a provider by ID.
    /// </summary>
    public static AIProvider? GetById(string id) => All.FirstOrDefault(p => p.Id == id);
}
