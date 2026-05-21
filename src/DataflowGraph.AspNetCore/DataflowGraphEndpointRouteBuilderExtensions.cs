using DataflowGraph.Abstractions;
using DataflowGraph.Resolution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Claims;

namespace DataflowGraph.AspNetCore;

/// <summary>
/// Extension methods for IEndpointRouteBuilder to map batch endpoints.
/// Enables minimal API-style batch endpoints (alternative to BatchControllerBase).
/// 
/// Usage (Program.cs):
/// var app = builder.Build();
/// 
/// // Simple batch endpoint (default route: POST /batch)
/// app.MapBatchEndpoint();
/// 
/// // Custom route
/// app.MapBatchEndpoint("/api/batch");
/// 
/// // With authorization
/// app.MapBatchEndpoint("/api/batch")
///     .RequireAuthorization();
/// 
/// // With custom configuration
/// app.MapBatchEndpoint("/api/batch", options =>
/// {
///     options.ValidateRequest = true;
///     options.LogExecution = true;
///     options.MaxOperationsPerBatch = 50;
/// });
/// </summary>
public static class DataflowGraphEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a batch endpoint with default configuration.
    /// Route: POST /batch
    /// </summary>
    public static IEndpointConventionBuilder MapBatchEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapBatchEndpoint("/batch");
    }

    /// <summary>
    /// Maps a batch endpoint with custom route pattern.
    /// </summary>
    public static IEndpointConventionBuilder MapBatchEndpoint(
        this IEndpointRouteBuilder endpoints,
        string pattern)
    {
        return endpoints.MapBatchEndpoint(pattern, null);
    }

    /// <summary>
    /// Maps a batch endpoint with custom configuration.
    /// </summary>
    public static IEndpointConventionBuilder MapBatchEndpoint(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Action<BatchEndpointOptions>? configureOptions)
    {
        var options = new BatchEndpointOptions();
        configureOptions?.Invoke(options);

        return endpoints.MapPost(pattern, async (
            BatchOperationRequest? request,
            IBatchGraphFactory graphFactory,
            IOperationResolver operationResolver,
            ILogger<BatchEndpointOptions> logger,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var stopwatch = Stopwatch.StartNew();
            var batchId = request?.BatchId ?? Guid.NewGuid().ToString("N")[..12];

            if (request == null)
            {
                logger.LogWarning("Batch request body is null for BatchId={BatchId}", batchId);
                return Results.Json(
                    BatchOperationResponse.CreateError(batchId, "Request body is required"),
                    statusCode: 400);
            }

            try
            {
                // Step 1: Validate request
                if (options.ValidateRequest)
                {
                    var validationResult = await ValidateRequestAsync(
                        request,
                        operationResolver,
                        options);

                    if (!validationResult.IsValid)
                    {
                        logger.LogWarning(
                            "Batch request validation failed for BatchId={BatchId}. Errors: {Errors}",
                            batchId,
                            string.Join("; ", validationResult.Errors));

                        return Results.Json(
                            BatchOperationResponse.CreateError(batchId, string.Join("; ", validationResult.Errors)),
                            statusCode: 400);
                    }
                }

                // Step 2: Create batch context
                var batchContext = CreateBatchContext(request, httpContext, options);

                // Step 3: Log request start
                if (options.LogExecution)
                {
                    logger.LogInformation(
                        "Batch execution started: BatchId={BatchId}, Operations={OperationCount}, Mode={ProcessingType}, UserId={UserId}",
                        batchContext.BatchId,
                        request.Operations.Count,
                        request.ProcessingType,
                        batchContext.UserId ?? "anonymous");
                }

                // Step 4: Build and execute batch graph
                var graph = graphFactory.Create()
                    .AddOperations(request.ToOperationDefinitions())
                    .WithProcessingType(request.ProcessingType)
                    .WithMaxDegreeOfParallelism(request.MaxDegreeOfParallelism)
                    .OnError((name, ex) => logger.LogError(
                        ex,
                        "Operation {OperationName} failed in batch {BatchId}",
                        name,
                        batchId))
                    .OnComplete((name, duration) => logger.LogDebug(
                        "Operation {OperationName} completed in {DurationMs}ms",
                        name,
                        duration.TotalMilliseconds));

                var result = await graph.ExecuteAsync(batchContext, cancellationToken);

                // Step 5: Create response
                var response = BatchOperationResponse.FromGraphResult(
                    result,
                    batchContext.BatchId,
                    request.Metadata);

                stopwatch.Stop();

                // Step 6: Log completion
                if (options.LogExecution)
                {
                    logger.LogInformation(
                        "Batch execution completed: BatchId={BatchId}, Status={Status}, Duration={DurationMs}ms, Success={SuccessCount}/{OperationCount}",
                        batchContext.BatchId,
                        response.IsSuccess ? "Success" : "Failed",
                        response.DurationMs,
                        response.SuccessCount,
                        response.OperationCount);
                }

                // Step 7: Return response (200 OK even if some operations failed)
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();

                logger.LogWarning(
                    "Batch execution cancelled: BatchId={BatchId}, Duration={DurationMs}ms",
                    batchId,
                    stopwatch.Elapsed.TotalMilliseconds);

                return Results.Json(
                    BatchOperationResponse.CreateError(batchId, "Batch execution was cancelled (timeout or client disconnect)"),
                    statusCode: 408);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                logger.LogError(
                    ex,
                    "Batch execution failed unexpectedly: BatchId={BatchId}, Duration={DurationMs}ms",
                    batchId,
                    stopwatch.Elapsed.TotalMilliseconds);

                return Results.Json(
                    BatchOperationResponse.CreateError(batchId, $"Internal server error: {ex.Message}"),
                    statusCode: 500);
            }
        })
        .WithName("ExecuteBatch")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Execute a batch of operations",
            Description = "Executes multiple operations in a single request with support for dependencies and parallel execution."
        });
    }

    /// <summary>
    /// Validates the batch operation request.
    /// </summary>
    private static async Task<BatchValidationResult> ValidateRequestAsync(
        BatchOperationRequest? request,
        IOperationResolver operationResolver,
        BatchEndpointOptions options)
    {
        var errors = new List<string>();

        // Handle null request
        if (request == null)
        {
            errors.Add("Request body is required");
            return new BatchValidationResult(false, errors, []);
        }

        if (request.Operations?.Count == 0)
        {
            errors.Add("At least one operation is required");
            return new BatchValidationResult(false, errors, []);
        }

        if (options.MaxOperationsPerBatch > 0 && request.Operations!.Count > options.MaxOperationsPerBatch)
        {
            errors.Add($"Maximum {options.MaxOperationsPerBatch} operations allowed per batch. Requested: {request.Operations.Count}");
            return new BatchValidationResult(false, errors, []);
        }

        var registeredOps = operationResolver.GetRegisteredOperationNames();
        var validationResult = request.Validate(registeredOps);

        return await Task.FromResult(validationResult);
    }

    /// <summary>
    /// Creates the BatchContext for batch execution.
    /// </summary>
    private static BatchContext CreateBatchContext(
        BatchOperationRequest? request,
        HttpContext httpContext,
        BatchEndpointOptions options)
    {
        var user = httpContext.User;
        var batchContext = new BatchContext(
            batchId: request?.BatchId,
            cancellationToken: httpContext.RequestAborted,
            userId: GetUserIdFromClaims(user),
            tenantId: GetTenantIdFromClaims(user));

        if (request?.Metadata != null)
        {
            foreach (var kvp in request.Metadata)
            {
                batchContext.SetData(kvp.Key, kvp.Value);
            }
        }

        if (!batchContext.Data.ContainsKey("CorrelationId"))
        {
            batchContext.SetData("CorrelationId", httpContext.TraceIdentifier);
        }

        if (options.ContextDataFactory != null)
        {
            var customData = options.ContextDataFactory(httpContext);
            foreach (var kvp in customData)
            {
                batchContext.SetData(kvp.Key, kvp.Value);
            }
        }

        return batchContext;
    }

    private static string? GetUserIdFromClaims(ClaimsPrincipal user)
    {
        var claimTypes = new[] { "sub", "userid", "user_id", "nameid" };
        foreach (var claimType in claimTypes)
        {
            var claim = user.FindFirst(c => c.Type.Equals(claimType, StringComparison.OrdinalIgnoreCase));
            if (claim != null)
            {
                return claim.Value;
            }
        }
        return null;
    }

    private static string? GetTenantIdFromClaims(ClaimsPrincipal user)
    {
        var claimTypes = new[] { "tenant", "tenantid", "tenant_id", "tid" };
        foreach (var claimType in claimTypes)
        {
            var claim = user.FindFirst(c => c.Type.Equals(claimType, StringComparison.OrdinalIgnoreCase));
            if (claim != null)
            {
                return claim.Value;
            }
        }
        return null;
    }
}

/// <summary>
/// Configuration options for batch endpoints.
/// </summary>
public class BatchEndpointOptions
{
    /// <summary>
    /// Gets or sets whether to validate requests before execution.
    /// Default is true.
    /// </summary>
    public bool ValidateRequest { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to log batch execution details.
    /// Default is true.
    /// </summary>
    public bool LogExecution { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of operations allowed per batch.
    /// Default is 0 (no limit).
    /// </summary>
    public int MaxOperationsPerBatch { get; set; } = 0;

    /// <summary>
    /// Gets or sets a factory for adding custom data to BatchContext.
    /// </summary>
    public Func<HttpContext, Dictionary<string, object?>>? ContextDataFactory { get; set; }
}