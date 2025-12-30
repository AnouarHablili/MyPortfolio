using MyPortfolio.Core.Features.Prioritizer.Models;
using MyPortfolio.Core.Shared;

namespace MyPortfolio.Core.Abstractions;

/// <summary>
/// Abstraction for all core AI operations (Gemini, OpenAI, Groq, etc.).
/// </summary>
public interface IAIService
{
    /// <summary>
    /// Processes a goal/vision and returns a prioritized list of steps with reasoning.
    /// This corresponds to POC #1 (AI Task Prioritizer).
    /// </summary>
    /// <param name="rawGoal">The user's raw input goal (e.g., "Launch my SaaS").</param>
    /// <returns>A Result containing the prioritized steps or an error message.</returns>
    Task<Result<PrioritizationResponse>> PrioritizeGoalAsync(string rawGoal, CancellationToken cancellationToken = default);

    // TODO: Define methods for POC #2 (RAG Chat) and POC #3 (Vision Board Suggestions) later.
}