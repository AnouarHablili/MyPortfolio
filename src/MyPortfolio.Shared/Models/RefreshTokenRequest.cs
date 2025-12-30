namespace MyPortfolio.Shared.Models;

/// <summary>
/// Request to refresh an access token.
/// </summary>
public record RefreshTokenRequest
{
    /// <summary>The current JWT token to refresh.</summary>
    public required string Token { get; init; }
}

