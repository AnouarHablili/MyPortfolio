// tests/MyPortfolio.Core.Tests/Features/RAG/VectorStoreTests.cs

using Microsoft.Extensions.Logging;
using Moq;
using MyPortfolio.Core.Features.RAG.Models;
using MyPortfolio.Core.Features.RAG.Services;
using Xunit;

namespace MyPortfolio.Core.Tests.Features.RAG;

public class VectorStoreTests
{
    private readonly VectorStore _sut;
    private readonly Mock<ILogger<VectorStore>> _loggerMock;

    public VectorStoreTests()
    {
        _loggerMock = new Mock<ILogger<VectorStore>>();
        _sut = new VectorStore(_loggerMock.Object);
    }

    private static RAGSession CreateSession()
    {
        return new RAGSession("test_session", new RAGSessionConfig());
    }

    private static ChunkWithEmbedding CreateChunk(string id, float[] embedding)
    {
        return new ChunkWithEmbedding
        {
            Chunk = new TextChunk
            {
                Id = id,
                DocumentId = "doc_1",
                DocumentName = "test.txt",
                Content = $"Content for {id}",
                StartIndex = 0,
                EndIndex = 100,
                ChunkIndex = 0
            },
            Embedding = embedding
        };
    }

    [Fact]
    public void CosineSimilaritySIMD_IdenticalVectors_ReturnsOne()
    {
        // Arrange
        float[] vector = [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f, 7.0f, 8.0f];

        // Act
        var similarity = VectorStore.CosineSimilaritySIMD(vector, vector);

        // Assert
        Assert.Equal(1.0f, similarity, 5);
    }

    [Fact]
    public void CosineSimilaritySIMD_OrthogonalVectors_ReturnsZero()
    {
        // Arrange
        float[] vectorA = [1.0f, 0.0f, 0.0f, 0.0f];
        float[] vectorB = [0.0f, 1.0f, 0.0f, 0.0f];

        // Act
        var similarity = VectorStore.CosineSimilaritySIMD(vectorA, vectorB);

        // Assert
        Assert.Equal(0.0f, similarity, 5);
    }

    [Fact]
    public void CosineSimilaritySIMD_OppositeVectors_ReturnsNegativeOne()
    {
        // Arrange
        float[] vectorA = [1.0f, 2.0f, 3.0f, 4.0f];
        float[] vectorB = [-1.0f, -2.0f, -3.0f, -4.0f];

        // Act
        var similarity = VectorStore.CosineSimilaritySIMD(vectorA, vectorB);

        // Assert
        Assert.Equal(-1.0f, similarity, 5);
    }

    [Fact]
    public void CosineSimilaritySIMD_SimilarVectors_ReturnsHighScore()
    {
        // Arrange
        float[] vectorA = [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f, 7.0f, 8.0f];
        float[] vectorB = [1.1f, 2.1f, 3.1f, 4.1f, 5.1f, 6.1f, 7.1f, 8.1f];

        // Act
        var similarity = VectorStore.CosineSimilaritySIMD(vectorA, vectorB);

        // Assert
        Assert.True(similarity > 0.99f);
    }

    [Fact]
    public void CosineSimilaritySIMD_EmptyVectors_ReturnsZero()
    {
        // Arrange
        float[] empty = Array.Empty<float>();

        // Act
        var similarity = VectorStore.CosineSimilaritySIMD(empty, empty);

        // Assert
        Assert.Equal(0.0f, similarity);
    }

