namespace MyPortfolio.Shared.Models;

/// <summary>
/// Request to validate a global access code.
/// </summary>
public record AccessCodeRequest
{
    /// <summary>The access code provided by the user.</summary>
    public required string Code { get; init; }
}