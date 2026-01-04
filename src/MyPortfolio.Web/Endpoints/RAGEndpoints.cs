// MyPortfolio.Web/Endpoints/RAGEndpoints.cs

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MyPortfolio.Core.Features.RAG.Abstractions;
using MyPortfolio.Core.Features.RAG.Models;
using MyPortfolio.Core.Shared;

namespace MyPortfolio.Web.Endpoints;

/// <summary>
/// API endpoints for the RAG POC.
/// Demonstrates SSE streaming for real-time progress updates.
/// </summary>
public static class RAGEndpoints
{
    public static void MapRAGEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rag")
            .RequireAuthorization(); // Require access code

        // Create a new RAG session
        group.MapPost("/session", (
            [FromBody] CreateSessionRequest? request,
            IRAGSessionManager sessionManager) =>
        {
            var session = sessionManager.CreateSession(request?.Config);

            return Results.Ok(new CreateSessionResponse
            {
                SessionId = session.SessionId,
                ExpiresAt = session.ExpiresAt,
                MaxDocuments = session.Config.MaxDocuments,
                MaxFileSizeBytes = session.Config.MaxFileSizeBytes
            });
        })
        .WithName("CreateRAGSession");

        // Ingest a document (with SSE progress streaming)
        group.MapPost("/ingest", async (
            HttpContext context,
            [FromBody] IngestDocumentRequest request,
            IRAGSessionManager sessionManager,
            IRAGOrchestrator orchestrator,
            CancellationToken cancellationToken) =>
        {
            // Validate session
            var session = sessionManager.GetSession(request.SessionId);
            if (session == null)
            {
                return Results.NotFound($"Session {request.SessionId} not found or expired");
            }

            // Validate request
            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return Results.BadRequest("Document content cannot be empty");
            }

            if (string.IsNullOrWhiteSpace(request.FileName))
            {
                return Results.BadRequest("File name is required");
            }

            // Set up SSE response
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            try
            {
                await foreach (var update in orchestrator.IngestDocumentAsync(session, request, cancellationToken))
                {
                    var json = JsonSerializer.Serialize(update, jsonOptions);
                    await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }

                await context.Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected
            }

            return Results.Empty;
        })
        .WithName("IngestDocument");

        // Query the RAG system (with SSE streaming)
        group.MapPost("/query", async (
            HttpContext context,
            [FromBody] RAGQueryRequest request,
            IRAGSessionManager sessionManager,
            IRAGOrchestrator orchestrator,
            CancellationToken cancellationToken) =>
        {
            // Validate session
            var session = sessionManager.GetSession(request.SessionId);
            if (session == null)
            {
                return Results.NotFound($"Session {request.SessionId} not found or expired");
            }

            // Validate request
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.BadRequest("Query cannot be empty");
            }

            // Set up SSE response
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            try
            {
                await foreach (var chunk in orchestrator.QueryAsync(session, request, cancellationToken))
                {
                    var json = JsonSerializer.Serialize(chunk, jsonOptions);
                    await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }

                await context.Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected
            }

            return Results.Empty;
        })
        .WithName("QueryRAG");

        // Get session statistics
        group.MapGet("/stats", (
            [FromQuery] string sessionId,
            IRAGSessionManager sessionManager) =>
        {
            var session = sessionManager.GetSession(sessionId);
            if (session == null)
            {
                return Results.NotFound($"Session {sessionId} not found or expired");
            }

            var documentSummaries = session.Documents
                .Select(d => new DocumentSummary
                {
                    Id = d.Id,
                    FileName = d.FileName,
                    CharacterCount = d.CharacterCount,
                    ChunkCount = session.VectorIndex.Count(c => c.Chunk.DocumentId == d.Id),
                    UploadedAt = d.UploadedAt
                })
                .ToList();

            return Results.Ok(new SessionStatsResponse
            {
                SessionId = session.SessionId,
                CreatedAt = session.CreatedAt,
                ExpiresAt = session.ExpiresAt,
                DocumentCount = session.DocumentCount,
                ChunkCount = session.ChunkCount,
                SessionMetrics = session.SessionMetrics,
                Documents = documentSummaries
            });
        })
        .WithName("GetRAGStats");

        // Get global RAG statistics (for monitoring)
        group.MapGet("/global-stats", (IRAGSessionManager sessionManager) =>
        {
            var (activeSessions, totalDocuments, totalChunks) = sessionManager.GetGlobalStats();

            return Results.Ok(new
            {
                activeSessions,
                totalDocuments,
                totalChunks
            });
        })
        .WithName("GetGlobalRAGStats");

        // Delete a session
        group.MapDelete("/session/{sessionId}", (
            string sessionId,
            IRAGSessionManager sessionManager) =>
        {
            var removed = sessionManager.RemoveSession(sessionId);
            return removed ? Results.Ok() : Results.NotFound($"Session {sessionId} not found");
        })
        .WithName("DeleteRAGSession");
    }
}
