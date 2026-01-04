// MyPortfolio.Shared/ViewModels/RAGViewModel.cs

using MyPortfolio.Core.Features.RAG.Models;
using MyPortfolio.Shared.Services;
using System.Net.Http.Json;
using System.Text.Json;

namespace MyPortfolio.Shared.ViewModels;

/// <summary>
/// ViewModel for the RAG POC component.
/// Manages session state, document ingestion, and query execution.
/// </summary>
public class RAGViewModel
{
    private readonly HttpClient _httpClient;
    private readonly AccessCodeService _accessCodeService;

    // Session state
    public string? SessionId { get; private set; }
    public DateTime? SessionExpiresAt { get; private set; }
    public int MaxDocuments { get; private set; } = 2;
    public int MaxFileSizeKB { get; private set; } = 100; // Default to 100KB to match backend config

    // Document state
    public List<UploadedDocument> Documents { get; } = new();
    public int TotalChunks { get; private set; }

    // Query state
    public string Query { get; set; } = string.Empty;
    public RAGStrategy SelectedStrategy { get; set; } = RAGStrategy.Naive;
    public string StreamingResponse { get; private set; } = string.Empty;
    public List<Citation> Citations { get; } = new();
    public RAGMetrics? LastMetrics { get; private set; }

    // UI state
    public bool IsCreatingSession { get; private set; }
    public bool IsUploading { get; private set; }
    public bool IsQuerying { get; private set; }
    public string? ErrorMessage { get; set; }
    public string? UploadProgress { get; private set; }
    public double UploadPercentComplete { get; private set; }

    // Events for UI updates
    public event Action? OnStateChanged;

    public RAGViewModel(HttpClient httpClient, AccessCodeService accessCodeService)
    {
        _httpClient = httpClient;
        _accessCodeService = accessCodeService;
    }

