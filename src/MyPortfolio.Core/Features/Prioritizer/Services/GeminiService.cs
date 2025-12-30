// MyPortfolio.Core/Features/Prioritizer/Services/GeminiService.cs

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyPortfolio.Core.Abstractions;
using MyPortfolio.Core.Features.Prioritizer.Models;
using MyPortfolio.Core.Features.Prioritizer.Services.Models;
using MyPortfolio.Core.Shared;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MyPortfolio.Core.Features.Prioritizer.Services;

/// <summary>
/// Configuration options for the Gemini AI Service.
/// </summary>
public class GeminiOptions
{
    // The section name used in configuration files (e.g., appsettings.json)
    public const string Gemini = "Gemini";

    public required string ApiKey { get; init; }

    /// <summary>
    /// Maximum number of retry attempts for failed requests.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Initial delay in seconds before the first retry.
    /// </summary>
    public int InitialRetryDelaySeconds { get; init; } = 1;

    /// <summary>
    /// Request timeout in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; init; } = 60;
}

public class GeminiService : IAIService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiService> _logger;
    private readonly GeminiOptions _options;
    private readonly string _apiKey;

    // Uses the latest Gemini 2.5 Flash model for speed
    private const string ModelId = "gemini-2.5-flash";

    // The streaming endpoint URL
    private const string ApiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{ModelId}:streamGenerateContent";

    // The JSON Source Generator context instance
    private static readonly ApiJsonContext JsonContext = ApiJsonContext.Default;

    public GeminiService(HttpClient httpClient, IOptions<GeminiOptions> options, ILogger<GeminiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;

        // 1. Get the API Key from the IOptions wrapper
        _apiKey = _options.ApiKey
                     ?? throw new InvalidOperationException("GeminiOptions: ApiKey must be configured.");

        // 2. Set Authentication Header
        // The service now relies on the API Key being passed in via IOptions
        _httpClient.DefaultRequestHeaders.Add("X-Goog-Api-Key", _apiKey);

        // 3. Set the Content Type/Accept header
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );

        // 4. Set request timeout
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);

        _logger.LogInformation("GeminiService initialized with MaxRetries={MaxRetries}, Timeout={Timeout}s",
            _options.MaxRetries,
            _options.RequestTimeoutSeconds);
    }

    public async Task<Result<PrioritizationResponse>> PrioritizeGoalAsync(string goal, CancellationToken cancellationToken = default)
    {
        // 1. Define the System Prompt to enforce the task execution
        const string systemPrompt = "You are an expert project manager and AI task prioritor. Your job is to take a high-level goal and break it down into a sequence of actionable TaskItems, prioritizing them by rank (1 being most critical). For every task, you MUST provide a detailed 'ReasoningChain' explaining why that task has that specific rank and how it contributes to the overall goal. You MUST ONLY return the result as a single, valid JSON object that strictly adheres to the provided JSON Schema. Do not include any text, headers, or markdown outside the JSON object.";

        // 2. Dynamically Generate JSON Schema for the output model
        var taskItemSchema = new PropertySchema(
            Type: "object",
            Properties: new Dictionary<string, PropertySchema>
            {
                { "rank", new PropertySchema("integer", "The priority rank (1, 2, 3...)") },
                { "taskTitle", new PropertySchema("string", "The title of the task.") },
                { "reasoningChain", new PropertySchema("string", "Detailed justification for the rank.") },
                { "estimate", new PropertySchema("string", "Estimated time or complexity.") }
            },
            Required: new[] { "rank", "taskTitle", "reasoningChain" }
        );

        var prioritizationSchema = new ResponseSchema(
            Type: "object",
            Properties: new Dictionary<string, PropertySchema>
            {
                { "tasks", new PropertySchema("array", "The prioritized list of TaskItems.", taskItemSchema) },
                { "executiveSummary", new PropertySchema("string", "A summary of the overall strategy.") }
            },
            Required: new[] { "tasks", "executiveSummary" }
        );

        // 3. Build the Gemini Request Payload
        var requestPayload = new GeminiRequest(
            Contents: [new([new(goal)], Role: "user")],
            SystemInstruction: new Content([new Part(systemPrompt)]),
            GenerationConfig: new GenerationConfig(
                ResponseSchema: prioritizationSchema
            )
        );

        _logger.LogInformation("Starting goal prioritization for goal: {Goal}", goal);

        // Retry Parameters
        var maxRetries = _options.MaxRetries;
        var currentDelay = TimeSpan.FromSeconds(_options.InitialRetryDelaySeconds);
        Exception? lastException = null;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            // 4. Serialize the Request using the Source Generator
            var requestJson = JsonSerializer.Serialize(requestPayload, JsonContext.GeminiRequest);
            _logger.LogDebug("Request payload (attempt {Attempt}): {Payload}", attempt + 1, requestJson);
            
            var content = new StringContent(
                requestJson,
                MediaTypeHeaderValue.Parse("application/json")
            );
            // 5. Make the Streaming API Call
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
                {
                    Content = content
                };

                using var httpResponse = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!httpResponse.IsSuccessStatusCode)
                {
                    // Handle retryable status codes
                    if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests ||
                        httpResponse.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        if (attempt < maxRetries - 1)
                        {
                            var errorBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                            _logger.LogWarning(
                                "HTTP {StatusCode} received (attempt {Attempt}/{MaxRetries}). Retrying in {DelaySeconds}s. Error: {Error}",
                                httpResponse.StatusCode,
                                attempt + 1,
                                maxRetries,
                                currentDelay.TotalSeconds,
                                errorBody);

                            await Task.Delay(currentDelay, cancellationToken);
                            currentDelay *= 2;
                            continue;
                        }
                    }

                    // Non-retryable or final retry attempt - return failure
                    var finalErrorBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError(
                        "API request failed with status {StatusCode} after {Attempt} attempts. Error: {Error}",
                        httpResponse.StatusCode,
                        attempt + 1,
                        finalErrorBody);
                    return Result<PrioritizationResponse>.Failure($"API Request Failed: {httpResponse.StatusCode} - {finalErrorBody}");
                }

                using var responseStream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
                
                // Read the entire response as a string
                string fullResponseText;
                using (var streamReader = new StreamReader(responseStream))
                {
                    fullResponseText = await streamReader.ReadToEndAsync(cancellationToken);
                }

                _logger.LogDebug("Raw response length: {Length} characters", fullResponseText.Length);

                // The Gemini API can return:
                // 1. A JSON array of response chunks (production with JSON mode)
                // 2. A single JSON object (simple response)
                // 3. Multiple JSON objects separated by newlines (SSE format)
                var fullResponseJson = "";
                UsageMetadata? usageMetadata = null;

                try
                {
                    // Try to parse as a single JSON structure first (array or object)
                    JsonNode? responseNode = null;
                    var lines = fullResponseText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    // If there's only one line or it starts with '[', try parsing as single structure
                    if (lines.Length == 1 || fullResponseText.TrimStart().StartsWith("["))
                    {
                        try
                        {
                            responseNode = JsonNode.Parse(fullResponseText);
                        }
                        catch (JsonException)
                        {
                            // Not a single JSON structure, will try line-by-line
                        }
                    }
                    
                    if (responseNode is JsonArray array)
                    {
                        // Format 1: JSON array of chunks
                        _logger.LogInformation("Parsing JSON array with {Count} elements", array.Count);
                        
                        foreach (var item in array)
                        {
                            if (!ProcessResponseChunk(item, ref fullResponseJson, ref usageMetadata))
                            {
                                return Result<PrioritizationResponse>.Failure("API returned an error");
                            }
                        }
                    }
                    else if (responseNode is JsonObject obj)
                    {
                        // Format 2: Single JSON object
                        _logger.LogDebug("Parsing single JSON object");
                        
                        if (!ProcessResponseChunk(obj, ref fullResponseJson, ref usageMetadata))
                        {
                            return Result<PrioritizationResponse>.Failure("API returned an error");
                        }
                    }
                    else
                    {
                        // Format 3: Multiple JSON objects separated by newlines (SSE format)
                        _logger.LogDebug("Parsing multiple JSON objects (SSE format), {Count} lines", lines.Length);
                        
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line))
                                continue;
                                
                            // Remove SSE prefix if present
                            var jsonLine = line;
                            if (line.StartsWith("data: ", StringComparison.Ordinal))
                            {
                                jsonLine = line.Substring(6);
                            }
                            else if (line.StartsWith("data:", StringComparison.Ordinal))
                            {
                                jsonLine = line.Substring(5);
                            }
                            
                            if (string.IsNullOrWhiteSpace(jsonLine))
                                continue;
                            
                            try
                            {
                                var lineNode = JsonNode.Parse(jsonLine);
                                if (!ProcessResponseChunk(lineNode, ref fullResponseJson, ref usageMetadata))
                                {
                                    return Result<PrioritizationResponse>.Failure("API returned an error");
                                }
                            }
                            catch (JsonException ex)
                            {
                                _logger.LogWarning(ex, "Failed to parse line as JSON, skipping: {Line}", 
                                    jsonLine.Substring(0, Math.Min(100, jsonLine.Length)));
                                continue;
                            }
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to parse response as JSON. First 500 chars: {Response}", 
                        fullResponseText.Substring(0, Math.Min(500, fullResponseText.Length)));
                    return Result<PrioritizationResponse>.Failure($"Failed to parse API response: {ex.Message}");
                }

                _logger.LogInformation("Accumulated JSON length: {JsonLength}", fullResponseJson.Length);

                // 6. Final Deserialization: The fullResponseJson string is now a complete, valid JSON object.
                if (string.IsNullOrEmpty(fullResponseJson))
                {
                    _logger.LogWarning("AI returned an empty response. Full response (first 1000 chars): {FullResponse}", 
                        fullResponseText.Substring(0, Math.Min(1000, fullResponseText.Length)));
                    return Result<PrioritizationResponse>.Failure("AI returned an empty response.");
                }

                var finalResponse = JsonSerializer.Deserialize(fullResponseJson, JsonContext.PrioritizationResponse);

                if (finalResponse is null)
                {
                    _logger.LogError("Failed to deserialize AI response. JSON length: {JsonLength}", fullResponseJson.Length);
                    return Result<PrioritizationResponse>.Failure("AI output could not be parsed into the required object structure.");
                }
                
                finalResponse.UsageMetadata = usageMetadata;
                
                _logger.LogInformation(
                    "Goal prioritization completed successfully. Tasks: {TaskCount}, Tokens: {TotalTokens}",
                    finalResponse.TaskItems.Count(),
                    usageMetadata?.TotalTokenCount ?? 0);
                
                return Result<PrioritizationResponse>.Success(finalResponse);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Goal prioritization was canceled by the user");
                return Result<PrioritizationResponse>.Failure("Operation was canceled by the user.");
            }
            catch (HttpRequestException httpEx) when (
                attempt < maxRetries - 1 &&
                httpEx.StatusCode.HasValue &&
                (httpEx.StatusCode.Value == HttpStatusCode.TooManyRequests ||
                 httpEx.StatusCode.Value == HttpStatusCode.ServiceUnavailable))
            {
                // Log and Retry
                _logger.LogWarning(
                    httpEx,
                    "HTTP Request failed (attempt {Attempt}/{MaxRetries}): {Message}. Retrying in {DelaySeconds}s",
                    attempt + 1,
                    maxRetries,
                    httpEx.Message,
                    currentDelay.TotalSeconds);
                lastException = httpEx;

                await Task.Delay(currentDelay, cancellationToken);
                currentDelay *= 2;
            }
            catch (HttpRequestException httpEx)
            {
                // Non-retryable HTTP errors or final retry
                _logger.LogError(httpEx, "Non-retryable HTTP error occurred");
                return Result<PrioritizationResponse>.Failure($"API Request Failed: {httpEx.Message}");
            }
            catch (Exception ex)
            {
                // Check if this is a retryable network error
                if (attempt < maxRetries - 1 && IsRetryableException(ex))
                {
                    _logger.LogWarning(
                        ex,
                        "Retryable error (attempt {Attempt}/{MaxRetries}): {Message}. Retrying in {DelaySeconds}s",
                        attempt + 1,
                        maxRetries,
                        ex.Message,
                        currentDelay.TotalSeconds);
                    lastException = ex;

                    await Task.Delay(currentDelay, cancellationToken);
                    currentDelay *= 2;
                    continue;
                }

                // Non-retryable exception or final retry
                _logger.LogError(ex, "Non-retryable exception occurred during API processing");
                return Result<PrioritizationResponse>.Failure($"An unexpected error occurred during API processing: {ex.Message}");
            }
        }

        // If we've exhausted all retries
        _logger.LogError(
            lastException,
            "Failed after {MaxRetries} attempts. Last error: {ErrorMessage}",
            maxRetries,
            lastException?.Message ?? "Unknown error");
        
        return Result<PrioritizationResponse>.Failure(
            $"Failed after {maxRetries} attempts. Last error: {lastException?.Message ?? "Unknown error"}");
    }

    private static bool IsRetryableException(Exception ex)
    {
        // Add other retryable exceptions as needed
        return ex is TaskCanceledException or TimeoutException;
    }

    /// <summary>
    /// Processes a single response chunk (JSON node) and extracts text content and metadata.
    /// Returns false if the chunk contains an error.
    /// </summary>
    private bool ProcessResponseChunk(JsonNode? chunk, ref string accumulatedJson, ref UsageMetadata? metadata)
    {
        if (chunk == null)
            return true;

        // Check for error
        var errorNode = chunk["error"];
        if (errorNode != null)
        {
            var errorMessage = errorNode["message"]?.GetValue<string>() ?? "Unknown error";
            _logger.LogError("API returned error: {Error}", errorMessage);
            return false;
        }

        // Extract usage metadata
        var usageNode = chunk["usageMetadata"];
        if (usageNode != null)
        {
            metadata = usageNode.Deserialize<UsageMetadata>(ApiJsonContext.Default.UsageMetadata);
        }

        // Extract text content
        var textPart = chunk["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();
        
        if (!string.IsNullOrEmpty(textPart))
        {
            accumulatedJson += textPart;
        }

        return true;
    }
}