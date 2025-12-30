using MyPortfolio.Shared.Models;

namespace MyPortfolio.Shared.Services;

/// <summary>
/// Service for managing POC metadata and registry.
/// </summary>
public class POCService
{
    private readonly List<POCMetadata> _pocRegistry = new();

    public POCService()
    {
        InitializeRegistry();
    }

    /// <summary>
    /// Gets all registered POCs.
    /// </summary>
    public IReadOnlyList<POCMetadata> GetAllPOCs()
    {
        return _pocRegistry
            .OrderBy(p => p.DisplayOrder)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Gets active POCs only.
    /// </summary>
    public IReadOnlyList<POCMetadata> GetActivePOCs()
    {
        return _pocRegistry
            .Where(p => p.Status == POCStatus.Active)
            .OrderBy(p => p.DisplayOrder)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Gets a POC by ID.
    /// </summary>
    public POCMetadata? GetPOCById(string id)
    {
        return _pocRegistry.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>
    /// Registers a new POC.
    /// </summary>
    public void RegisterPOC(POCMetadata poc)
    {
        if (_pocRegistry.Any(p => p.Id == poc.Id))
        {
            throw new InvalidOperationException($"POC with ID '{poc.Id}' is already registered.");
        }

        _pocRegistry.Add(poc);
    }

    private void InitializeRegistry()
    {
        // Register the Prioritizer POC
        RegisterPOC(new POCMetadata
        {
            Id = "prioritizer",
            Title = "AI Task Prioritizer",
            Description = "Powered by Google Gemini 2.5 Flash. Enter a high-level goal and get intelligent task breakdown with reasoning chains.",
            Route = "/poc/prioritizer",
            IconUrl = "/images/poc-icons/prioritizer.svg",
            Technologies = new[] { "Gemini API", "C#", "Blazor", "ASP.NET Core" },
            Category = "AI",
            Status = POCStatus.Active,
            DisplayOrder = 1,
            ClientFacingName = "Intelligent Task Prioritization"
        });

        // Placeholder for future POCs
        // RegisterPOC(new POCMetadata { ... });
    }
}