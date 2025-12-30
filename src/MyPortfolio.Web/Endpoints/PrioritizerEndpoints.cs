using MyPortfolio.Core.Abstractions;
using MyPortfolio.Core.Features.Prioritizer.Models;
using MyPortfolio.Web.Endpoints;
using Microsoft.AspNetCore.Mvc;

namespace MyPortfolio.Web.Endpoints;

public static class PrioritizerEndpoints
{
    public static void MapPrioritizerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/prioritizer")
            .RequireAuthorization(); // Require access code

        group.MapPost("/prioritize", async ([FromBody] string goal, IAIService aiService) =>
        {
            if (string.IsNullOrWhiteSpace(goal))
            {
                return Results.BadRequest("Goal cannot be empty");
            }

            var result = await aiService.PrioritizeGoalAsync(goal);

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
