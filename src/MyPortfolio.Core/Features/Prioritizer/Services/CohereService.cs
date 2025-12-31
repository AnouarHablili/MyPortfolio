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
/// Cohere implementation of IAIService.
/// Supports Command R+, Command R, and Command models.
/// Uses direct HTTP calls to the Cohere API.
/// </summary>
public class CohereService : IAIService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CohereService> _logger;
    private readonly CohereOptions _options;
    private readonly bool _isConfigured;

    private const string SystemPreamble = @"You are an expert project manager and AI task prioritizer. Your job is to take a high-level goal and break it down into a sequence of actionable tasks, prioritizing them by rank (1 being most critical).

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

    public CohereService(
        HttpClient httpClient,
        IOptions<CohereOptions> options,
        ILogger<CohereService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            _logger.LogWarning("Cohere API key is not configured. Service will not function.");
            _isConfigured = false;
            return;
        }

        _isConfigured = true;

        // Set up headers for Cohere API
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogInformation("CohereService initialized with model {Model}", _options.Model);
    }

    public async Task<Result<PrioritizationResponse>> PrioritizeGoalAsync(string goal, CancellationToken cancellationToken = default)
    {
        if (!_isConfigured)
        {
            return Result<PrioritizationResponse>.Failure("Cohere service is not configured. API key is missing.");
        }

        _logger.LogInformation("Starting goal prioritization with Cohere for goal: {Goal}", goal);

        try
        {
            // Build the Cohere Chat API request
            var requestBody = new
            {
                model = _options.Model,
                message = $"Please prioritize and break down this goal: {goal}",
                preamble = SystemPreamble,
                temperature = _options.Temperature,
                max_tokens = _options.MaxTokens,
                response_format = new { type = "json_object" }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_options.ApiUrl, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Cohere API error: {StatusCode} - {Error}", response.StatusCode, errorBody);
                return Result<PrioritizationResponse>.Failure($"Cohere API error: {response.StatusCode}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("Cohere response received, length: {Length}", responseBody.Length);

            // Parse Cohere response format
            var responseJson = JsonNode.Parse(responseBody);
            var textContent = responseJson?["text"]?.GetValue<string>();

            if (string.IsNullOrEmpty(textContent))
            {
                _logger.LogWarning("Cohere returned an empty response");
                return Result<PrioritizationResponse>.Failure("Cohere returned an empty response.");
            }

            // Clean up any markdown code blocks if present
            textContent = CleanJsonResponse(textContent);

            // Parse the JSON response
            var result = JsonSerializer.Deserialize<PrioritizationResponse>(
                textContent,
                ApiJsonContext.Default.PrioritizationResponse);

            if (result == null)
            {
                _logger.LogError("Failed to deserialize Cohere response");
                return Result<PrioritizationResponse>.Failure("Failed to parse Cohere response.");
            }

            // Extract usage metadata from Cohere's billing info
            var inputTokens = responseJson?["meta"]?["billed_units"]?["input_tokens"]?.GetValue<int>() ?? 0;
            var outputTokens = responseJson?["meta"]?["billed_units"]?["output_tokens"]?.GetValue<int>() ?? 0;

            result.UsageMetadata = new UsageMetadata
            {
                PromptTokenCount = inputTokens,
                CandidatesTokenCount = outputTokens,
                TotalTokenCount = inputTokens + outputTokens
            };

            _logger.LogInformation(
                "Goal prioritization completed with Cohere. Tasks: {TaskCount}, Tokens: {TotalTokens}",
                result.TaskItems.Count(),
                result.UsageMetadata.TotalTokenCount);

            return Result<PrioritizationResponse>.Success(result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Cohere request was canceled");
            return Result<PrioritizationResponse>.Failure("Operation was canceled.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Cohere response as JSON");
            return Result<PrioritizationResponse>.Failure($"Failed to parse response: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Cohere prioritization");
            return Result<PrioritizationResponse>.Failure($"Cohere error: {ex.Message}");
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
