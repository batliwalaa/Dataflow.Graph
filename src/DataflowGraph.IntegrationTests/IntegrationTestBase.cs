using DataflowGraph.AspNetCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using System.Text.Json;

namespace DataflowGraph.IntegrationTests;

/// <summary>
/// Custom WebApplicationFactory that properly configures the test host.
/// </summary>
public class IntegrationTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // FIX: Explicitly set content root to find appsettings.json, etc.
        builder.UseContentRoot(Directory.GetCurrentDirectory());

        // Configure test services if needed
        builder.ConfigureTestServices(services =>
        {
            // Add any test-specific service overrides here
            // For example: services.AddSingleton<IEmailService, MockEmailService>();
        });
    }

    /// <summary>
    /// Creates a client with custom configuration.
    /// </summary>
    public HttpClient CreateClient(Action<IServiceCollection>? configureServices = null)
    {
        var client = base.CreateClient();

        if (configureServices != null)
        {
            // Services already configured in ConfigureWebHost
            // This is for runtime configuration if needed
        }

        return client;
    }
}

/// <summary>
/// Base class for integration tests using the custom factory.
/// </summary>
public class IntegrationTestBase : IClassFixture<IntegrationTestFactory>
{
    protected readonly HttpClient _httpClient;
    protected readonly IntegrationTestFactory _factory;

    public IntegrationTestBase(IntegrationTestFactory factory)
    {
        _factory = factory;
        _httpClient = _factory.CreateClient();
    }

    /// <summary>
    /// Creates a batch request with the specified operations.
    /// </summary>
    protected static BatchOperationRequest CreateBatchRequest(
        params BatchOperationItem[] operations)
    {
        return new BatchOperationRequest
        {
            BatchId = Guid.NewGuid().ToString("N")[..12],
            Operations = [.. operations],
            ProcessingType = ProcessingType.Parallel,
            MaxDegreeOfParallelism = 4
        };
    }

    /// <summary>
    /// Creates a single operation item for batch requests.
    /// </summary>
    protected static BatchOperationItem CreateOperationItem(
        string name,
        Dictionary<string, object?>? arguments = null,
        IEnumerable<string>? dependsOn = null,
        bool isRequired = true)
    {
        return new BatchOperationItem
        {
            Name = name,
            Arguments = arguments ?? [],
            DependsOn = dependsOn?.ToList() ?? [],
            IsRequired = isRequired
        };
    }

    /// <summary>
    /// Sends a batch request and returns the response.
    /// </summary>
    protected async Task<HttpResponseMessage> SendBatchRequestAsync(BatchOperationRequest request)
    {
        var content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8,
            "application/json");

        return await _httpClient.PostAsync("/batch", content);
    }

    /// <summary>
    /// Deserializes the response content to BatchOperationResponse.
    /// </summary>
    protected async Task<BatchOperationResponse> ReadResponseAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<BatchOperationResponse>(content, _jsonOptions)!;
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}