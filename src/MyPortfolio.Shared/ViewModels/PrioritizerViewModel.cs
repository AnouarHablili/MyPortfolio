using MyPortfolio.Core.Features.Prioritizer.Models;
using MyPortfolio.Shared.Services;
using System.Net.Http.Json;

namespace MyPortfolio.Shared.ViewModels;

public class PrioritizerViewModel
{
    private readonly HttpClient _httpClient;
    private readonly AccessCodeService _accessCodeService;

    public string Goal { get; set; } = string.Empty;
    public bool IsLoading { get; set; }
    public string? ErrorMessage { get; set; }
    public PrioritizationResponse? Result { get; set; }

    public PrioritizerViewModel(HttpClient httpClient, AccessCodeService accessCodeService)
    {
        _httpClient = httpClient;
        _accessCodeService = accessCodeService;
    }

    public async Task PrioritizeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Goal))
        {
            ErrorMessage = "Please enter a goal to prioritize.";
            return;
        }

        if (!_accessCodeService.HasValidAccess())
        {
            ErrorMessage = "Access code required. Please unlock features first.";
            _accessCodeService.RequestUnlock();
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var token = _accessCodeService.GetAccessToken();
            if (!string.IsNullOrEmpty(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            var response = await _httpClient.PostAsJsonAsync("api/prioritizer/prioritize", Goal, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                Result = await response.Content.ReadFromJsonAsync<PrioritizationResponse>(cancellationToken: cancellationToken);
            }
            else
            {
                var errorMessage = await response.Content.ReadAsStringAsync(cancellationToken);
                ErrorMessage = $"API Error: {errorMessage}";
            }
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "The request was cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"An unexpected error occurred: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Reset()
    {
        Goal = string.Empty;
        IsLoading = false;
        ErrorMessage = null;
        Result = null;
    }
}