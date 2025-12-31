namespace MyPortfolio.Core.Features.Prioritizer.Services;

/// <summary>
/// Configuration options for OpenAI Service.
/// </summary>
public class OpenAIOptions
{
    public const string OpenAI = "OpenAI";

    /// <summary>
    /// OpenAI API Key from https://platform.openai.com/api-keys
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Model to use (e.g., "gpt-4o", "gpt-4-turbo", "gpt-3.5-turbo")
    /// </summary>
    public string Model { get; init; } = "gpt-4o";

    /// <summary>
    /// Maximum tokens for the response.
    /// </summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Temperature for response generation (0.0 to 2.0).
    /// </summary>
    public float Temperature { get; init; } = 0.7f;
}

/// <summary>
/// Configuration options for Azure OpenAI Service.
/// </summary>
public class AzureOpenAIOptions
{
    public const string AzureOpenAI = "AzureOpenAI";

    /// <summary>
    /// Azure OpenAI API Key.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Azure OpenAI Endpoint URL (e.g., "https://your-resource.openai.azure.com/")
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// Deployment name in Azure OpenAI Studio.
    /// </summary>
    public string? DeploymentName { get; init; }

    /// <summary>
    /// Maximum tokens for the response.
    /// </summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Temperature for response generation (0.0 to 2.0).
    /// </summary>
    public float Temperature { get; init; } = 0.7f;
}

/// <summary>
/// Configuration options for Anthropic Claude Service.
/// </summary>
public class AnthropicOptions
{
    public const string Anthropic = "Anthropic";

    /// <summary>
    /// Anthropic API Key from https://console.anthropic.com/
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Model to use (e.g., "claude-3-opus-20240229", "claude-3-sonnet-20240229", "claude-3-haiku-20240307")
    /// </summary>
    public string Model { get; init; } = "claude-3-5-sonnet-20241022";

    /// <summary>
    /// Maximum tokens for the response.
    /// </summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Temperature for response generation (0.0 to 1.0).
    /// </summary>
    public float Temperature { get; init; } = 0.7f;

    /// <summary>
    /// API endpoint URL.
    /// </summary>
    public string ApiUrl { get; init; } = "https://api.anthropic.com/v1/messages";
}

/// <summary>
/// Configuration options for Cohere Service.
/// </summary>
public class CohereOptions
{
    public const string Cohere = "Cohere";

    /// <summary>
    /// Cohere API Key from https://dashboard.cohere.com/api-keys
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Model to use (e.g., "command-r-plus", "command-r", "command")
    /// </summary>
    public string Model { get; init; } = "command-r-plus";

    /// <summary>
    /// Maximum tokens for the response.
    /// </summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Temperature for response generation (0.0 to 1.0).
    /// </summary>
    public float Temperature { get; init; } = 0.7f;

    /// <summary>
    /// API endpoint URL.
    /// </summary>
    public string ApiUrl { get; init; } = "https://api.cohere.ai/v1/chat";
}
