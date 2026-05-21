using DataflowGraph.Abstractions;

namespace DataflowGraph;

/// <summary>
/// Base class for operation implementations that return a value.
/// Handles exception capture and OperationResult wrapping automatically.
/// 
/// Benefits:
/// - No need to manually wrap results in OperationResult.Success()
/// - Exceptions are automatically caught and returned as OperationResult.Failure()
/// - Reduces boilerplate code in every operation
/// - Consistent error handling across all operations
/// 
/// Usage:
/// Inherit from this class and implement ExecuteCoreAsync() with your operation logic.
/// Return the value directly - BaseOperation wraps it in OperationResult.
/// 
/// </summary>
/// <typeparam name="TResult">The result type this operation produces</typeparam>
/// <example>
/// <code>
/// public class FetchUsersOperation : BaseOperation&lt;List&lt;User&gt;&gt;
/// {
///     public override string OperationName => "FetchUsers";
/// 
///     protected override async Task&lt;List&lt;User&gt;&gt; ExecuteCoreAsync(
///         IBatchContext batchContext,
///         IGraphContext graphContext,
///         IDictionary&lt;string, object?&gt;? arguments,
///         CancellationToken cancellationToken)
///     {
///         // Your logic here - just return the value
///         var users = await _userRepository.GetAllAsync(cancellationToken);
///         return users;  // ← Automatically wrapped in OperationResult.Success()
///     }
/// }
/// </code>
/// </example>
public abstract class BaseOperation<TResult> : IOperationExecutor
{
    /// <summary>
    /// Gets the unique operation name used to resolve this executor.
    /// Must be implemented by derived classes.
    /// This name is used in BatchOperationRequest to specify which operation to run.
    /// Must be unique across all registered operations in the application.
    /// 
    /// Example: "FetchUsers", "ValidateOrder", "SendNotification"
    /// </summary>
    public abstract string OperationName { get; }

    /// <summary>
    /// Executes the operation with the provided context and arguments.
    /// This method handles exception capture and returns OperationResult.
    /// Derived classes should override ExecuteCoreAsync() instead of this method.
    /// 
    /// Execution flow:
    /// 1. Calls ExecuteCoreAsync() (implemented by derived class)
    /// 2. Wraps successful result in OperationResult.Success()
    /// 3. Catches exceptions and wraps in OperationResult.Failure()
    /// 4. Returns OperationResult to the batch library
    /// 
    /// Do not override this method unless you need custom error handling.
    /// </summary>
    /// <param name="batchContext">The batch context with shared state and configuration</param>
    /// <param name="graphContext">The graph context with results from dependent operations</param>
    /// <param name="arguments">Optional arguments passed from the request</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>The operation result containing value and error status</returns>
    public async Task<OperationResult> ExecuteAsync(
        IBatchContext batchContext,
        IGraphContext graphContext,
        IDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            // Call the core logic implemented by derived class
            var result = await ExecuteCoreAsync(batchContext, graphContext, arguments, cancellationToken);

            // Wrap successful result in OperationResult
            return OperationResult.Success(OperationName, result);
        }
        catch (OperationCanceledException)
        {
            // Don't wrap cancellation exceptions - let them propagate
            // This allows the batch to be cancelled properly
            throw;
        }
        catch (Exception ex)
        {
            // Wrap all other exceptions in OperationResult.Failure
            // This allows the batch to continue with other operations (error isolation)
            return OperationResult.Failure(OperationName, ex);
        }
    }

    /// <summary>
    /// The core operation logic that derived classes must implement.
    /// Exceptions thrown here are automatically captured and returned as Failure.
    /// Return the value directly - it will be wrapped in OperationResult.Success().
    /// 
    /// Implementation guidelines:
    /// - Do not throw exceptions for expected errors (return a result instead)
    /// - Respect the CancellationToken for cooperative cancellation
    /// - Use graphContext to read results from dependent operations
    /// - Use batchContext for shared state (UserId, TenantId, Data dictionary)
    /// - Use arguments for operation-specific parameters from the request
    /// - Keep operations focused (single responsibility)
    /// - Make operations thread-safe (may run in parallel with other operations)
    /// </summary>
    /// <param name="batchContext">The batch context with shared state and configuration</param>
    /// <param name="graphContext">The graph context with results from dependent operations</param>
    /// <param name="arguments">Optional arguments passed from the request</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>The operation result value (will be wrapped in OperationResult.Success())</returns>
    protected abstract Task<TResult> ExecuteCoreAsync(
        IBatchContext batchContext,
        IGraphContext graphContext,
        IDictionary<string, object?>? arguments,
        CancellationToken cancellationToken);
}

