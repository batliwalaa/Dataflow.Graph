using System.Diagnostics;
using DataflowGraph.Abstractions;
using DataflowGraph.Resolution;

#pragma warning disable IDE0290 // Use primary constructor
namespace DataflowGraph.Execution;

/// <summary>
/// Executes batch operations sequentially, one after another.
/// No parallelism - simple and predictable execution order.
/// 
/// Characteristics:
/// - ✅ Operations execute in the order they appear in the list
/// - ✅ Each operation waits for its dependencies to complete
/// - ✅ Easy to debug (clear step-by-step flow)
/// - ✅ Safe for non-thread-safe operations
/// - ⚠️ Slower for independent operations (no concurrency benefit)
/// 
/// Execution flow:
/// 1. Iterate through operations in order
/// 2. For each operation:
///    a. Wait for all dependencies to complete
///    b. Check if dependencies succeeded (if required)
///    c. Resolve operation executor by name
///    d. Execute the operation
///    e. Store result in GraphContext
///    f. Handle errors (stop if required operation fails)
/// 3. Continue until all operations complete or batch is cancelled
/// 
/// </summary>
internal class SerialExecutionStrategy : IExecutionStrategy
{
    private readonly IOperationResolver _operationResolver;
    private readonly Action<string, Exception>? _onError;
    private readonly Action<string, TimeSpan>? _onComplete;

    /// <summary>
    /// Initializes a new instance of the SerialExecutionStrategy class.
    /// </summary>
    /// <param name="operationResolver">
    /// Resolves operation names to IOperationExecutor instances.
    /// Used to get the actual operation implementation for each step.
    /// </param>
    /// <param name="onError">
    /// Optional callback invoked when an operation fails.
    /// Useful for logging, metrics, or custom error handling.
    /// Signature: (operationName, exception)
    /// </param>
    /// <param name="onComplete">
    /// Optional callback invoked when an operation completes successfully.
    /// Useful for logging, metrics, or progress tracking.
    /// Signature: (operationName, duration)
    /// </param>
    public SerialExecutionStrategy(
        IOperationResolver operationResolver,
        Action<string, Exception>? onError = null,
        Action<string, TimeSpan>? onComplete = null)
    {
        _operationResolver = operationResolver ?? throw new ArgumentNullException(nameof(operationResolver));
        _onError = onError;
        _onComplete = onComplete;
    }

