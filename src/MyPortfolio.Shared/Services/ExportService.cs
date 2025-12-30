using MyPortfolio.Core.Features.Prioritizer.Models;
using System.Text;
using System.Text.Json;

namespace MyPortfolio.Shared.Services;

/// <summary>
/// Service for exporting prioritization results to various formats.
/// </summary>
public class ExportService
{
    /// <summary>
    /// Exports prioritization results to JSON format.
    /// </summary>
    public string ExportToJson(PrioritizationResponse result, string? goal = null)
    {
        var exportData = new
        {
            exportedAt = DateTime.UtcNow,
            goal = goal,
            executiveSummary = result.ExecutiveSummary,
            tasks = result.TaskItems.OrderBy(t => t.Rank).Select(t => new
            {
                rank = t.Rank,
                taskTitle = t.TaskTitle,
                reasoningChain = t.ReasoningChain,
                estimate = t.Estimate
            }),
            usageMetadata = result.UsageMetadata != null ? new
            {
                promptTokenCount = result.UsageMetadata.PromptTokenCount,
                candidatesTokenCount = result.UsageMetadata.CandidatesTokenCount,
                totalTokenCount = result.UsageMetadata.TotalTokenCount
            } : null
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(exportData, options);
    }

    /// <summary>
    /// Exports prioritization results to plain text format.
    /// </summary>
    public string ExportToText(PrioritizationResponse result, string? goal = null)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("=".PadRight(60, '='));
        sb.AppendLine("AI TASK PRIORITIZATION RESULTS");
        sb.AppendLine("=".PadRight(60, '='));
        sb.AppendLine();
        
        if (!string.IsNullOrEmpty(goal))
        {
            sb.AppendLine($"Goal: {goal}");
            sb.AppendLine();
        }
        
        sb.AppendLine($"Executive Summary:");
        sb.AppendLine(result.ExecutiveSummary);
        sb.AppendLine();
        sb.AppendLine("-".PadRight(60, '-'));
        sb.AppendLine("PRIORITIZED TASKS");
        sb.AppendLine("-".PadRight(60, '-'));
        sb.AppendLine();
        
        foreach (var task in result.TaskItems.OrderBy(t => t.Rank))
        {
            sb.AppendLine($"Rank {task.Rank}: {task.TaskTitle}");
            if (!string.IsNullOrEmpty(task.Estimate))
            {
                sb.AppendLine($"  Estimate: {task.Estimate}");
            }
            sb.AppendLine($"  Reasoning: {task.ReasoningChain}");
            sb.AppendLine();
        }
        
        if (result.UsageMetadata != null)
        {
            sb.AppendLine("-".PadRight(60, '-'));
            sb.AppendLine("USAGE METADATA");
            sb.AppendLine("-".PadRight(60, '-'));
            sb.AppendLine($"Total Tokens: {result.UsageMetadata.TotalTokenCount}");
            sb.AppendLine($"  - Prompt: {result.UsageMetadata.PromptTokenCount}");
            sb.AppendLine($"  - Candidates: {result.UsageMetadata.CandidatesTokenCount}");
        }
        
        sb.AppendLine();
        sb.AppendLine($"Exported: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        
        return sb.ToString();
    }

    /// <summary>
    /// Downloads the exported content as a file in the browser.
    /// </summary>
    public void DownloadAsFile(string content, string filename, string contentType = "text/plain")
    {
        // This will be called from JavaScript interop
        // The actual download is handled in JavaScript
    }
}

