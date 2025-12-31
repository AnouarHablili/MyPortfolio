using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyPortfolio.Core.Abstractions;
using MyPortfolio.Core.Features.Prioritizer.Models;
using MyPortfolio.Core.Shared;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MyPortfolio.Core.Features.Prioritizer.Services;

/// <summary>
/// Anthropic Claude implementation of IAIService.
/// Supports Claude 3 Opus, Sonnet, and Haiku models.
/// Uses direct HTTP calls to the Anthropic API.
/// </summary>
public class AnthropicService : IAIService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AnthropicService> _logger;
    private readonly AnthropicOptions _options;
    private readonly bool _isConfigured;

    private const string SystemPrompt = @"You are an expert project manager and AI task prioritizer. Your job is to take a high-level goal and break it down into a sequence of actionable tasks, prioritizing them by rank (1 being most critical).

For every task, you MUST provide:
- A rank (integer, 1 being highest priority)
- A clear task title
- A detailed reasoning chain explaining why that task has that specific rank
- An estimated time/complexity

IMPORTANT: You MUST return your response as a valid JSON object with this exact structure:
{
    ""tasks"": [
        {
            ""rank"": 1,
            ""taskTitle"": ""Task name"",
            ""reasoningChain"": ""Detailed explanation"",
            ""estimate"": ""Time estimate""
        }
    ],
    ""executiveSummary"": ""Overall strategy summary""
}

Return ONLY the JSON object, no other text or markdown.";

    public AnthropicService(
        HttpClient httpClient,
        IOptions<AnthropicOptions> options,
        ILogger<AnthropicService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            _logger.LogWarning("Anthropic API key is not configured. Service will not function.");
            _isConfigured = false;
            return;
        }

        _isConfigured = true;

        // Set up headers for Anthropic API
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogInformation("AnthropicService initialized with model {Model}", _options.Model);
    }

    public async Task<Result<PrioritizationResponse>> PrioritizeGoalAsync(string goal, CancellationToken cancellationToken = default)
    {
        if (!_isConfigured)
        {
            return Result<PrioritizationResponse>.Failure("Anthropic service is not configured. API key is missing.");
        }

        _logger.LogInformation("Starting goal prioritization with Anthropic Claude for goal: {Goal}", goal);

        try
        {
            // Build the Anthropic API request
            var requestBody = new
            {
                model = _options.Model,
                max_tokens = _options.MaxTokens,
                temperature = _options.Temperature,
                system = SystemPrompt,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = $"Please prioritize and break down this goal: {goal}"
                    }
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_options.ApiUrl, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Anthropic API error: {StatusCode} - {Error}", response.StatusCode, errorBody);
                return Result<PrioritizationResponse>.Failure($"Anthropic API error: {response.StatusCode}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("Anthropic response received, length: {Length}", responseBody.Length);

            // Parse Anthropic response format
            var responseJson = JsonNode.Parse(responseBody);
            var textContent = responseJson?["content"]?[0]?["text"]?.GetValue<string>();

            if (string.IsNullOrEmpty(textContent))
            {
                _logger.LogWarning("Anthropic returned an empty response");
                return Result<PrioritizationResponse>.Failure("Anthropic returned an empty response.");
            }

            // Clean up any markdown code blocks if present
            textContent = CleanJsonResponse(textContent);

            // Parse the JSON response
            var result = JsonSerializer.Deserialize<PrioritizationResponse>(
                textContent,
                ApiJsonContext.Default.PrioritizationResponse);

            if (result == null)
            {
                _logger.LogError("Failed to deserialize Anthropic response");
                return Result<PrioritizationResponse>.Failure("Failed to parse Anthropic response.");
            }

            // Extract usage metadata
            var inputTokens = responseJson?["usage"]?["input_tokens"]?.GetValue<int>() ?? 0;
            var outputTokens = responseJson?["usage"]?["output_tokens"]?.GetValue<int>() ?? 0;

            result.UsageMetadata = new UsageMetadata
            {
                PromptTokenCount = inputTokens,
                CandidatesTokenCount = outputTokens,
                TotalTokenCount = inputTokens + outputTokens
            };

            _logger.LogInformation(
                "Goal prioritization completed with Anthropic. Tasks: {TaskCount}, Tokens: {TotalTokens}",
                result.TaskItems.Count(),
                result.UsageMetadata.TotalTokenCount);

            return Result<PrioritizationResponse>.Success(result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Anthropic request was canceled");
            return Result<PrioritizationResponse>.Failure("Operation was canceled.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Anthropic response as JSON");
            return Result<PrioritizationResponse>.Failure($"Failed to parse response: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Anthropic prioritization");
            return Result<PrioritizationResponse>.Failure($"Anthropic error: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes markdown code blocks from JSON response if present.
    /// </summary>
    private static string CleanJsonResponse(string response)
    {
        var trimmed = response.Trim();
        
        // Remove ```json ... ``` wrapper
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(7);
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed.Substring(3);
        }

        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 3);
        }

        return trimmed.Trim();
    }
}
