namespace MyPortfolio.Shared.Models;

/// <summary>
/// Response from access code validation.
/// </summary>
public record AccessCodeResponse
{
    /// <summary>Whether the access code is valid.</summary>
    public required bool IsValid { get; init; }

    /// <summary>Error message if validation failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Token to include in subsequent API calls (if valid).</summary>
    public string? Token { get; init; }

    /// <summary>Token expiration time (UTC).</summary>
    public DateTime? TokenExpiresAt { get; init; }

    /// <summary>Remaining validation attempts before rate limit (if applicable).</summary>
    public int? RemainingAttempts { get; init; }
}