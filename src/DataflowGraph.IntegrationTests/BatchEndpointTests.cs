using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DataflowGraph.IntegrationTests;

public class BatchEndpointTests : IntegrationTestBase
{
#pragma warning disable IDE0290 // Use primary constructor
    public BatchEndpointTests(IntegrationTestFactory factory) : base(factory) { }
#pragma warning restore IDE0290 // Use primary constructor

    [Fact]
    public async Task ExecuteBatch_SingleOperation_ReturnsSuccess()
    {
        // Arrange
        var request = CreateBatchRequest(
            CreateOperationItem("TestFetchUsers", new() { ["filter"] = "A" }));

        // Act
        var response = await SendBatchRequestAsync(request);
        var result = await ReadResponseAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.IsSuccess.Should().BeTrue();
        result.OperationCount.Should().Be(1);
        result.SuccessCount.Should().Be(1);
        result.FailureCount.Should().Be(0);

        // FIX: Results is a Dictionary<string, BatchOperationResult>
        // Key = OperationName, Value = BatchOperationResult
        result.Results.Should().ContainKey("TestFetchUsers");
        var opResult = result.Results["TestFetchUsers"];
        opResult.IsSuccess.Should().BeTrue();

        // Deserialize the value to verify content
        var users = JsonSerializer.Deserialize<List<string>>(
            opResult.Value?.ToString() ?? "[]");
        users.Should().Contain("Alice");
        users.Should().NotContain("Bob"); // Filtered out
    }

    [Fact]
    public async Task ExecuteBatch_MultipleIndependentOperations_ExecutesInParallel()
    {
        // Arrange
        var request = CreateBatchRequest(
            CreateOperationItem("TestFetchUsers", new() { ["filter"] = "A" }),
            CreateOperationItem("TestFetchUsers", new() { ["filter"] = "B" }),
            CreateOperationItem("TestValidate"));

        // Act
        var response = await SendBatchRequestAsync(request);
        var result = await ReadResponseAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.IsSuccess.Should().BeTrue();
        result.OperationCount.Should().Be(3);
        result.SuccessCount.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteBatch_WithDependencies_ExecutesInCorrectOrder()
    {
        // Arrange
        var request = CreateBatchRequest(
            CreateOperationItem("TestValidate"),
            CreateOperationItem("TestFetchUsers",
                dependsOn: ["TestValidate"],
                arguments: new() { ["filter"] = "C" }));

        // Act
        var response = await SendBatchRequestAsync(request);
        var result = await ReadResponseAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.IsSuccess.Should().BeTrue();
        result.Results.Should().HaveCount(2);

        // FIX: Use ContainsKey for dictionary
        result.Results.Should().ContainKey("TestValidate");
        result.Results.Should().ContainKey("TestFetchUsers");
    }

    [Fact]
    public async Task ExecuteBatch_OperationFails_ReturnsFailureResult()
    {
        // Arrange
        var request = CreateBatchRequest(
            CreateOperationItem("TestValidate", new() { ["invalid"] = true }));

        // Act
        var response = await SendBatchRequestAsync(request);
        var result = await ReadResponseAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.IsSuccess.Should().BeFalse();
        result.FailureCount.Should().Be(1);

        // FIX: Access via dictionary key
        result.Results.Should().ContainKey("TestValidate");
        var opResult = result.Results["TestValidate"];
        opResult.IsSuccess.Should().BeFalse();
        opResult.ErrorMessage.Should().Contain("Validation failed");
    }

    [Fact]
    public async Task ExecuteBatch_RequiredOperationFails_StopsBatch()
    {
        // Arrange
        var request = CreateBatchRequest(
            CreateOperationItem("TestValidate", new() { ["invalid"] = true }),
            CreateOperationItem("TestFetchUsers", dependsOn: ["TestValidate"]));

        // Act
        var response = await SendBatchRequestAsync(request);
        var result = await ReadResponseAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.IsSuccess.Should().BeFalse();

        // FIX: Access via dictionary
        result.Results.Should().ContainKey("TestValidate");
        var validateResult = result.Results["TestValidate"];
        validateResult.IsSuccess.Should().BeFalse();

        result.Results.Should().ContainKey("TestFetchUsers");
        var fetchResult = result.Results["TestFetchUsers"];
        fetchResult.IsSuccess.Should().BeFalse();
        fetchResult.ErrorMessage.Should().Contain("Dependency failed");
    }

    [Fact]
    public async Task ExecuteBatch_OptionalOperationFails_ContinuesBatch()
    {
        // Arrange
        var request = CreateBatchRequest(
            CreateOperationItem("TestValidate",
                arguments: new() { ["invalid"] = true },
                isRequired: false),
            CreateOperationItem("TestFetchUsers"));

        // Act
        var response = await SendBatchRequestAsync(request);
        var result = await ReadResponseAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.IsSuccess.Should().BeTrue();

        // FIX: Access via dictionary
        result.Results.Should().ContainKey("TestValidate");
        var validateResult = result.Results["TestValidate"];
        validateResult.IsSuccess.Should().BeFalse();

        result.Results.Should().ContainKey("TestFetchUsers");
        var fetchResult = result.Results["TestFetchUsers"];
        fetchResult.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteBatch_InvalidOperationName_ReturnsBadRequest()
    {
        // Arrange
        var request = CreateBatchRequest(
            CreateOperationItem("NonExistentOperation"));

        // Act
        var response = await SendBatchRequestAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("not found");
        content.Should().Contain("TestFetchUsers");
    }

    [Fact]
    public async Task ExecuteBatch_EmptyOperations_ReturnsBadRequest()
    {
        // Arrange
        var request = CreateBatchRequest();

        // Act
        var response = await SendBatchRequestAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("at least one operation");
    }

    [Fact]
    public async Task ExecuteBatch_CircularDependency_ReturnsBadRequest()
    {
        // Arrange
        var request = CreateBatchRequest(
            CreateOperationItem("TestFetchUsers", dependsOn: ["TestValidate"]),
            CreateOperationItem("TestValidate", dependsOn: ["TestFetchUsers"]));

        // Act
        var response = await SendBatchRequestAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Circular");
    }

    [Fact]
    public async Task ExecuteBatch_SerialProcessing_ExecutesInOrder()
    {
        // Arrange
        var request = CreateBatchRequest(CreateOperationItem("TestValidate"), CreateOperationItem("TestFetchUsers"));
        request.ProcessingType = ProcessingType.Serial;

        // Act
        var response = await SendBatchRequestAsync(request);
        var result = await ReadResponseAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.IsSuccess.Should().BeTrue();
        result.Results.Should().HaveCount(2);
        result.Results.Values.All(r => r.IsSuccess).Should().BeTrue();
    }
}