/// <summary>
/// Base class for void-returning operation implementations (no result value).
/// Handles exception capture and OperationResult wrapping automatically.
/// 
/// Benefits:
/// - No need to manually wrap results in OperationResult.Success()
/// - Exceptions are automatically caught and returned as OperationResult.Failure()
/// - Reduces boilerplate code in every operation
/// - Consistent error handling across all operations
/// 
/// Usage:
/// Inherit from this class and implement ExecuteCoreAsync() with your operation logic.
/// Return Task.CompletedTask when done - BaseOperation wraps it in OperationResult.Success(null).
/// 
/// </summary>
/// <example>
/// <code>
/// public class SendNotificationOperation : BaseOperation
/// {
///     public override string OperationName => "SendNotification";
/// 
///     protected override async Task ExecuteCoreAsync(
///         IBatchContext batchContext,
///         IGraphContext graphContext,
///         IDictionary&lt;string, object?&gt;? arguments,
///         CancellationToken cancellationToken)
///     {
///         // Your logic here - no return value
///         var userId = graphContext.GetValue&lt;string&gt;("FetchUserId");
///         await _notificationService.SendAsync(userId, cancellationToken);
///         // Return Task.CompletedTask or just end the method
///     }
/// }
/// </code>
/// </example>
public abstract class BaseOperation : IOperationExecutor
{
    /// <summary>
    /// Gets the unique operation name used to resolve this executor.
    /// Must be implemented by derived classes.
    /// </summary>
    public abstract string OperationName { get; }

    /// <summary>
    /// Executes the operation with the provided context and arguments.
    /// This method handles exception capture and returns OperationResult.
    /// Derived classes should override ExecuteCoreAsync() instead of this method.
    /// </summary>
    /// <param name="batchContext">The batch context with shared state and configuration</param>
    /// <param name="graphContext">The graph context with results from dependent operations</param>
    /// <param name="arguments">Optional arguments passed from the request</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>The operation result containing success status (Value is always null)</returns>
    public async Task<OperationResult> ExecuteAsync(
        IBatchContext batchContext,
        IGraphContext graphContext,
        IDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            // Call the core logic implemented by derived class
            await ExecuteCoreAsync(batchContext, graphContext, arguments, cancellationToken);

            // Wrap successful completion in OperationResult (null value for void)
            return OperationResult.Success(OperationName, null);
        }
        catch (OperationCanceledException)
        {
            // Don't wrap cancellation exceptions - let them propagate
            throw;
        }
        catch (Exception ex)
        {
            // Wrap all other exceptions in OperationResult.Failure
            return OperationResult.Failure(OperationName, ex);
        }
    }

    /// <summary>
    /// The core operation logic that derived classes must implement.
    /// Exceptions thrown here are automatically captured and returned as Failure.
    /// </summary>
    /// <param name="batchContext">The batch context with shared state and configuration</param>
    /// <param name="graphContext">The graph context with results from dependent operations</param>
    /// <param name="arguments">Optional arguments passed from the request</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Task that completes when the operation is done</returns>
    protected abstract Task ExecuteCoreAsync(
        IBatchContext batchContext,
        IGraphContext graphContext,
        IDictionary<string, object?>? arguments,
        CancellationToken cancellationToken);
}