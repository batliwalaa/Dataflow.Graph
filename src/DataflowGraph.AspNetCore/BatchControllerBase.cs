using DataflowGraph.Abstractions;
using DataflowGraph.Resolution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

#pragma warning disable IDE0290 // Use primary constructor
namespace DataflowGraph.AspNetCore;

/// <summary>
/// Base controller class for HTTP batch endpoints.
/// Provides reusable functionality for:
/// - Request validation
/// - Batch context creation (UserId, TenantId from claims)
/// - Batch graph execution
/// - Standardized response formatting
/// - Error handling
/// - Logging/telemetry hooks
/// 
/// Inherit from this class to create specific batch endpoints.
/// Override methods to customize behavior for your scenario.
/// 
/// Usage:
/// public class UserBatchController : BatchControllerBase
/// {
///     public UserBatchController(
///         IBatchGraphFactory graphFactory,
///         IOperationResolver operationResolver,
///         ILogger<UserBatchController> logger)
///         : base(graphFactory, operationResolver, logger)
///     {
///     }
/// 
///     [HttpPost("users/batch")]
///     public override async Task<ActionResult<BatchOperationResponse>> ExecuteBatchAsync(
///         BatchOperationRequest request,
///         CancellationToken cancellationToken)
///     {
///         // Add custom validation
///         if (!User.HasClaim("tenant", out _))
///         {
///             return BadRequest("Tenant ID is required");
///         }
/// 
///         // Execute base implementation
///         return await base.ExecuteBatchAsync(request, cancellationToken);
///     }
/// }
/// 
/// Matches the original MicroCreations.Batch.Mvc.BatchControllerBase pattern.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class BatchControllerBase : ControllerBase
{
    private readonly IBatchGraphFactory _graphFactory;
    private readonly IOperationResolver _operationResolver;
    private readonly ILogger<BatchControllerBase> _logger;

    /// <summary>
    /// Initializes a new instance of the BatchControllerBase class.
    /// </summary>
    /// <param name="graphFactory">Factory for creating BatchGraph instances</param>
    /// <param name="operationResolver">Resolver for operation name to executor mapping</param>
    /// <param name="logger">Logger for request/execution logging</param>
    protected BatchControllerBase(
        IBatchGraphFactory graphFactory,
        IOperationResolver operationResolver,
        ILogger<BatchControllerBase> logger)
    {
        _graphFactory = graphFactory ?? throw new ArgumentNullException(nameof(graphFactory));
        _operationResolver = operationResolver ?? throw new ArgumentNullException(nameof(operationResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a batch operation request.
    /// This is the main entry point for HTTP batch endpoints.
    /// 
    /// Execution flow:
    /// 1. Validate request (operation names, dependencies, etc.)
    /// 2. Create BatchContext (with UserId, TenantId from claims)
    /// 3. Build BatchGraph from request
    /// 4. Execute graph
    /// 5. Return BatchOperationResponse
    /// 
    /// Override this method in derived controllers to add custom validation or logic.
    /// </summary>
    /// <param name="request">The batch operation request from the client</param>
    /// <param name="cancellationToken">Cancellation token for the request</param>
    /// <returns>BatchOperationResponse with all operation results</returns>
    /// <response code="200">Batch executed successfully (may include operation failures)</response>
    /// <response code="400">Invalid request (validation errors)</response>
    /// <response code="401">Unauthorized (authentication required)</response>
    /// <response code="403">Forbidden (insufficient permissions)</response>
    /// <response code="408">Request timeout (batch took too long)</response>
    /// <response code="500">Internal server error (unexpected exception)</response>
    [HttpPost("batch")]
    [AllowAnonymous] // Override with [Authorize] in derived controllers
    public virtual async Task<ActionResult<BatchOperationResponse>> ExecuteBatchAsync(
        BatchOperationRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var batchId = request?.BatchId ?? Guid.NewGuid().ToString("N")[..12];

        try
        {
            // Check for null request before validation
            if (request == null)
            {
                _logger.LogWarning("Batch request body is null for BatchId={BatchId}", batchId);
                return BadRequest(BatchOperationResponse.CreateError(batchId, "Request body is required"));
            }


            // Step 1: Validate request
            var validationResult = await ValidateRequestAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning(
                    "Batch request validation failed for BatchId={BatchId}. Errors: {Errors}",
                    batchId,
                    string.Join("; ", validationResult.Errors));

                return BadRequest(BatchOperationResponse.CreateError(batchId, string.Join("; ", validationResult.Errors)));
            }

            // Step 2: Create batch context
            var batchContext = await CreateBatchContextAsync(request, cancellationToken);

            // Step 3: Log request start
            _logger.LogInformation(
                "Batch execution started: BatchId={BatchId}, Operations={OperationCount}, Mode={ProcessingType}, UserId={UserId}",
                batchContext.BatchId,
                request.Operations.Count,
                request.ProcessingType,
                batchContext.UserId ?? "anonymous");

            // Step 4: Build and execute batch graph
            var graph = _graphFactory.Create()
                .AddOperations(request.ToOperationDefinitions())
                .WithProcessingType(request.ProcessingType)
                .WithMaxDegreeOfParallelism(request.MaxDegreeOfParallelism)
                .OnError((name, ex) => _logger.LogError(ex, "Operation {OperationName} failed in batch {BatchId}", name, batchId))
                .OnComplete((name, duration) => _logger.LogDebug("Operation {OperationName} completed in {DurationMs}ms", name, duration.TotalMilliseconds));

            var result = await graph.ExecuteAsync(batchContext, cancellationToken);

            // Step 5: Create and log response
            var response = BatchOperationResponse.FromGraphResult(result, batchContext.BatchId, request.Metadata);

            stopwatch.Stop();

            _logger.LogInformation(
                "Batch execution completed: BatchId={BatchId}, Status={Status}, Duration={DurationMs}ms, Success={SuccessCount}/{OperationCount}",
                batchContext.BatchId,
                response.IsSuccess ? "Success" : "Failed",
                response.DurationMs,
                response.SuccessCount,
                response.OperationCount);

            // Return 200 OK even if some operations failed (client checks IsSuccess)
            return Ok(response);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();

            _logger.LogWarning(
                "Batch execution cancelled: BatchId={BatchId}, Duration={DurationMs}ms",
                batchId,
                stopwatch.Elapsed.TotalMilliseconds);

            return StatusCode(408, BatchOperationResponse.CreateError(batchId, "Batch execution was cancelled (timeout or client disconnect)"));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Batch execution failed unexpectedly: BatchId={BatchId}, Duration={DurationMs}ms",
                batchId,
                stopwatch.Elapsed.TotalMilliseconds);

            return StatusCode(500, BatchOperationResponse.CreateError(batchId, $"Internal server error: {ex.Message}"));
        }
    }

    /// <summary>
    /// Validates the batch operation request.
    /// Override this method in derived controllers to add custom validation.
    /// 
    /// Default validation:
    /// - Request is not null
    /// - Operations list is not empty
    /// - All operation names are registered
    /// - No circular dependencies
    /// - All dependencies exist in the batch
    /// </summary>
    /// <param name="request">The batch operation request to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>BatchValidationResult with validation errors (if any)</returns>
    protected virtual Task<BatchValidationResult> ValidateRequestAsync(
        BatchOperationRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        // Validate request is not null
        if (request == null)
        {
            errors.Add("Request body is required");
            return Task.FromResult(new BatchValidationResult(false, errors, []));
        }

        // Validate operations list is not empty
        if (request.Operations.Count == 0)
        {
            errors.Add("At least one operation is required");
            return Task.FromResult(new BatchValidationResult(false, errors, []));
        }

        // Validate against registered operations
        var registeredOps = _operationResolver.GetRegisteredOperationNames();
        var validationResult = request.Validate(registeredOps);

        return Task.FromResult(validationResult);
    }

    /// <summary>
    /// Creates the BatchContext for batch execution.
    /// Override this method in derived controllers to customize context creation.
    /// 
    /// Default behavior:
    /// - BatchId from request (or generated if not provided)
    /// - UserId from JWT claim "sub" or "userid"
    /// - TenantId from JWT claim "tenant" or "tenantid"
    /// - CancellationToken from request
    /// 
    /// Common customizations:
    /// - Extract UserId/TenantId from different claims
    /// - Add custom data to context
    /// - Set timeout based on user tier
    /// </summary>
    /// <param name="request">The batch operation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Configured BatchContext instance</returns>
    protected virtual Task<BatchContext> CreateBatchContextAsync(
        BatchOperationRequest request,
        CancellationToken cancellationToken)
    {
        var batchContext = new BatchContext(
            batchId: request?.BatchId,
            cancellationToken: cancellationToken,
            userId: GetUserIdFromClaims(),
            tenantId: GetTenantIdFromClaims());

        // Add request metadata to context
        if (request?.Metadata != null)
        {
            foreach (var kvp in request.Metadata)
            {
                batchContext.SetData(kvp.Key, kvp.Value);
            }
        }

        // Add correlation ID if not present
        if (!batchContext.Data.ContainsKey("CorrelationId"))
        {
            var correlationId = HttpContext.TraceIdentifier;
            batchContext.SetData("CorrelationId", correlationId);
        }

        return Task.FromResult(batchContext);
    }

    /// <summary>
    /// Extracts the user ID from HTTP claims.
    /// Override this method in derived controllers to customize user ID extraction.
    /// 
    /// Default: Checks claims "sub", "userid", "user_id", "nameid" (case-insensitive)
    /// </summary>
    /// <returns>User ID string, or null if not found</returns>
    protected virtual string? GetUserIdFromClaims()
    {
        var claimTypes = new[] { "sub", "userid", "user_id", "nameid", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier" };

        foreach (var claimType in claimTypes)
        {
            var claim = User.FindFirst(c => c.Type.Equals(claimType, StringComparison.OrdinalIgnoreCase));
            if (claim != null)
            {
                return claim.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the tenant ID from HTTP claims.
    /// Override this method in derived controllers to customize tenant ID extraction.
    /// 
    /// Default: Checks claims "tenant", "tenantid", "tenant_id" (case-insensitive)
    /// </summary>
    /// <returns>Tenant ID string, or null if not found</returns>
    protected virtual string? GetTenantIdFromClaims()
    {
        var claimTypes = new[] { "tenant", "tenantid", "tenant_id", "http://schemas.contoso.com/tenant" };

        foreach (var claimType in claimTypes)
        {
            var claim = User.FindFirst(c => c.Type.Equals(claimType, StringComparison.OrdinalIgnoreCase));
            if (claim != null)
            {
                return claim.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Called before batch execution starts.
    /// Override for custom pre-execution logic (logging, metrics, etc.).
    /// </summary>
    /// <param name="request">The batch operation request</param>
    /// <param name="batchContext">The batch context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected virtual Task OnBeforeExecuteAsync(
        BatchOperationRequest request,
        BatchContext batchContext,
        CancellationToken cancellationToken)
    {
        // Override in derived controllers for custom pre-execution logic
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called after batch execution completes.
    /// Override for custom post-execution logic (logging, metrics, cleanup, etc.).
    /// </summary>
    /// <param name="request">The batch operation request</param>
    /// <param name="response">The batch operation response</param>
    /// <param name="batchContext">The batch context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected virtual Task OnAfterExecuteAsync(
        BatchOperationRequest request,
        BatchOperationResponse response,
        BatchContext batchContext,
        CancellationToken cancellationToken)
    {
        // Override in derived controllers for custom post-execution logic
        return Task.CompletedTask;
    }
}
#pragma warning restore IDE0290 // Use primary constructor