    /// <summary>
    /// Creates a new RAG session.
    /// </summary>
    public async Task CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!_accessCodeService.HasValidAccess())
        {
            ErrorMessage = "Access code required. Please unlock features first.";
            _accessCodeService.RequestUnlock();
            return;
        }

        IsCreatingSession = true;
        ErrorMessage = null;
        NotifyStateChanged();

        try
        {
            SetAuthHeader();
            var response = await _httpClient.PostAsJsonAsync(
                "api/rag/session",
                new { },
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<CreateSessionResponse>(cancellationToken: cancellationToken);
                if (result != null)
                {
                    SessionId = result.SessionId;
                    SessionExpiresAt = result.ExpiresAt;
                    MaxDocuments = result.MaxDocuments;
                    MaxFileSizeKB = result.MaxFileSizeBytes / 1024;
                    Documents.Clear();
                    TotalChunks = 0;
                    Citations.Clear();
                    StreamingResponse = string.Empty;
                    LastMetrics = null;
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                ErrorMessage = $"Failed to create session: {errorContent}";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error creating session: {ex.Message}";
        }
        finally
        {
            IsCreatingSession = false;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Uploads and ingests a document into the current session.
    /// </summary>
    public async Task UploadDocumentAsync(string fileName, string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(SessionId))
        {
            ErrorMessage = "No active session. Please create a session first.";
            NotifyStateChanged();
            return;
        }

        if (Documents.Count >= MaxDocuments)
        {
            ErrorMessage = $"Maximum documents ({MaxDocuments}) reached for this session.";
            NotifyStateChanged();
            return;
        }

        IsUploading = true;
        UploadProgress = "Starting upload...";
        UploadPercentComplete = 0;
        ErrorMessage = null;
        NotifyStateChanged();

        try
        {
            SetAuthHeader();
            
            var request = new IngestDocumentRequest
            {
                SessionId = SessionId,
                FileName = fileName,
                Content = content
            };

            // Use SSE for progress updates
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/rag/ingest")
            {
                Content = JsonContent.Create(request)
            };

            using var response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                ErrorMessage = $"Upload failed: {errorContent}";
                return;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("data: "))
                {
                    var json = line[6..];
                    if (json == "[DONE]") break;

                    var update = JsonSerializer.Deserialize<IngestProgressUpdate>(json, 
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    if (update != null)
                    {
                        UploadProgress = update.Message;
                        UploadPercentComplete = update.PercentComplete ?? 0;

                        if (update.Phase == "Error")
                        {
                            ErrorMessage = update.Message;
                            return;
                        }

                        if (update.Phase == "Complete")
                        {
                            Documents.Add(new UploadedDocument
                            {
                                FileName = fileName,
                                CharacterCount = content.Length,
                                UploadedAt = DateTime.UtcNow
                            });
                        }

                        NotifyStateChanged();
                    }
                }
            }

            // Refresh session stats
            await RefreshSessionStatsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Upload was cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error uploading document: {ex.Message}";
        }
        finally
        {
            IsUploading = false;
            UploadProgress = null;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Executes a RAG query with streaming response.
    /// </summary>
    public async Task QueryAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(SessionId))
        {
            ErrorMessage = "No active session. Please create a session first.";
            NotifyStateChanged();
            return;
        }

        if (string.IsNullOrWhiteSpace(Query))
        {
            ErrorMessage = "Please enter a query.";
            NotifyStateChanged();
            return;
        }

        if (TotalChunks == 0)
        {
            ErrorMessage = "No documents uploaded. Please upload documents first.";
            NotifyStateChanged();
            return;
        }

        IsQuerying = true;
        StreamingResponse = string.Empty;
        Citations.Clear();
        LastMetrics = null;
        ErrorMessage = null;
        NotifyStateChanged();

        try
        {
            SetAuthHeader();

            var request = new RAGQueryRequest
            {
                SessionId = SessionId,
                Query = Query,
                Strategy = SelectedStrategy
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/rag/query")
            {
                Content = JsonContent.Create(request)
            };

            using var response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                ErrorMessage = $"Query failed: {errorContent}";
                return;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("data: "))
                {
                    var json = line[6..];
                    if (json == "[DONE]") break;

                    var chunk = JsonSerializer.Deserialize<RAGStreamChunk>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (chunk != null)
                    {
                        switch (chunk.Type)
                        {
                            case "error":
                                ErrorMessage = chunk.Content;
                                break;

                            case "retrieval":
                                // Could show retrieval info in UI
                                break;

                            case "generation":
                                StreamingResponse += chunk.Content ?? string.Empty;
                                break;

                            case "citation":
                                if (chunk.Citation != null)
                                {
                                    Citations.Add(chunk.Citation);
                                }
                                break;

                            case "done":
                                LastMetrics = chunk.Metrics;
                                break;
                        }

                        NotifyStateChanged();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Query was cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error executing query: {ex.Message}";
        }
        finally
        {
            IsQuerying = false;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Refreshes session statistics from the server.
    /// </summary>
    private async Task RefreshSessionStatsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(SessionId)) return;

        try
        {
            SetAuthHeader();
            var response = await _httpClient.GetAsync(
                $"api/rag/stats?sessionId={SessionId}",
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var stats = await response.Content.ReadFromJsonAsync<SessionStatsResponse>(
                    cancellationToken: cancellationToken);
                
                if (stats != null)
                {
                    TotalChunks = stats.ChunkCount;
                    SessionExpiresAt = stats.ExpiresAt;
                }
            }
        }
        catch
        {
            // Silently ignore stats refresh errors
        }
    }

    /// <summary>
    /// Resets the view model state.
    /// </summary>
    public void Reset()
    {
        SessionId = null;
        SessionExpiresAt = null;
        Documents.Clear();
        TotalChunks = 0;
        Query = string.Empty;
        SelectedStrategy = RAGStrategy.Naive;
        StreamingResponse = string.Empty;
        Citations.Clear();
        LastMetrics = null;
        IsCreatingSession = false;
        IsUploading = false;
        IsQuerying = false;
        ErrorMessage = null;
        UploadProgress = null;
        UploadPercentComplete = 0;
        NotifyStateChanged();
    }

    public bool HasActiveSession => !string.IsNullOrEmpty(SessionId);
    public bool CanUpload => HasActiveSession && Documents.Count < MaxDocuments && !IsUploading;
    public bool CanQuery => HasActiveSession && TotalChunks > 0 && !IsQuerying;

    private void SetAuthHeader()
    {
        var token = _accessCodeService.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }

    private void NotifyStateChanged() => OnStateChanged?.Invoke();

    /// <summary>
    /// Represents an uploaded document in the session.
    /// </summary>
    public class UploadedDocument
    {
        public string FileName { get; init; } = string.Empty;
        public int CharacterCount { get; init; }
        public DateTime UploadedAt { get; init; }
    }
}
