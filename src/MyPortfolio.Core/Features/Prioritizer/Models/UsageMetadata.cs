using System.Text.Json.Serialization;

namespace MyPortfolio.Core.Features.Prioritizer.Models;

// Maps the 'usageMetadata' object from the Gemini API
public record UsageMetadata
{
    // These properties track the cost/token usage
    [JsonPropertyName("promptTokenCount")]
    public int PromptTokenCount { get; init; }

    [JsonPropertyName("candidatesTokenCount")]
    public int CandidatesTokenCount { get; init; }

    [JsonPropertyName("totalTokenCount")]
    public int TotalTokenCount { get; init; }
}