// MyPortfolio.Core/Features/RAG/Services/RAGSessionManager.cs

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MyPortfolio.Core.Features.RAG.Abstractions;
using MyPortfolio.Core.Features.RAG.Models;

namespace MyPortfolio.Core.Features.RAG.Services;

/// <summary>
/// Manages RAG sessions stored in-memory with TTL expiration.
/// 
/// Demonstrates:
/// - IMemoryCache for session storage with sliding expiration
/// - Thread-safe session creation/retrieval
/// - Memory-efficient cleanup on expiration
/// </summary>
public sealed class RAGSessionManager : IRAGSessionManager
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<RAGSessionManager> _logger;
    
    // Track active session IDs for statistics (cache doesn't expose keys)
    private readonly ConcurrentDictionary<string, DateTime> _sessionRegistry = new();

    // Default configuration for new sessions (demo limits)
    private static readonly RAGSessionConfig DefaultConfig = new()
    {
        SessionTTL = TimeSpan.FromMinutes(15),
        MaxDocuments = 2,
        MaxFileSizeBytes = 100 * 1024, // 100KB - increased for better demo experience
        ChunkSize = 512,
        ChunkOverlap = 50,
        TopK = 5,
        MinSimilarityScore = 0.3f,
        DefaultStrategy = RAGStrategy.Naive,
        DefaultChunkingStrategy = ChunkingStrategy.FixedSize,
        MaxConcurrentEmbeddings = 5
    };

    public RAGSessionManager(IMemoryCache cache, ILogger<RAGSessionManager> logger)
    {
        _cache = cache;
        _logger = logger;
        _logger.LogInformation("RAGSessionManager initialized with default TTL: {TTL} minutes",
            DefaultConfig.SessionTTL.TotalMinutes);
    }

    /// <inheritdoc/>
    public RAGSession CreateSession(RAGSessionConfig? config = null)
    {
        var effectiveConfig = config ?? DefaultConfig;
        var sessionId = GenerateSessionId();
        var session = new RAGSession(sessionId, effectiveConfig);

        // Configure cache entry with sliding expiration
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(effectiveConfig.SessionTTL)
            .RegisterPostEvictionCallback(OnSessionEvicted);

        _cache.Set(sessionId, session, cacheOptions);
        _sessionRegistry[sessionId] = session.CreatedAt;

        _logger.LogInformation(
            "Created session {SessionId}. TTL: {TTL} min, MaxDocs: {MaxDocs}, MaxSize: {MaxSize}KB",
            sessionId,
            effectiveConfig.SessionTTL.TotalMinutes,
            effectiveConfig.MaxDocuments,
            effectiveConfig.MaxFileSizeBytes / 1024);

        return session;
    }

    /// <inheritdoc/>
    public RAGSession? GetSession(string sessionId)
    {
        if (_cache.TryGetValue<RAGSession>(sessionId, out var session))
        {
            // Session access extends sliding expiration automatically via IMemoryCache
            session!.Touch();
            _logger.LogDebug("Retrieved session {SessionId}. Docs: {DocCount}, Chunks: {ChunkCount}",
                sessionId, session.DocumentCount, session.ChunkCount);
            return session;
        }

        _logger.LogWarning("Session {SessionId} not found or expired", sessionId);
        return null;
    }

    /// <inheritdoc/>
    public bool RemoveSession(string sessionId)
    {
        _cache.Remove(sessionId);
        var removed = _sessionRegistry.TryRemove(sessionId, out _);
        
        if (removed)
        {
            _logger.LogInformation("Removed session {SessionId}", sessionId);
        }
        
        return removed;
    }

    /// <inheritdoc/>
    public (int ActiveSessions, int TotalDocuments, int TotalChunks) GetGlobalStats()
    {
        // Clean up expired sessions from registry
        var expiredSessions = _sessionRegistry
            .Where(kvp => !_cache.TryGetValue<RAGSession>(kvp.Key, out _))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var expired in expiredSessions)
        {
            _sessionRegistry.TryRemove(expired, out _);
        }

        var activeSessions = 0;
        var totalDocuments = 0;
        var totalChunks = 0;

        foreach (var sessionId in _sessionRegistry.Keys)
        {
            if (_cache.TryGetValue<RAGSession>(sessionId, out var session))
            {
                activeSessions++;
                totalDocuments += session!.DocumentCount;
                totalChunks += session.ChunkCount;
            }
        }

        return (activeSessions, totalDocuments, totalChunks);
    }

    private void OnSessionEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        var sessionId = key.ToString();
        _sessionRegistry.TryRemove(sessionId!, out _);
        
        _logger.LogInformation(
            "Session {SessionId} evicted. Reason: {Reason}",
            sessionId,
            reason);
    }

    private static string GenerateSessionId()
    {
        // Generate a URL-safe session ID
        return $"rag_{Guid.NewGuid():N}"[..20];
    }
}
