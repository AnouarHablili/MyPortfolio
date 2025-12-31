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
/// OpenAI implementation of IAIService using direct HTTP calls.
/// Supports GPT-4o, GPT-4-Turbo, and GPT-3.5-Turbo models.
/// </summary>
public class OpenAIService : IAIService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIService> _logger;
    private readonly OpenAIOptions _options;
    private readonly bool _isConfigured;

    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

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

Return ONLY the JSON object, no other text.";

    public OpenAIService(
        HttpClient httpClient,
        IOptions<OpenAIOptions> options,
        ILogger<OpenAIService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            _logger.LogWarning("OpenAI API key is not configured. Service will not function.");
            _isConfigured = false;
            return;
        }

        _isConfigured = true;

        // Set up headers for OpenAI API
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogInformation("OpenAIService initialized with model {Model}", _options.Model);
    }

    public async Task<Result<PrioritizationResponse>> PrioritizeGoalAsync(string goal, CancellationToken cancellationToken = default)
    {
        if (!_isConfigured)
        {
            return Result<PrioritizationResponse>.Failure("OpenAI service is not configured. API key is missing.");
        }

        _logger.LogInformation("Starting goal prioritization with OpenAI for goal: {Goal}", goal);

        try
        {
            // Build the OpenAI Chat Completion request
            var requestBody = new
            {
                model = _options.Model,
                messages = new[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = $"Please prioritize and break down this goal: {goal}" }
                },
                max_tokens = _options.MaxTokens,
                temperature = _options.Temperature,
                response_format = new { type = "json_object" }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(ApiUrl, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("OpenAI API error: {StatusCode} - {Error}", response.StatusCode, errorBody);
                return Result<PrioritizationResponse>.Failure($"OpenAI API error: {response.StatusCode}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("OpenAI response received, length: {Length}", responseBody.Length);

            // Parse OpenAI response format
            var responseJson = JsonNode.Parse(responseBody);
            var textContent = responseJson?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();

            if (string.IsNullOrEmpty(textContent))
            {
                _logger.LogWarning("OpenAI returned an empty response");
                return Result<PrioritizationResponse>.Failure("OpenAI returned an empty response.");
            }

            // Parse the JSON response
            var result = JsonSerializer.Deserialize<PrioritizationResponse>(
                textContent,
                ApiJsonContext.Default.PrioritizationResponse);

            if (result == null)
            {
                _logger.LogError("Failed to deserialize OpenAI response");
                return Result<PrioritizationResponse>.Failure("Failed to parse OpenAI response.");
            }

            // Extract usage metadata
            var promptTokens = responseJson?["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0;
            var completionTokens = responseJson?["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0;
            var totalTokens = responseJson?["usage"]?["total_tokens"]?.GetValue<int>() ?? 0;

            result.UsageMetadata = new UsageMetadata
            {
                PromptTokenCount = promptTokens,
                CandidatesTokenCount = completionTokens,
                TotalTokenCount = totalTokens
            };

            _logger.LogInformation(
                "Goal prioritization completed with OpenAI. Tasks: {TaskCount}, Tokens: {TotalTokens}",
                result.TaskItems.Count(),
                totalTokens);

            return Result<PrioritizationResponse>.Success(result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("OpenAI request was canceled");
            return Result<PrioritizationResponse>.Failure("Operation was canceled.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse OpenAI response as JSON");
            return Result<PrioritizationResponse>.Failure($"Failed to parse response: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OpenAI prioritization");
            return Result<PrioritizationResponse>.Failure($"OpenAI error: {ex.Message}");
        }
    }
}
