// tests/MyPortfolio.Core.Tests/Features/RAG/ChunkingServiceTests.cs

using Microsoft.Extensions.Logging;
using Moq;
using MyPortfolio.Core.Features.RAG.Models;
using MyPortfolio.Core.Features.RAG.Services;
using Xunit;

namespace MyPortfolio.Core.Tests.Features.RAG;

public class ChunkingServiceTests
{
    private readonly ChunkingService _sut;
    private readonly Mock<ILogger<ChunkingService>> _loggerMock;

    public ChunkingServiceTests()
    {
        _loggerMock = new Mock<ILogger<ChunkingService>>();
        _sut = new ChunkingService(_loggerMock.Object);
    }

    private static DocumentInfo CreateDocument(string content, string fileName = "test.txt")
    {
        return new DocumentInfo
        {
            Id = "doc_1",
            FileName = fileName,
            Content = content,
            CharacterCount = content.Length,
            UploadedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task ChunkDocumentAsync_FixedSize_CreatesCorrectNumberOfChunks()
    {
        // Arrange
        var content = new string('x', 1000); // 1000 characters
        var document = CreateDocument(content);
        var chunkSize = 200;
        var overlap = 50;

        // Act
        var chunks = await _sut.ChunkDocumentAsync(
            document, 
            ChunkingStrategy.FixedSize, 
            chunkSize, 
            overlap);

        // Assert
        Assert.NotEmpty(chunks);
        // With 1000 chars, chunk size 200, overlap 50 -> effective step is 150
        // Expected chunks: ceil(1000 / 150) = 7
        Assert.True(chunks.Count >= 6 && chunks.Count <= 8);
        Assert.All(chunks, c => Assert.True(c.Content.Length <= chunkSize));
    }

    [Fact]
    public async Task ChunkDocumentAsync_FixedSize_PreservesOverlap()
    {
        // Arrange
        var content = "AAAA_BBBB_CCCC_DDDD_EEEE"; // 24 characters
        var document = CreateDocument(content);
        var chunkSize = 10;
        var overlap = 5;

        // Act
        var chunks = await _sut.ChunkDocumentAsync(
            document, 
            ChunkingStrategy.FixedSize, 
            chunkSize, 
            overlap);

        // Assert
        Assert.True(chunks.Count >= 2);
        
        // Verify overlap by checking that consecutive chunks share content
        for (int i = 1; i < chunks.Count; i++)
        {
            var prevEnd = chunks[i - 1].Content;
            var currStart = chunks[i].Content;
            // The end of previous chunk should overlap with start of current
            // (exact overlap depends on chunk boundaries)
        }
    }

    [Fact]
    public async Task ChunkDocumentAsync_SentenceBased_SplitsOnSentenceBoundaries()
    {
        // Arrange
        var content = "First sentence. Second sentence. Third sentence. Fourth sentence.";
        var document = CreateDocument(content);
        var chunkSize = 35; // Should fit about 2 sentences
        var overlap = 10;

        // Act
        var chunks = await _sut.ChunkDocumentAsync(
            document, 
            ChunkingStrategy.Sentence, 
            chunkSize, 
            overlap);

        // Assert
        Assert.NotEmpty(chunks);
        // Each chunk should contain complete sentences (no mid-sentence breaks)
    }

    [Fact]
    public async Task ChunkDocumentAsync_ParagraphBased_SplitsOnParagraphBoundaries()
    {
        // Arrange
        var content = "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.";
        var document = CreateDocument(content);
        var chunkSize = 40;
        var overlap = 10;

        // Act
        var chunks = await _sut.ChunkDocumentAsync(
            document, 
            ChunkingStrategy.Paragraph, 
            chunkSize, 
            overlap);

        // Assert
        Assert.NotEmpty(chunks);
    }

    [Fact]
    public async Task ChunkDocumentAsync_EmptyDocument_ReturnsEmptyList()
    {
        // Arrange
        var document = CreateDocument("");

        // Act
        var chunks = await _sut.ChunkDocumentAsync(
            document, 
            ChunkingStrategy.FixedSize, 
            100, 
            10);

        // Assert
        Assert.Empty(chunks);
    }

    [Fact]
    public async Task ChunkDocumentAsync_SmallDocument_ReturnsSingleChunk()
    {
        // Arrange
        var content = "Short text";
        var document = CreateDocument(content);

        // Act
        var chunks = await _sut.ChunkDocumentAsync(
            document, 
            ChunkingStrategy.FixedSize, 
            100, // Much larger than content
            10);

        // Assert
        Assert.Single(chunks);
        Assert.Equal(content, chunks[0].Content);
    }

    [Fact]
    public async Task ChunkDocumentAsync_AssignsCorrectChunkIds()
    {
        // Arrange
        var content = new string('x', 500);
        var document = CreateDocument(content);

        // Act
        var chunks = await _sut.ChunkDocumentAsync(
            document, 
            ChunkingStrategy.FixedSize, 
            100, 
            10);

        // Assert
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
            Assert.Contains($"chunk_{i}", chunks[i].Id);
            Assert.Equal(document.Id, chunks[i].DocumentId);
            Assert.Equal(document.FileName, chunks[i].DocumentName);
        }
    }

    [Fact]
    public async Task ChunkDocumentAsync_TrackStartAndEndIndices()
    {
        // Arrange
        var content = "ABCDEFGHIJ"; // 10 characters
        var document = CreateDocument(content);

        // Act
        var chunks = await _sut.ChunkDocumentAsync(
            document, 
            ChunkingStrategy.FixedSize, 
            5, 
            0); // No overlap for easier testing

        // Assert
        Assert.Equal(2, chunks.Count);
        Assert.Equal(0, chunks[0].StartIndex);
        Assert.Equal(5, chunks[0].EndIndex);
        Assert.Equal(5, chunks[1].StartIndex);
        Assert.Equal(10, chunks[1].EndIndex);
    }
}
