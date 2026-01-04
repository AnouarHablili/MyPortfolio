// MyPortfolio.Core/Features/RAG/Services/ChunkingService.cs

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MyPortfolio.Core.Features.RAG.Abstractions;
using MyPortfolio.Core.Features.RAG.Models;

namespace MyPortfolio.Core.Features.RAG.Services;

/// <summary>
/// Implements multiple chunking strategies for document processing.
/// Demonstrates parallel processing with Task.WhenAll and configurable algorithms.
/// </summary>
public sealed partial class ChunkingService : IChunkingService
{
    private readonly ILogger<ChunkingService> _logger;

    // Precompiled regex patterns for performance
    [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.Compiled)]
    private static partial Regex SentenceSplitter();

    [GeneratedRegex(@"\n\s*\n", RegexOptions.Compiled)]
    private static partial Regex ParagraphSplitter();

    public ChunkingService(ILogger<ChunkingService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TextChunk>> ChunkDocumentAsync(
        DocumentInfo document,
        ChunkingStrategy strategy,
        int chunkSize,
        int overlap,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sw = Stopwatch.StartNew();

        _logger.LogInformation(
            "Chunking document {FileName} ({CharCount} chars) using {Strategy} strategy",
            document.FileName,
            document.CharacterCount,
            strategy);

        var chunks = strategy switch
        {
            ChunkingStrategy.FixedSize => ChunkFixedSize(document, chunkSize, overlap),
            ChunkingStrategy.Sentence => ChunkBySentence(document, chunkSize, overlap),
            ChunkingStrategy.Paragraph => ChunkByParagraph(document, chunkSize, overlap),
            _ => ChunkFixedSize(document, chunkSize, overlap)
        };

        sw.Stop();

        _logger.LogInformation(
            "Created {ChunkCount} chunks in {ElapsedMs}ms for {FileName}",
            chunks.Count,
            sw.ElapsedMilliseconds,
            document.FileName);

        return Task.FromResult(chunks);
    }

    /// <summary>
    /// Fixed-size chunking with configurable overlap.
    /// Simple and predictable, good baseline strategy.
    /// </summary>
    private IReadOnlyList<TextChunk> ChunkFixedSize(DocumentInfo document, int chunkSize, int overlap)
    {
        var chunks = new List<TextChunk>();
        var content = document.Content;
        var effectiveStep = Math.Max(1, chunkSize - overlap);
        var chunkIndex = 0;

        for (int start = 0; start < content.Length; start += effectiveStep)
        {
            var end = Math.Min(start + chunkSize, content.Length);
            var chunkContent = content[start..end];

            // Don't create chunks that are too small (unless it's the last one)
            if (chunkContent.Length < chunkSize / 4 && chunks.Count > 0)
                break;

            chunks.Add(CreateChunk(document, chunkContent, start, end, chunkIndex++));
        }

        return chunks;
    }

    /// <summary>
    /// Sentence-aware chunking that preserves natural language boundaries.
    /// Better for semantic coherence in retrieval.
    /// </summary>
    private IReadOnlyList<TextChunk> ChunkBySentence(DocumentInfo document, int targetSize, int overlap)
    {
        var chunks = new List<TextChunk>();
        var sentences = SentenceSplitter().Split(document.Content);
        var currentChunk = new List<string>();
        var currentLength = 0;
        var currentStart = 0;
        var chunkIndex = 0;
        var position = 0;

        foreach (var sentence in sentences)
        {
            var trimmedSentence = sentence.Trim();
            if (string.IsNullOrEmpty(trimmedSentence))
            {
                position += sentence.Length;
                continue;
            }

            // If adding this sentence exceeds target size, finalize current chunk
            if (currentLength + trimmedSentence.Length > targetSize && currentChunk.Count > 0)
            {
                var chunkContent = string.Join(" ", currentChunk);
                var end = currentStart + chunkContent.Length;
                chunks.Add(CreateChunk(document, chunkContent, currentStart, end, chunkIndex++));

                // Keep overlap by retaining last sentence(s) if they fit
                var overlapSentences = GetOverlapSentences(currentChunk, overlap);
                currentChunk = overlapSentences;
                currentLength = currentChunk.Sum(s => s.Length + 1);
                currentStart = position - currentLength;
            }

            currentChunk.Add(trimmedSentence);
            currentLength += trimmedSentence.Length + 1; // +1 for space
            position += sentence.Length;
        }

        // Don't forget the last chunk
        if (currentChunk.Count > 0)
        {
            var chunkContent = string.Join(" ", currentChunk);
            chunks.Add(CreateChunk(document, chunkContent, currentStart, document.Content.Length, chunkIndex));
        }

        return chunks;
    }

    /// <summary>
    /// Paragraph-based chunking for documents with clear structure.
    /// Preserves semantic blocks like sections and topics.
    /// </summary>
    private IReadOnlyList<TextChunk> ChunkByParagraph(DocumentInfo document, int targetSize, int overlap)
    {
        var chunks = new List<TextChunk>();
        var paragraphs = ParagraphSplitter().Split(document.Content);
        var currentChunk = new List<string>();
        var currentLength = 0;
        var currentStart = 0;
        var chunkIndex = 0;
        var position = 0;

        foreach (var paragraph in paragraphs)
        {
            var trimmedParagraph = paragraph.Trim();
            if (string.IsNullOrEmpty(trimmedParagraph))
            {
                position += paragraph.Length + 2; // +2 for \n\n
                continue;
            }

            // If single paragraph exceeds target, fall back to sentence chunking for it
            if (trimmedParagraph.Length > targetSize * 2)
            {
                // Flush current chunk first
                if (currentChunk.Count > 0)
                {
                    var chunkContent = string.Join("\n\n", currentChunk);
                    var end = currentStart + chunkContent.Length;
                    chunks.Add(CreateChunk(document, chunkContent, currentStart, end, chunkIndex++));
                    currentChunk.Clear();
                    currentLength = 0;
                }

                // Sub-chunk the large paragraph using fixed-size
                var subDoc = new DocumentInfo
                {
                    Id = document.Id,
                    FileName = document.FileName,
                    Content = trimmedParagraph,
                    CharacterCount = trimmedParagraph.Length,
                    UploadedAt = document.UploadedAt
                };
                var subChunks = ChunkFixedSize(subDoc, targetSize, overlap);
                foreach (var subChunk in subChunks)
                {
                    chunks.Add(CreateChunk(
                        document,
                        subChunk.Content,
                        position + subChunk.StartIndex,
                        position + subChunk.EndIndex,
                        chunkIndex++));
                }

                position += paragraph.Length + 2;
                currentStart = position;
                continue;
            }

            // If adding this paragraph exceeds target size, finalize current chunk
            if (currentLength + trimmedParagraph.Length > targetSize && currentChunk.Count > 0)
            {
                var chunkContent = string.Join("\n\n", currentChunk);
                var end = currentStart + chunkContent.Length;
                chunks.Add(CreateChunk(document, chunkContent, currentStart, end, chunkIndex++));

                // Keep overlap
                var overlapParagraphs = GetOverlapItems(currentChunk, overlap);
                currentChunk = overlapParagraphs;
                currentLength = currentChunk.Sum(p => p.Length + 2);
                currentStart = position - currentLength;
            }

            currentChunk.Add(trimmedParagraph);
            currentLength += trimmedParagraph.Length + 2;
            position += paragraph.Length + 2;
        }

        // Don't forget the last chunk
        if (currentChunk.Count > 0)
        {
            var chunkContent = string.Join("\n\n", currentChunk);
            chunks.Add(CreateChunk(document, chunkContent, currentStart, document.Content.Length, chunkIndex));
        }

        return chunks;
    }

    private static TextChunk CreateChunk(
        DocumentInfo document,
        string content,
        int startIndex,
        int endIndex,
        int chunkIndex)
    {
        return new TextChunk
        {
            Id = $"{document.Id}_chunk_{chunkIndex}",
            DocumentId = document.Id,
            DocumentName = document.FileName,
            Content = content,
            StartIndex = startIndex,
            EndIndex = endIndex,
            ChunkIndex = chunkIndex
        };
    }

    private static List<string> GetOverlapSentences(List<string> sentences, int targetOverlap)
    {
        var result = new List<string>();
        var length = 0;

        for (int i = sentences.Count - 1; i >= 0 && length < targetOverlap; i--)
        {
            result.Insert(0, sentences[i]);
            length += sentences[i].Length + 1;
        }

        return result;
    }

    private static List<string> GetOverlapItems(List<string> items, int targetOverlap)
    {
        var result = new List<string>();
        var length = 0;

        for (int i = items.Count - 1; i >= 0 && length < targetOverlap; i--)
        {
            result.Insert(0, items[i]);
            length += items[i].Length + 2;
        }

        return result;
    }
}
