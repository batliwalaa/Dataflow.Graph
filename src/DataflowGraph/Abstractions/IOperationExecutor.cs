namespace DataflowGraph.Abstractions;

/// <summary>
/// Defines an operation executor that can be invoked by the batch library.
/// Implementations are registered in DI and resolved by operation name.
/// The library does NOT know what operations do - they are implemented by consuming projects.
/// This is the core abstraction that enables the plugin/registry architecture.
/// 
/// Key characteristics:
/// - Operation name is used for DI resolution (e.g., "FetchUsers", "ValidateOrder")
/// - ExecuteAsync receives batch context, graph context, arguments, and cancellation token
/// - Returns OperationResult (contains value, success status, exception details)
/// - Multiple operations can be registered and resolved by name
/// 
/// </summary>
public interface IOperationExecutor
{
    /// <summary>
    /// Gets the unique operation name used to resolve this executor.
    /// This name is used in BatchOperationRequest to specify which operation to run.
    /// Must be unique across all registered operations in the application.
    /// 
    /// Examples:
    /// - "FetchUsers"
    /// - "ValidateOrder"
    /// - "SendNotification"
    /// - "ProcessPayment"
    /// </summary>
    string OperationName { get; }

    /// <summary>
    /// Executes the operation with the provided context and arguments.
    /// This method is called by the batch library when the operation is scheduled to run.
    /// 
    /// Execution flow:
    /// 1. Library resolves operation by name via IOperationResolver
    /// 2. Library waits for dependencies to complete (if any)
    /// 3. Library calls ExecuteAsync with contexts and arguments
    /// 4. Operation returns OperationResult (success or failure)
    /// 5. Library stores result in GraphContext for dependent operations
    /// 
    /// Implementation notes:
    /// - Should be thread-safe (multiple operations may run in parallel)
    /// - Should respect CancellationToken for cooperative cancellation
    /// - Should not throw exceptions (BaseOperation wraps exceptions in OperationResult)
    /// - Can read results from dependent operations via graphContext
    /// - Can access shared state via batchContext (UserId, TenantId, Data dictionary)
    /// </summary>
    /// <param name="batchContext">
    /// The batch context with shared state, configuration, and correlation info.
    /// Contains: BatchId, StartTime, UserId, TenantId, Data dictionary, CancellationToken.
    /// Same instance is passed to all operations in the batch.
    /// </param>
    /// <param name="graphContext">
    /// The graph context with results from dependent operations.
    /// Use this to read outputs from operations this one depends on.
    /// Example: graphContext.GetValue{List{User}}("FetchUsers")
    /// </param>
    /// <param name="arguments">
    /// Optional arguments passed from the request (key-value pairs).
    /// Provided by the client in BatchOperationItem.Arguments.
    /// Example: arguments["tenantId"], arguments["includeInactive"]
    /// Null if no arguments were provided.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token for async operations.
    /// All operations should respect this for cooperative cancellation.
    /// Triggered when batch is cancelled or times out.
    /// </param>
    /// <returns>
    /// The operation result containing value, success status, and error details.
    /// Use OperationResult.Success() for success, OperationResult.Failure() for failure.
    /// BaseOperation helper class handles this wrapping automatically.
    /// </returns>
    Task<OperationResult> ExecuteAsync(
        IBatchContext batchContext,
        IGraphContext graphContext,
        IDictionary<string, object?>? arguments,
        CancellationToken cancellationToken);
}