    [Fact]
    public void CosineSimilaritySIMD_DifferentLengths_ThrowsException()
    {
        // Arrange
        float[] vectorA = [1.0f, 2.0f, 3.0f];
        float[] vectorB = [1.0f, 2.0f];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            VectorStore.CosineSimilaritySIMD(vectorA, vectorB));
    }

    [Fact]
    public void CosineSimilaritySIMD_LargeVectors_MatchesScalarImplementation()
    {
        // Arrange - Create vectors larger than SIMD width
        var random = new Random(42);
        var vectorA = Enumerable.Range(0, 256).Select(_ => (float)random.NextDouble()).ToArray();
        var vectorB = Enumerable.Range(0, 256).Select(_ => (float)random.NextDouble()).ToArray();

        // Act
        var simdResult = VectorStore.CosineSimilaritySIMD(vectorA, vectorB);
        var scalarResult = VectorStore.CosineSimilarityScalar(vectorA, vectorB);

        // Assert - Results should be nearly identical
        Assert.Equal(scalarResult, simdResult, 4);
    }

    [Fact]
    public void AddChunk_AddsToVectorIndex()
    {
        // Arrange
        var session = CreateSession();
        var chunk = CreateChunk("chunk_1", [1.0f, 2.0f, 3.0f]);

        // Act
        _sut.AddChunk(session, chunk);

        // Assert
        Assert.Single(session.VectorIndex);
    }

    [Fact]
    public void AddChunks_AddsMultipleToVectorIndex()
    {
        // Arrange
        var session = CreateSession();
        var chunks = new[]
        {
            CreateChunk("chunk_1", [1.0f, 0.0f, 0.0f]),
            CreateChunk("chunk_2", [0.0f, 1.0f, 0.0f]),
            CreateChunk("chunk_3", [0.0f, 0.0f, 1.0f])
        };

        // Act
        _sut.AddChunks(session, chunks);

        // Assert
        Assert.Equal(3, session.VectorIndex.Count);
    }

    [Fact]
    public void Search_EmptyIndex_ReturnsEmptyResults()
    {
        // Arrange
        var session = CreateSession();
        float[] query = [1.0f, 2.0f, 3.0f];

        // Act
        var results = _sut.Search(session, query, topK: 5);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Search_FindsMostSimilarChunks()
    {
        // Arrange
        var session = CreateSession();
        
        // Add chunks with different embeddings
        _sut.AddChunk(session, CreateChunk("chunk_1", [1.0f, 0.0f, 0.0f])); // X axis
        _sut.AddChunk(session, CreateChunk("chunk_2", [0.0f, 1.0f, 0.0f])); // Y axis
        _sut.AddChunk(session, CreateChunk("chunk_3", [0.707f, 0.707f, 0.0f])); // 45 degrees

        // Query closer to X axis
        float[] query = [0.9f, 0.1f, 0.0f];

        // Act
        var results = _sut.Search(session, query, topK: 3);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal("chunk_1", results[0].Chunk.Id); // Most similar (X axis)
        Assert.Equal(1, results[0].Rank);
    }

    [Fact]
    public void Search_RespectsTopK()
    {
        // Arrange
        var session = CreateSession();
        for (int i = 0; i < 10; i++)
        {
            _sut.AddChunk(session, CreateChunk($"chunk_{i}", [(float)i, 0, 0]));
        }
        float[] query = [5.0f, 0, 0];

        // Act
        var results = _sut.Search(session, query, topK: 3);

        // Assert
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void Search_RespectsMinScore()
    {
        // Arrange
        var session = CreateSession();
        _sut.AddChunk(session, CreateChunk("chunk_1", [1.0f, 0.0f, 0.0f]));
        _sut.AddChunk(session, CreateChunk("chunk_2", [0.0f, 1.0f, 0.0f])); // Orthogonal, score = 0

        float[] query = [1.0f, 0.0f, 0.0f];

        // Act
        var results = _sut.Search(session, query, topK: 10, minScore: 0.5f);

        // Assert
        Assert.Single(results);
        Assert.Equal("chunk_1", results[0].Chunk.Id);
    }

    [Fact]
    public void Search_AssignsCorrectRanks()
    {
        // Arrange
        var session = CreateSession();
        _sut.AddChunk(session, CreateChunk("chunk_1", [1.0f, 0.0f, 0.0f]));
        _sut.AddChunk(session, CreateChunk("chunk_2", [0.8f, 0.6f, 0.0f]));
        _sut.AddChunk(session, CreateChunk("chunk_3", [0.5f, 0.5f, 0.707f]));

        float[] query = [1.0f, 0.0f, 0.0f];

        // Act
        var results = _sut.Search(session, query, topK: 3);

        // Assert
        for (int i = 0; i < results.Count; i++)
        {
            Assert.Equal(i + 1, results[i].Rank);
        }
        
        // Verify scores are in descending order
        for (int i = 1; i < results.Count; i++)
        {
            Assert.True(results[i - 1].SimilarityScore >= results[i].SimilarityScore);
        }
    }

    [Fact]
    public void EuclideanDistanceSIMD_IdenticalVectors_ReturnsZero()
    {
        // Arrange
        float[] vector = [1.0f, 2.0f, 3.0f, 4.0f];

        // Act
        var distance = VectorStore.EuclideanDistanceSIMD(vector, vector);

        // Assert
        Assert.Equal(0.0f, distance, 5);
    }

    [Fact]
    public void EuclideanDistanceSIMD_KnownDistance_ReturnsCorrectValue()
    {
        // Arrange
        float[] vectorA = [0.0f, 0.0f, 0.0f];
        float[] vectorB = [3.0f, 4.0f, 0.0f]; // Distance should be 5 (3-4-5 triangle)

        // Act
        var distance = VectorStore.EuclideanDistanceSIMD(vectorA, vectorB);

        // Assert
        Assert.Equal(5.0f, distance, 5);
    }
}
