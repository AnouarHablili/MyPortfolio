using MyPortfolio.Core.Abstractions;
using MyPortfolio.Core.Features.Prioritizer.Models;
using MyPortfolio.Core.Features.Prioritizer.Services;
using MyPortfolio.Web.Endpoints;
using Microsoft.AspNetCore.Mvc;

namespace MyPortfolio.Web.Endpoints;

public static class PrioritizerEndpoints
{
    public static void MapPrioritizerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/prioritizer")
            .RequireAuthorization(); // Require access code

        group.MapPost("/prioritize", async (
            [FromBody] PrioritizeRequest request,
            IAIServiceFactory aiServiceFactory,
            IAIService defaultService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Goal))
            {
                return Results.BadRequest("Goal cannot be empty");
            }

            // Get the appropriate service based on provider ID, fallback to default (Gemini)
            var aiService = aiServiceFactory.GetService(request.ProviderId ?? "gemini") ?? defaultService;

            var result = await aiService.PrioritizeGoalAsync(request.Goal);

            if (result.IsSuccess)
            {
                return Results.Ok(result.Value);
            }
            else
            {
                return Results.BadRequest(result.Error);
            }
        })
        .WithName("PrioritizeGoal");
    }
}

/// <summary>
/// Request model for the prioritize endpoint.
/// </summary>
public class PrioritizeRequest
{
    public string Goal { get; set; } = string.Empty;
    public string? ProviderId { get; set; }
}
