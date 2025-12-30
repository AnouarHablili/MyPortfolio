using System.Text.Json.Serialization;

namespace MyPortfolio.Core.Features.Prioritizer.Models;

public class PrioritizationResponse
{
    // Map C# property "TaskItems" to JSON property "tasks"
    // This allows you to use TaskItems in C# (avoiding confusion with System.Threading.Tasks.Task)
    // while the JSON/API uses the simpler "tasks" name
    [JsonPropertyName("tasks")]
    public IEnumerable<TaskItem> TaskItems { get; set; } = Array.Empty<TaskItem>();

    [JsonPropertyName("executiveSummary")]
    public string ExecutiveSummary { get; set; } = string.Empty;

    // New property to track API usage and cost
    [JsonPropertyName("usageMetadata")]  
    public UsageMetadata? UsageMetadata { get; set; }
}

public class TaskItem
{
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("taskTitle")]
    public string TaskTitle { get; set; } = string.Empty;

    [JsonPropertyName("reasoningChain")]
    public string ReasoningChain { get; set; } = string.Empty;

    [JsonPropertyName("estimate")]
    public string? Estimate { get; set; }
}