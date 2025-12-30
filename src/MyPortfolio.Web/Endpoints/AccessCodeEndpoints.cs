using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyPortfolio.Shared.Configuration;
using MyPortfolio.Shared.Models;
using MyPortfolio.Web.Services;

namespace MyPortfolio.Web.Endpoints;

public static class AccessCodeEndpoints
{
    public static IEndpointRouteBuilder MapAccessCodeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/access-code");

        group.MapPost("/validate", ValidateAccessCode)
            .WithName("ValidateAccessCode")
            .Produces<AccessCodeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status429TooManyRequests)
            .WithTags("Access Code");

        group.MapPost("/refresh", RefreshToken)
            .WithName("RefreshToken")
            .Produces<AccessCodeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("Access Code");

        return endpoints;
    }

    private static IResult ValidateAccessCode(
        [FromBody] AccessCodeRequest request,
        HttpContext httpContext,
        [FromServices] IOptions<AccessCodeOptions> options,
        [FromServices] JwtTokenService jwtTokenService,
        [FromServices] RateLimitService rateLimitService,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AccessCodeEndpoints");
        
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Results.Ok(new AccessCodeResponse
            {
                IsValid = false,
                ErrorMessage = "Access code is required."
            });
        }

        // Get client IP address for rate limiting
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Check rate limit
        if (rateLimitService.IsRateLimited(clientIp, out var remainingAttempts))
        {
            logger.LogWarning(
                "Rate limit exceeded for IP {IpAddress}. Remaining attempts: {RemainingAttempts}",
                clientIp,
                remainingAttempts);

            return Results.Json(
                new AccessCodeResponse
                {
                    IsValid = false,
                    ErrorMessage = $"Too many attempts. Please try again later."
                },
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        // Record attempt
        rateLimitService.RecordAttempt(clientIp);

        var configuredCode = options.Value.Code;
        var isValid = string.Equals(request.Code, configuredCode, StringComparison.Ordinal);

        if (!isValid)
        {
            logger.LogWarning("Invalid access code attempt from IP {IpAddress}", clientIp);
            return Results.Ok(new AccessCodeResponse
            {
                IsValid = false,
                ErrorMessage = "Invalid access code. Please try again.",
                RemainingAttempts = remainingAttempts - 1
            });
        }

        // Generate JWT token
        var token = jwtTokenService.GenerateToken(request.Code);
        var expiresAt = DateTime.UtcNow.AddHours(options.Value.TokenExpirationHours);

        // Reset rate limit on successful validation
        rateLimitService.ResetLimit(clientIp);

        logger.LogInformation("Access code validated successfully for IP {IpAddress}", clientIp);

        return Results.Ok(new AccessCodeResponse
        {
            IsValid = true,
            Token = token,
            TokenExpiresAt = expiresAt
        });
    }

    private static IResult RefreshToken(
        [FromBody] RefreshTokenRequest request,
        [FromServices] JwtTokenService jwtTokenService,
        [FromServices] IOptions<AccessCodeOptions> options,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AccessCodeEndpoints");
        
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Results.Unauthorized();
        }

        var principal = jwtTokenService.ValidateToken(request.Token);
        if (principal == null)
        {
            logger.LogWarning("Token refresh failed: invalid token");
            return Results.Unauthorized();
        }

        // Extract access code from claims
        var accessCode = principal.FindFirst("access_code")?.Value;
        if (string.IsNullOrEmpty(accessCode))
        {
            logger.LogWarning("Token refresh failed: access code claim not found");
            return Results.Unauthorized();
        }

        // Generate new token
        var newToken = jwtTokenService.GenerateToken(accessCode);
        var expiresAt = DateTime.UtcNow.AddHours(options.Value.TokenExpirationHours);

        logger.LogInformation("Token refreshed successfully");

        return Results.Ok(new AccessCodeResponse
        {
            IsValid = true,
            Token = newToken,
            TokenExpiresAt = expiresAt
        });
    }
}
