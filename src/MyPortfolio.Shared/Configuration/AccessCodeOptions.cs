namespace MyPortfolio.Shared.Configuration;

/// <summary>
/// Configuration options for access code authentication.
/// </summary>
public class AccessCodeOptions
{
    public const string SectionName = "AccessCode";

    /// <summary>
    /// The access code required to unlock POCs.
    /// Store in User Secrets (dev) or Azure Key Vault (production).
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Number of hours before the access token expires.
    /// </summary>
    public int TokenExpirationHours { get; init; } = 24;

    /// <summary>
    /// Maximum number of validation attempts per IP address per time window.
    /// </summary>
    public int MaxValidationAttempts { get; init; } = 5;

    /// <summary>
    /// Time window in minutes for rate limiting validation attempts.
    /// </summary>
    public int RateLimitWindowMinutes { get; init; } = 15;
}

