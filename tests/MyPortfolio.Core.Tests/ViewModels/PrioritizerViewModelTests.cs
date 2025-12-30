using MyPortfolio.Core.Abstractions;
using MyPortfolio.Core.Features.Prioritizer.Models;
using MyPortfolio.Core.Shared;
using MyPortfolio.Shared.Models;
using MyPortfolio.Shared.Services;
using MyPortfolio.Shared.ViewModels;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http.Json;
using Xunit;
using Xunit.Abstractions;

namespace MyPortfolio.Core.Tests.ViewModels;

public class PrioritizerViewModelTests
{
    private readonly HttpClient _httpClient;
    private readonly PrioritizerViewModel _viewModel;
    private readonly ITestOutputHelper _output;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly FakeAccessCodeService _fakeAccessCodeService;

    public PrioritizerViewModelTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Setup HttpClient mock
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object)
        {
            BaseAddress = new Uri("http://localhost")
        };
        
        // Use a fake implementation instead of mocking
        var fakeHttpClient = new HttpClient(new Mock<HttpMessageHandler>().Object);
        _fakeAccessCodeService = new FakeAccessCodeService(fakeHttpClient);
        
        _viewModel = new PrioritizerViewModel(_httpClient, _fakeAccessCodeService);
    }

    [Fact]
    public void InitialState_ShouldBeEmpty()
    {
        // Assert
        Assert.Empty(_viewModel.Goal);
        Assert.False(_viewModel.IsLoading);
        Assert.Null(_viewModel.ErrorMessage);
        Assert.Null(_viewModel.Result);
    }

    [Fact]
    public async Task PrioritizeAsync_ShouldSetError_WhenGoalIsEmpty()
    {
        // Act
        await _viewModel.PrioritizeAsync();

        // Assert
        Assert.False(_viewModel.IsLoading);
        Assert.Equal("Please enter a goal to prioritize.", _viewModel.ErrorMessage);
        Assert.Null(_viewModel.Result);
    }

    [Fact]
    public async Task PrioritizeAsync_ShouldSetError_WhenGoalIsWhitespace()
    {
        // Arrange
        _viewModel.Goal = "   ";

        // Act
        await _viewModel.PrioritizeAsync();

        // Assert
        Assert.False(_viewModel.IsLoading);
        Assert.Equal("Please enter a goal to prioritize.", _viewModel.ErrorMessage);
    }

    [Fact]
    public async Task PrioritizeAsync_ShouldSetError_WhenAccessCodeNotValid()
    {
        // Arrange
        _viewModel.Goal = "Test goal";
        _fakeAccessCodeService.HasValidAccessValue = false;

        // Act
        await _viewModel.PrioritizeAsync();

        // Assert
        Assert.False(_viewModel.IsLoading);
        Assert.Equal("Access code required. Please unlock features first.", _viewModel.ErrorMessage);
        Assert.True(_fakeAccessCodeService.RequestUnlockCalled);
    }

    [Fact]
    public async Task PrioritizeAsync_ShouldSetLoading_WhileProcessing()
    {
        // Arrange
        _viewModel.Goal = "Test goal";
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        
        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(tcs.Task);

        // Act
        var task = _viewModel.PrioritizeAsync();

        // Assert - Check loading state before completion
        await Task.Delay(10); // Give it time to set loading
        Assert.True(_viewModel.IsLoading);

        // Complete the task
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var prioritizationResponse = new PrioritizationResponse
        {
            ExecutiveSummary = "Test summary",
            TaskItems = Array.Empty<TaskItem>()
        };
        response.Content = JsonContent.Create(prioritizationResponse);
        tcs.SetResult(response);
        await task;

        // Assert - Loading should be false after completion
        Assert.False(_viewModel.IsLoading);
    }

    [Fact]
    public async Task PrioritizeAsync_ShouldSetResult_WhenSuccessful()
    {
        // Arrange
        _viewModel.Goal = "Launch a SaaS product";
        var expectedResponse = new PrioritizationResponse
        {
            ExecutiveSummary = "Test summary",
            TaskItems = new[]
            {
                new TaskItem
                {
                    Rank = 1,
                    TaskTitle = "Define MVP",
                    ReasoningChain = "First step",
                    Estimate = "2-3 days"
                }
            }
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = JsonContent.Create(expectedResponse);

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        await _viewModel.PrioritizeAsync();

        // Assert
        Assert.False(_viewModel.IsLoading);
        Assert.Null(_viewModel.ErrorMessage);
        Assert.NotNull(_viewModel.Result);
        Assert.Equal("Test summary", _viewModel.Result.ExecutiveSummary);
        Assert.Single(_viewModel.Result.TaskItems);
        Assert.Equal(1, _viewModel.Result.TaskItems.First().Rank);
    }

    [Fact]
    public async Task PrioritizeAsync_ShouldSetError_WhenAPIFails()
    {
        // Arrange
        _viewModel.Goal = "Test goal";
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        response.Content = new StringContent("API error occurred");

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        await _viewModel.PrioritizeAsync();

        // Assert
        Assert.False(_viewModel.IsLoading);
        Assert.Contains("API Error:", _viewModel.ErrorMessage);
        Assert.Null(_viewModel.Result);
    }

    [Fact]
    public async Task PrioritizeAsync_ShouldHandleCancellation()
    {
        // Arrange
        _viewModel.Goal = "Test goal";
        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        // Act
        await _viewModel.PrioritizeAsync();

        // Assert
        Assert.False(_viewModel.IsLoading);
        Assert.Equal("The request was cancelled.", _viewModel.ErrorMessage);
    }

    [Fact]
    public async Task PrioritizeAsync_ShouldHandleGenericException()
    {
        // Arrange
        _viewModel.Goal = "Test goal";
        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new Exception("Unexpected error"));

        // Act
        await _viewModel.PrioritizeAsync();

        // Assert
        Assert.False(_viewModel.IsLoading);
        Assert.Equal("An unexpected error occurred: Unexpected error", _viewModel.ErrorMessage);
    }

    [Fact]
    public void Reset_ShouldClearAllState()
    {
        // Arrange
        _viewModel.Goal = "Test goal";
        _viewModel.IsLoading = true;
        _viewModel.ErrorMessage = "Test error";
        _viewModel.Result = new PrioritizationResponse
        {
            ExecutiveSummary = "Test"
        };

        // Act
        _viewModel.Reset();

        // Assert
        Assert.Empty(_viewModel.Goal);
        Assert.False(_viewModel.IsLoading);
        Assert.Null(_viewModel.ErrorMessage);
        Assert.Null(_viewModel.Result);
    }

    [Fact]
    public async Task PrioritizeAsync_ShouldClearPreviousError_OnNewAttempt()
    {
        // Arrange
        _viewModel.Goal = "Test goal";
        _viewModel.ErrorMessage = "Previous error";

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = JsonContent.Create(new PrioritizationResponse
        {
            ExecutiveSummary = "Success"
        });

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        await _viewModel.PrioritizeAsync();

        // Assert
        Assert.Null(_viewModel.ErrorMessage);
    }

    // Fake implementation to avoid mocking sealed/non-virtual members
    private class FakeAccessCodeService : AccessCodeService
    {
        public bool HasValidAccessValue { get; set; } = true;
        public string? AccessTokenValue { get; set; } = "test-token";
        public bool RequestUnlockCalled { get; private set; }

        public FakeAccessCodeService(HttpClient httpClient) : base(httpClient)
        {
        }

        public override bool HasValidAccess()
        {
            return HasValidAccessValue;
        }

        public override string? GetAccessToken()
        {
            return AccessTokenValue;
        }

        public override void RequestUnlock()
        {
            RequestUnlockCalled = true;
            base.RequestUnlock();
        }
    }
}

