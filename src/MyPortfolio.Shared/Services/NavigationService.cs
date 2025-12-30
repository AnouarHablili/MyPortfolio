using Microsoft.AspNetCore.Components;

namespace MyPortfolio.Shared.Services;

/// <summary>
/// Service for managing navigation between pages and POCs.
/// </summary>
public class NavigationService
{
    private readonly NavigationManager _navigationManager;

    public NavigationService(NavigationManager navigationManager)
    {
        _navigationManager = navigationManager;
    }

    /// <summary>
    /// Navigates to a specific POC.
    /// </summary>
    public void NavigateToPOC(string pocId)
    {
        _navigationManager.NavigateTo($"/poc/{pocId}");
    }

    /// <summary>
    /// Navigates to the home page.
    /// </summary>
    public void NavigateToHome()
    {
        _navigationManager.NavigateTo("/");
    }

    /// <summary>
    /// Navigates to the about page.
    /// </summary>
    public void NavigateToAbout()
    {
        _navigationManager.NavigateTo("/about");
    }

    /// <summary>
    /// Gets the current page URI.
    /// </summary>
    public string GetCurrentUri()
    {
        return _navigationManager.Uri;
    }

    /// <summary>
    /// Checks if currently on home page.
    /// </summary>
    public bool IsOnHomePage()
    {
        return _navigationManager.Uri.EndsWith("/");
    }
}