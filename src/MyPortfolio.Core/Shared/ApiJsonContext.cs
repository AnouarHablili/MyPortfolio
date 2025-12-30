// MyPortfolio.Core/Shared/ApiJsonContext.cs

using System.Text.Json.Serialization;
using MyPortfolio.Core.Features.Prioritizer.Models;
using MyPortfolio.Core.Features.Prioritizer.Services.Models;

namespace MyPortfolio.Core.Shared;

// Note: You will need to create the 'Services.Models' folder and the types below in the next steps.

// 1. Mark all types that need fast serialization/deserialization.
// 2. Set the naming policy to match the C# standard, so it converts to camelCase JSON (default for System.Text.Json).
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(PrioritizationResponse))]    // Our output model
[JsonSerializable(typeof(GeminiRequest))]           // The JSON we send to the API
[JsonSerializable(typeof(GeminiStreamResponse))]    // The chunks we receive from the API
internal partial class ApiJsonContext : JsonSerializerContext
{
}