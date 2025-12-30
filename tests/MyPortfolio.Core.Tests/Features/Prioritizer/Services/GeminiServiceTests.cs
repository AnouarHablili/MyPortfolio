using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyPortfolio.Core.Abstractions;
using MyPortfolio.Core.Features.Prioritizer.Models;
using MyPortfolio.Core.Features.Prioritizer.Services;
using RichardSzalay.MockHttp;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace MyPortfolio.Core.Tests.Features.Prioritizer.Services;

public class GeminiServiceTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHttp;
    //private readonly IConfiguration? _testConfiguration;
    private readonly IAIService _geminiService;
    private readonly ITestOutputHelper _output;
    private const string ApiKey = "TEST_API_KEY";
    private const string ApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:streamGenerateContent";
    private readonly HttpClient _httpClient;

    // Note: The dependency on IConfiguration has been permanently removed from the test class.

    public GeminiServiceTests(ITestOutputHelper output)
    {
        _output = output;

        // 1. Setup Mock HTTP Client (no change)
        _mockHttp = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHttp)
        {
            BaseAddress = new Uri("http://test-host/")
        };

        // 2. Setup the IOptions<GeminiOptions> dependency (THE CRITICAL FIX)

        // Define the simple options object we want the service to receive.
        var optionsInstance = new GeminiOptions
        {
            ApiKey = "Test-ApiKey-For-Unit-Tests"
        };

        // Wrap the options object using the standard Options.Create helper method.
        var optionsWrapper = Options.Create(optionsInstance);


        // 3. Create a logger for the service
        var logger = NullLogger<GeminiService>.Instance;

        // 4. Initialize the service with the new, correct dependencies
        _geminiService = new GeminiService(_httpClient, optionsWrapper, logger);
    }

    [Fact]
    public async Task PrioritizeGoalAsync_ShouldReturnFailure_OnHttpError()
    {
        _mockHttp.When(HttpMethod.Post, ApiUrl)
                 .Respond(System.Net.HttpStatusCode.InternalServerError, "application/json", "Internal Server Error");

        var goal = "Simulating an error request";
        var result = await _geminiService.PrioritizeGoalAsync(goal);

        _output.WriteLine($"Result IsSuccess: {result.IsSuccess}");
        _output.WriteLine($"Result Error: {result.Error}");

        Assert.False(result.IsSuccess);
        Assert.Contains("API Request Failed", result.Error);
    }

    [Fact]
    public async Task PrioritizeGoalAsync_ShouldReturnSuccess_OnValidStreamingResponse()
    {
        // Using "tasks" in JSON (mapped to TaskItems in C# via JsonPropertyName)
        var streamingResponse = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"tasks\\\":[{\\\"rank\\\":1,\\\"taskTitle\\\":\\\"Define MVP\\\",\\\"reasoningChain\\\":\\\"First, you must clarify the minimum viable product to avoid scope creep and align stakeholders.\\\",\\\"estimate\\\":\\\"2-3 days\\\"}],\\\"executiveSummary\\\":\\\"Initial validation phase\\\"}\"}],\"role\":\"model\"}}],\"finishReason\":\"STOP\"}\n";

        _mockHttp
            .When(HttpMethod.Post, ApiUrl)
            .Respond("application/json", streamingResponse);

        var result = await _geminiService.PrioritizeGoalAsync("Build and launch a mobile app in 6 months");

        _output.WriteLine($"Result IsSuccess: {result.IsSuccess}");
        _output.WriteLine($"Result Error: {result.Error}");

        Assert.True(result.IsSuccess, $"Expected success but got error: {result.Error}");
        Assert.NotNull(result.Value);

        var response = result.Value!;
        Assert.Equal("Initial validation phase", response.ExecutiveSummary);
        Assert.Single(response.TaskItems);
        Assert.Equal(1, response.TaskItems.First().Rank);
        Assert.Equal("Define MVP", response.TaskItems.First().TaskTitle);
    }

    [Fact]
    public async Task PrioritizeGoalAsync_ShouldParseRankAndTaskTitle_FromStreamingResponse()
    {
        var streamingResponse = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"tasks\\\":[{\\\"rank\\\":2,\\\"taskTitle\\\":\\\"Research Technologies\\\",\\\"reasoningChain\\\":\\\"Identify the most suitable technologies for the mobile app based on requirements and constraints.\\\",\\\"estimate\\\":\\\"3-5 days\\\"}],\\\"executiveSummary\\\":\\\"Technology research phase\\\"}\"}],\"role\":\"model\"}}],\"finishReason\":\"STOP\"}\n";

        _mockHttp
            .When(HttpMethod.Post, ApiUrl)
            .Respond("application/json", streamingResponse);

        var result = await _geminiService.PrioritizeGoalAsync("Build and launch a mobile app in 6 months");

        _output.WriteLine($"Result IsSuccess: {result.IsSuccess}");
        _output.WriteLine($"Result Error: {result.Error}");

        Assert.True(result.IsSuccess, $"Expected success but got error: {result.Error}");
        Assert.NotNull(result.Value);

        var response = result.Value!;
        Assert.Equal("Technology research phase", response.ExecutiveSummary);
        Assert.Single(response.TaskItems);
        Assert.Equal(2, response.TaskItems.First().Rank);
        Assert.Equal("Research Technologies", response.TaskItems.First().TaskTitle);
    }

    [Fact]
    public async Task PrioritizeGoalAsync_ShouldHandleEmptyTaskList_InStreamingResponse()
    {
        var streamingResponse = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"tasks\\\":[],\\\"executiveSummary\\\":\\\"No tasks were found for the given goal. Maybe it's already well-defined or too vague.\\\"}\"}],\"role\":\"model\"}}],\"finishReason\":\"STOP\"}\n";

        _mockHttp
            .When(HttpMethod.Post, ApiUrl)
            .Respond("application/json", streamingResponse);

        var result = await _geminiService.PrioritizeGoalAsync("An already clear and concise goal");

        _output.WriteLine($"Result IsSuccess: {result.IsSuccess}");
        _output.WriteLine($"Result Error: {result.Error}");

        Assert.True(result.IsSuccess, $"Expected success but got error: {result.Error}");
        Assert.NotNull(result.Value);

        var response = result.Value!;
        Assert.Equal("No tasks were found for the given goal. Maybe it's already well-defined or too vague.", response.ExecutiveSummary);
        Assert.Empty(response.TaskItems);
    }

    [Fact]
    public async Task PrioritizeGoalAsync_ShouldReturnPartialResults_WhenOnlySomeTasksAreRanked()
    {
        var streamingResponse = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"tasks\\\":[{\\\"rank\\\":1,\\\"taskTitle\\\":\\\"Define MVP\\\"},{\\\"taskTitle\\\":\\\"Research Technologies without rank\\\"}],\\\"executiveSummary\\\":\\\"Partial ranking example\\\"}\"}],\"role\":\"model\"}}],\"finishReason\":\"STOP\"}\n";

        _mockHttp
            .When(HttpMethod.Post, ApiUrl)
            .Respond("application/json", streamingResponse);

        var result = await _geminiService.PrioritizeGoalAsync("Build and launch a mobile app in 6 months");

        _output.WriteLine($"Result IsSuccess: {result.IsSuccess}");
        _output.WriteLine($"Result Error: {result.Error}");

        Assert.True(result.IsSuccess, $"Expected success but got error: {result.Error}");
        Assert.NotNull(result.Value);

        var response = result.Value!;
        Assert.Equal("Partial ranking example", response.ExecutiveSummary);
        Assert.Equal(2, response.TaskItems.Count());
        Assert.Equal(1, response.TaskItems.First().Rank);
        Assert.Equal("Define MVP", response.TaskItems.First().TaskTitle);
        Assert.Equal("Research Technologies without rank", response.TaskItems.Skip(1).First().TaskTitle);
    }

    [Fact]
    public async Task PrioritizeGoalAsync_ShouldHandleMultipleStreamingChunks()
    {
        var streamingResponse = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"tasks\\\":[{\\\"rank\\\":1,\"}],\"role\":\"model\"}}]}\n" +
                               "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"\\\"taskTitle\\\":\\\"Define MVP\\\",\\\"reasoningChain\\\":\\\"Critical first step\\\"}],\\\"executiveSummary\\\":\"}],\"role\":\"model\"}}]}\n" +
                               "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"\\\"Multi-chunk streaming test\\\"}\"}],\"role\":\"model\"}}],\"finishReason\":\"STOP\"}\n";

        _mockHttp
            .When(HttpMethod.Post, ApiUrl)
            .Respond("application/json", streamingResponse);

        var result = await _geminiService.PrioritizeGoalAsync("Build and launch a mobile app in 6 months");

        _output.WriteLine($"Result IsSuccess: {result.IsSuccess}");
        _output.WriteLine($"Result Error: {result.Error}");

        Assert.True(result.IsSuccess, $"Expected success but got error: {result.Error}");
        Assert.NotNull(result.Value);

        var response = result.Value!;
        Assert.Equal("Multi-chunk streaming test", response.ExecutiveSummary);
        Assert.Single(response.TaskItems);
        Assert.Equal(1, response.TaskItems.First().Rank);
        Assert.Equal("Define MVP", response.TaskItems.First().TaskTitle);
    }

    // MyPortfolio.Core.Tests/Features/Prioritizer/Services/GeminiServiceTests.cs

    [Fact]
    public async Task PrioritizeGoalAsync_ShouldHandleExternalCancellation_Gracefully()
    {
        // 1. Setup CancellationTokenSource
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // 2. Mock a response that is VALID, but delayed significantly (e.g., 5 seconds).
        // The test will cancel this request after a very short period (e.g., 100ms).
        var singleChunkResponse = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"tasks\\\":[{\\\"rank\\\":1,\\\"taskTitle\\\":\\\"Cancelled\\\"}],\\\"executiveSummary\\\":\\\"Cancelled\\\"}\"}],\"role\":\"model\"}}],\"finishReason\":\"STOP\"}\n";

        _mockHttp
            .When(HttpMethod.Post, ApiUrl)
            .Respond(request =>
            {
                // Inject a long delay into the mock response handler (5 seconds)
                return Task.Delay(TimeSpan.FromSeconds(5), token)
                           .ContinueWith(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                           {
                               Content = new StringContent(singleChunkResponse, System.Text.Encoding.UTF8, "application/json")
                           }, TaskContinuationOptions.NotOnCanceled); // Only return on success, not on cancel

            });

        // 3. Act: Start the operation, and schedule cancellation quickly
        var task = _geminiService.PrioritizeGoalAsync("Goal to cancel quickly", token);

        // Schedule the cancellation after 100 milliseconds
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Wait for the result of the canceled task
        var result = await task;

        // 4. Assert

        // Ensure the output is clean for debugging
        if (!result.IsSuccess)
        {
            _output.WriteLine($"Result Error: {result.Error}");
        }

        Assert.False(result.IsSuccess, "The operation should have failed due to cancellation.");

        // Assert against the specific error message returned by the service's catch block
        Assert.Contains("Operation was canceled by the user.", result.Error);
    }

    // MyPortfolio.Core.Tests/Features/Prioritizer/Services/GeminiServiceTests.cs

    [Fact]
    public async Task PrioritizeGoalAsync_ShouldRetryAndSucceed_OnTransientError()
    {
        // The successful JSON response for the final attempt
        var successResponse = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"tasks\\\":[{\\\"rank\\\":1,\\\"taskTitle\\\":\\\"Success after retry\\\"}],\\\"executiveSummary\\\":\\\"Recovered successfully\\\"}\"}],\"role\":\"model\"}}],\"finishReason\":\"STOP\"}\n";

        // We expect 3 requests total: 2 failures + 1 success
        var requestCount = 0;

        _mockHttp
            .When(HttpMethod.Post, ApiUrl)
            .Respond(request =>
            {
                requestCount++;
                _output.WriteLine($"Mock received request #{requestCount}");

                if (requestCount <= 2)
                {
                    // Respond with a transient error (Service Unavailable) for the first two attempts
                    var errorContent = new StringContent("Service is temporarily down", System.Text.Encoding.UTF8, "application/json");
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = errorContent
                    });
                }
                else
                {
                    // Succeed on the third attempt (the last max retry attempt is 2, so this is attempt 3)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(successResponse, System.Text.Encoding.UTF8, "application/json")
                    });
                }
            });

        // Act
        var result = await _geminiService.PrioritizeGoalAsync("Goal that needs a successful retry");

        // Assert
        _output.WriteLine($"Result IsSuccess: {result.IsSuccess}");

        Assert.True(result.IsSuccess, $"Expected success after retry but got error: {result.Error}");
        Assert.Equal(3, requestCount);
        Assert.Equal("Recovered successfully", result.Value!.ExecutiveSummary);
        Assert.Equal("Success after retry", result.Value!.TaskItems.First().TaskTitle);
    }

    [Fact]
    public async Task PrioritizeGoalAsync_ShouldFail_AfterMaxRetriesExhausted()
    {
        // We expect 3 requests total: all failures
        var requestCount = 0;
        var persistentErrorMessage = "Rate limit exceeded permanently.";

        _mockHttp
            .When(HttpMethod.Post, ApiUrl)
            .Respond(request =>
            {
                requestCount++;
                _output.WriteLine($"Mock received request #{requestCount}");

                // Respond with a transient error (Too Many Requests) for all 3 attempts
                var errorContent = new StringContent(persistentErrorMessage, System.Text.Encoding.UTF8, "application/json");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = errorContent
                });
            });

        // Act
        var result = await _geminiService.PrioritizeGoalAsync("Goal that will definitely fail");

        // Assert
        Assert.False(result.IsSuccess, "Expected failure after max retries.");
        Assert.Equal(3, requestCount);
        // Assert against the error message returned when MaxRetries is exhausted
        Assert.Contains("API Request Failed: TooManyRequests - Rate limit exceeded permanently.", result.Error);
    }

    // MyPortfolio.Core.Tests/Features/Prioritizer/Services/GeminiServiceTests.cs

    [Fact]
    public async Task PrioritizeGoalAsync_ShouldExtractUsageMetadata()
    {
        // The successful JSON response components, using your proven escaping pattern.
        // The inner JSON is split into two fragments across chunks.
        const string innerJsonPart1 = "{\\\"tasks\\\":[{\\\"rank\\\":1,";
        const string innerJsonPart2 = "\\\"taskTitle\\\":\\\"Track Usage\\\",\\\"reasoningChain\\\":\\\"Cost tracking critical\\\"}],\\\"executiveSummary\\\":\\\"Metadata successfully parsed\\\"}";

        // Expected Usage Metadata values
        const int expectedPromptTokens = 50;
        const int expectedCandidateTokens = 10;
        const int expectedTotalTokens = 60;

        // 1. Construct the Multi-Chunk Streaming Response (using your successful pattern)
        var streamingResponse =
            // Chunk 1: First content fragment
            "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"" + innerJsonPart1 + "\"}],\"role\":\"model\"}}]}\n" +

            // Chunk 2 (Final): Second content fragment + Usage Metadata
            "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"" + innerJsonPart2 + "\"}],\"role\":\"model\"}}],\"usageMetadata\":{\"promptTokenCount\":" + expectedPromptTokens + ",\"candidatesTokenCount\":" + expectedCandidateTokens + ",\"totalTokenCount\":" + expectedTotalTokens + "}}\n";

        // 2. Mock the HTTP client
        _mockHttp
            .When(HttpMethod.Post, ApiUrl)
            .Respond("application/json", streamingResponse); // Use your proven Respond pattern

        // 3. Act
        var result = await _geminiService.PrioritizeGoalAsync("Goal to test usage tracking");

        // 4. Assert
        Assert.True(result.IsSuccess, $"Expected success but got error: {result.Error}");
        Assert.NotNull(result.Value);

        var response = result.Value!;

        // Assert the core data (accumulated content)
        Assert.Equal("Metadata successfully parsed", response.ExecutiveSummary);
        Assert.Single(response.TaskItems);
        Assert.Equal("Track Usage", response.TaskItems.First().TaskTitle);

        // Assert the UsageMetadata field (new functionality)
        Assert.NotNull(response.UsageMetadata);
        Assert.Equal(expectedPromptTokens, response.UsageMetadata.PromptTokenCount);
        Assert.Equal(expectedCandidateTokens, response.UsageMetadata.CandidatesTokenCount);
        Assert.Equal(expectedTotalTokens, response.UsageMetadata.TotalTokenCount);
    }


    // This method is called by the testing framework after all tests are done.
    public void Dispose()
    {
        // Explicitly dispose of the resources we created in the test setup.
        _httpClient.Dispose();
        _mockHttp.Dispose(); // The underlying handler, if applicable
        GC.SuppressFinalize(this);
    }
}