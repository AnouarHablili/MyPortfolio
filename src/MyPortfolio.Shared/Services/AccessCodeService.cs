using MyPortfolio.Shared.Models;
using System.Net;
using System.Net.Http.Json;

namespace MyPortfolio.Shared.Services;

/// <summary>
/// Service for managing global access code validation for all POCs.
/// Once a valid access code is provided, the user can access all POCs.
/// </summary>
public class AccessCodeService
{
    private readonly HttpClient _httpClient;
    private string? _globalToken;
    private DateTime _tokenExpiresAt = DateTime.MinValue;

    /// <summary>
    /// Event triggered when the unlock modal should be shown.
    /// </summary>
    public event Action? OnShowUnlockModal;

    /// <summary>
    /// Event triggered when access state changes (login/logout).
    /// </summary>
    public event Action? OnAccessStateChanged;

    public AccessCodeService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Requests the UI to show the unlock modal.
    /// </summary>
    public virtual void RequestUnlock()
    {
        OnShowUnlockModal?.Invoke();
    }

    /// <summary>
    /// Validates an access code globally for all POCs.
    /// </summary>
    public virtual async Task<AccessCodeResponse> ValidateAccessCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return new AccessCodeResponse
            {
                IsValid = false,
                ErrorMessage = "Access code is required."
            };
        }

        try
        {
            var request = new { code };

            var response = await _httpClient.PostAsJsonAsync(
                "/api/access-code/validate",
                request,
                cancellationToken
            );

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AccessCodeResponse>(cancellationToken: cancellationToken);
                if (result?.IsValid == true && !string.IsNullOrEmpty(result.Token))
                {
                    _globalToken = result.Token;
                    _tokenExpiresAt = result.TokenExpiresAt ?? DateTime.UtcNow.AddHours(24);
                    OnAccessStateChanged?.Invoke();
                }
                return result!;
            }

            // Handle rate limiting
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var result = await response.Content.ReadFromJsonAsync<AccessCodeResponse>(cancellationToken: cancellationToken);
                return result ?? new AccessCodeResponse
                {
                    IsValid = false,
                    ErrorMessage = "Too many attempts. Please try again later."
                };
            }

            // Handle other error status codes
            var errorResult = await response.Content.ReadFromJsonAsync<AccessCodeResponse>(cancellationToken: cancellationToken);
            return errorResult ?? new AccessCodeResponse
            {
                IsValid = false,
                ErrorMessage = "Invalid access code. Please try again."
            };
        }
        catch (HttpRequestException ex)
        {
            return new AccessCodeResponse
            {
                IsValid = false,
                ErrorMessage = $"Network error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new AccessCodeResponse
            {
                IsValid = false,
                ErrorMessage = $"An error occurred: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Gets the global access token (if valid and not expired).
    /// </summary>
    public virtual string? GetAccessToken()
    {
        if (!string.IsNullOrEmpty(_globalToken) && DateTime.UtcNow < _tokenExpiresAt)
        {
            return _globalToken;
        }

        // Token expired, clear it
        _globalToken = null;
        _tokenExpiresAt = DateTime.MinValue;
        return null;
    }

    /// <summary>
    /// Checks if user has valid global access.
    /// </summary>
    public virtual bool HasValidAccess()
    {
        return !string.IsNullOrEmpty(GetAccessToken());
    }

    /// <summary>
    /// Logs out by clearing the global token.
    /// </summary>
    public virtual void Logout()
    {
        _globalToken = null;
        _tokenExpiresAt = DateTime.MinValue;
        OnAccessStateChanged?.Invoke();
    }

    /// <summary>
    /// Gets when the token expires (UTC).
    /// </summary>
    public virtual DateTime GetTokenExpiryTime()
    {
        return _tokenExpiresAt;
    }

    /// <summary>
    /// Refreshes the current token if it's about to expire.
    /// </summary>
    public virtual async Task<bool> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        var currentToken = GetAccessToken();
        if (string.IsNullOrEmpty(currentToken))
        {
            return false;
        }

        try
        {
            var request = new RefreshTokenRequest { Token = currentToken };
            var response = await _httpClient.PostAsJsonAsync(
                "/api/access-code/refresh",
                request,
                cancellationToken
            );

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AccessCodeResponse>(cancellationToken: cancellationToken);
                if (result?.IsValid == true && !string.IsNullOrEmpty(result.Token))
                {
                    _globalToken = result.Token;
                    _tokenExpiresAt = result.TokenExpiresAt ?? DateTime.UtcNow.AddHours(24);
                    return true;
                }
            }
        }
        catch
        {
            // Silently fail - token refresh is optional
        }

        return false;
    }

    /// <summary>
    /// Checks if the token is about to expire (within 1 hour).
    /// </summary>
    public virtual bool IsTokenExpiringSoon()
    {
        if (_tokenExpiresAt == DateTime.MinValue)
            return true;

        return DateTime.UtcNow.AddHours(1) >= _tokenExpiresAt;
    }
}