using System.Text.Json.Serialization;

namespace MyPortfolio.Core.Features.Prioritizer.Services.Models;

// Maps the high-level request body.
public record GeminiRequest(
    [property: JsonPropertyName("contents")] IReadOnlyList<Content> Contents,
    [property: JsonPropertyName("generationConfig")] GenerationConfig? GenerationConfig = null,
    [property: JsonPropertyName("systemInstruction")] Content? SystemInstruction = null
);

// Represents a conversation turn (user or model).
public record Content(
    [property: JsonPropertyName("parts")] IReadOnlyList<Part> Parts,
    // Role must be part of the primary constructor for proper serialization/deserialization
    // Use JsonIgnore with Condition to skip null values
    [property: JsonPropertyName("role"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Role = null
);

// Represents the textual data part of the content.
public record Part(
    [property: JsonPropertyName("text")] string Text
);

// Configuration to enforce JSON output based on our schema.
public record GenerationConfig(
    [property: JsonPropertyName("responseMimeType")] string ResponseMimeType = "application/json",
    [property: JsonPropertyName("responseSchema")] ResponseSchema? ResponseSchema = null
);

// This is the C# representation of the JSON Schema (OpenAPI 3.0 compatible)
// that Gemini uses to enforce structure.
public record ResponseSchema(
    [property: JsonPropertyName("type")] string Type = "object",
    [property: JsonPropertyName("properties")] IReadOnlyDictionary<string, PropertySchema>? Properties = null,
    [property: JsonPropertyName("required")] IReadOnlyList<string>? Required = null
);

// Defines the structure and type for a property within the schema.
public record PropertySchema(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("items")] PropertySchema? Items = null, // Used for array types

    // Properties and Required fields are necessary for object type schemas
    [property: JsonPropertyName("properties")] IReadOnlyDictionary<string, PropertySchema>? Properties = null,
    [property: JsonPropertyName("required")] IReadOnlyList<string>? Required = null
);