    /// <summary>
    /// Executes all operations sequentially in the order specified in the list.
    /// Dependencies are respected but execution is still serial (no concurrency benefit).
    /// </summary>
    /// <param name="operations">
    /// The list of operations to execute.
    /// Each operation contains: Name, DependsOn, Arguments, IsRequired, MaxRetries.
    /// </param>
    /// <param name="graphContext">
    /// The graph context for storing operation results.
    /// Operations read from this context to access dependency results.
    /// </param>
    /// <param name="batchContext">
    /// The batch context with shared state and configuration.
    /// Passed to all operations for correlation, user/tenant info, custom data.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token for the batch execution.
    /// All operations must respect this for cooperative cancellation.
    /// </param>
    /// <returns>
    /// Task that completes when all operations are executed.
    /// Results are stored in graphContext.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when cancellationToken is triggered.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when:
    /// - Operation name doesn't match any registered operation
    /// - Required dependency failed
    /// </exception>
    public async Task ExecuteAsync(
        IReadOnlyList<BatchOperationDefinition> operations,
        GraphContext graphContext,
        BatchContext batchContext,
        CancellationToken cancellationToken)
    {
        if (operations == null || operations.Count == 0)
        {
            return; // Nothing to execute
        }

        foreach (var operationDef in operations)
        {
            // Check for cancellation before each operation
            cancellationToken.ThrowIfCancellationRequested();

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Step 1: Wait for all dependencies to complete
                await WaitForDependenciesAsync(operationDef, graphContext, cancellationToken);

                // Step 2: Check if any required dependency failed
                if (!AreDependenciesSuccessful(operationDef, graphContext))
                {
                    // Skip this operation - dependency failed
                    var skippedResult = OperationResult.Failure(
                        operationDef.Name,
                        new InvalidOperationException(
                            $"Dependency failed for operation '{operationDef.Name}'. " +
                            $"Failed dependencies: [{string.Join(", ", GetFailedDependencies(operationDef, graphContext))}]"));

                    graphContext.SetResult(skippedResult);
                    _onError?.Invoke(operationDef.Name, skippedResult.Exception!);

                    // If this operation is required, stop the entire batch
                    if (operationDef.IsRequired)
                    {
                        throw skippedResult.Exception!;
                    }

                    continue; // Skip to next operation
                }

                // Step 3: Resolve the operation executor by name
                var executor = _operationResolver.Resolve(operationDef.Name);

                // Step 4: Execute the operation with retry logic
                var result = await ExecuteWithRetryAsync(
                    executor,
                    operationDef,
                    batchContext,
                    graphContext,
                    cancellationToken);

                // Step 5: Store the result in graph context
                graphContext.SetResult(result);

                stopwatch.Stop();

                // Step 6: Invoke callbacks
                if (result.IsSuccess)
                {
                    _onComplete?.Invoke(operationDef.Name, stopwatch.Elapsed);
                }
                else
                {
                    _onError?.Invoke(operationDef.Name, result.Exception!);

                    // If this operation is required, stop the entire batch
                    if (operationDef.IsRequired)
                    {
                        throw result.Exception!;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Don't catch cancellation - let it propagate
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                // Store failure result if not already stored
                if (!graphContext.HasResult(operationDef.Name))
                {
                    var failureResult = OperationResult.Failure(operationDef.Name, ex);
                    graphContext.SetResult(failureResult);
                }

                _onError?.Invoke(operationDef.Name, ex);

                // Re-throw if required operation
                if (operationDef.IsRequired)
                {
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// Waits for all dependencies to complete before proceeding.
    /// In serial mode, this polls until dependencies have results.
    /// </summary>
    private static async Task WaitForDependenciesAsync(
        BatchOperationDefinition operationDef,
        GraphContext graphContext,
        CancellationToken cancellationToken)
    {
        foreach (var dependencyName in operationDef.DependsOn)
        {
            // Poll until dependency has a result
            while (!graphContext.HasResult(dependencyName))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(10, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Checks if all required dependencies completed successfully.
    /// </summary>
    private static bool AreDependenciesSuccessful(
        BatchOperationDefinition operationDef,
        GraphContext graphContext)
    {
        foreach (var dependencyName in operationDef.DependsOn)
        {
            if (!graphContext.HasResult(dependencyName))
            {
                return false;
            }

            var depResult = graphContext.GetResult(dependencyName);
            if (depResult.IsFaulted)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Gets the names of failed dependencies.
    /// Used for error messages when skipping operations.
    /// </summary>
    private static List<string> GetFailedDependencies(
        BatchOperationDefinition operationDef,
        GraphContext graphContext)
    {
        var failed = new List<string>();
        foreach (var dependencyName in operationDef.DependsOn)
        {
            if (graphContext.HasResult(dependencyName))
            {
                var depResult = graphContext.GetResult(dependencyName);
                if (depResult.IsFaulted)
                {
                    failed.Add(dependencyName);
                }
            }
        }
        return failed;
    }

    /// <summary>
    /// Executes an operation with retry logic.
    /// Retries up to MaxRetries times on failure.
    /// </summary>
    private static async Task<OperationResult> ExecuteWithRetryAsync(
        IOperationExecutor executor,
        BatchOperationDefinition operationDef,
        BatchContext batchContext,
        GraphContext graphContext,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        Exception? lastException = null;

        while (attempt <= operationDef.MaxRetries)
        {
            try
            {
                // Execute the operation
                var result = await executor.ExecuteAsync(
                    batchContext,
                    graphContext,
                    operationDef.Arguments,
                    cancellationToken);

                return result;
            }
            catch (OperationCanceledException)
            {
                // Don't retry on cancellation
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                attempt++;

                if (attempt > operationDef.MaxRetries)
                {
                    // Max retries exceeded, return failure
                    return OperationResult.Failure(operationDef.Name, ex);
                }

                // Wait before retry (exponential backoff)
                var delayMs = 100 * (int)Math.Pow(2, attempt - 1);
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        // Should not reach here, but handle defensively
        return OperationResult.Failure(operationDef.Name, lastException!);
    }
}
#pragma warning restore IDE0290 // Use primary constructor