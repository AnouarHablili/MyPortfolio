using System.Text.Json.Serialization;

namespace MyPortfolio.Core.Features.Prioritizer.Services.Models;

// Maps the top-level object returned in each chunk of the streaming response.
public record GeminiStreamResponse(
    [property: JsonPropertyName("candidates")] IReadOnlyList<Candidate> Candidates
// Note: UsageMetadata and modelVersion are typically present but ignored for simplicity
);

// Represents a single candidate response from the model.
public record Candidate(
    [property: JsonPropertyName("content")] Content Content
// Note: finishReason is also present but ignored for simplicity
);