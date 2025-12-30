namespace MyPortfolio.Shared.Models;

/// <summary>
/// Metadata for a Proof of Concept (POC) feature.
/// </summary>
public record POCMetadata
{
    /// <summary>Unique identifier for the POC.</summary>
    public required string Id { get; init; }

    /// <summary>Display name of the POC.</summary>
    public required string Title { get; init; }

    /// <summary>Detailed description of what the POC demonstrates.</summary>
    public required string Description { get; init; }

    /// <summary>Route/path to access the POC.</summary>
    public required string Route { get; init; }

    /// <summary>Icon/image URL for the POC card.</summary>
    public string? IconUrl { get; init; }

    /// <summary>List of technologies used in the POC.</summary>
    public IReadOnlyList<string> Technologies { get; init; } = Array.Empty<string>();

    /// <summary>Category of the POC (e.g., AI, Web, Mobile).</summary>
    public required string Category { get; init; }

    /// <summary>Status of the POC (Active, ComingSoon, Deprecated).</summary>
    public POCStatus Status { get; init; } = POCStatus.Active;

    /// <summary>Order in which to display the POC on the home page.</summary>
    public int DisplayOrder { get; init; }

    /// <summary>Client-facing name (for portfolio/business purposes).</summary>
    public string ClientFacingName { get; init; } = string.Empty;
}

public enum POCStatus
{
    Active,
    ComingSoon,
    Deprecated
}