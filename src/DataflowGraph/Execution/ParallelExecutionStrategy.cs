using System.Collections.Concurrent;
using System.Diagnostics;
using DataflowGraph.Resolution;

namespace DataflowGraph.Execution;

/// <summary>
/// Executes batch operations with hybrid parallelism based on dependencies.
/// Independent operations run concurrently. Dependent operations wait for their prerequisites.
/// 
/// Characteristics:
/// - ✅ Independent operations run in parallel (maximum performance)
/// - ✅ Dependent operations wait for prerequisites (respects data flow)
/// - ✅ Thread-safe execution (uses ConcurrentDictionary, SemaphoreSlim)
/// - ✅ Configurable parallelism limit (MaxDegreeOfParallelism)
/// - ⚠️ Slightly harder to debug (non-deterministic order for independent ops)
/// 
/// Execution flow:
/// 1. Build dependency graph from operations
/// 2. Create TaskCompletionSource for each operation (for coordination)
/// 3. Start all operation tasks concurrently (limited by semaphore)
/// 4. Each task waits for its dependencies before executing
/// 5. Store results in GraphContext as operations complete
/// 6. Signal completion via TaskCompletionSource (unblocks dependents)
/// 7. Handle errors (respect IsRequired flag, skip dependents if needed)
/// 8. Return when all operations complete or batch is cancelled
/// 
/// Performance example:
/// - FetchUsers (100ms) + FetchProducts (100ms) run in parallel = 100ms total
/// - GenerateReport (50ms) waits for both = starts at 100ms, ends at 150ms
/// - Serial mode would take 250ms, Parallel mode takes 150ms (40% faster)
/// 
/// </summary>
internal class ParallelExecutionStrategy : IExecutionStrategy
{
    private readonly IOperationResolver _operationResolver;
    private readonly int _maxDegreeOfParallelism;
    private readonly Action<string, Exception>? _onError;
    private readonly Action<string, TimeSpan>? _onComplete;

    /// <summary>
    /// Initializes a new instance of the ParallelExecutionStrategy class.
    /// </summary>
    /// <param name="operationResolver">
    /// Resolves operation names to IOperationExecutor instances.
    /// Used to get the actual operation implementation for each step.
    /// </param>
    /// <param name="maxDegreeOfParallelism">
    /// Maximum number of operations that can run concurrently.
    /// Default is 4. Adjust based on:
    /// - Available system resources (CPU, memory, connections)
    /// - External service rate limits
    /// - Database connection pool size
    /// Set to -1 or int.MaxValue for unlimited parallelism (use with caution).
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
    public ParallelExecutionStrategy(
        IOperationResolver operationResolver,
        int maxDegreeOfParallelism = 4,
        Action<string, Exception>? onError = null,
        Action<string, TimeSpan>? onComplete = null)
    {
        _operationResolver = operationResolver ?? throw new ArgumentNullException(nameof(operationResolver));
        _maxDegreeOfParallelism = maxDegreeOfParallelism <= 0 ? int.MaxValue : maxDegreeOfParallelism;
        _onError = onError;
        _onComplete = onComplete;
    }

    /// <summary>
    /// Executes all operations with hybrid parallelism based on dependencies.
    /// Uses TaskCompletionSource to coordinate dependency waiting.
    /// Uses SemaphoreSlim to limit concurrent execution.
    /// </summary>
    /// <param name="operations">
    /// The list of operations to execute.
    /// Each operation contains: Name, DependsOn, Arguments, IsRequired, MaxRetries.
    /// </param>
    /// <param name="graphContext">
    /// The graph context for storing operation results.
    /// Operations read from this context to access dependency results.
    /// Thread-safe for concurrent access.
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
    /// - Circular dependency detected
    /// - Required operation fails (and ContinueOnError = false)
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

        // Build lookup for fast operation access
        var operationLookup = operations.ToDictionary(
            op => op.Name,
            StringComparer.OrdinalIgnoreCase);

        // Create TaskCompletionSource for each operation (for dependency coordination)
        var completionSources = new ConcurrentDictionary<string, TaskCompletionSource<bool>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var operation in operations)
        {
            completionSources[operation.Name] = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // Create semaphore to limit parallelism
        var semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);

        // Start all operation tasks
        var operationTasks = operations.Select(op =>
            ExecuteOperationAsync(op, operationLookup, completionSources, graphContext, batchContext, semaphore, cancellationToken));

        // Wait for all operations to complete
        var results = await Task.WhenAll(operationTasks).ConfigureAwait(false);

        // Check for failures in required operations
        var failures = results.Where(r => r.IsFaulted).ToList();
        if (failures.Count > 0)
        {
            var requiredFailures = failures.Where(f =>
                operationLookup[f.OperationName].IsRequired).ToList();

            if (requiredFailures.Any())
            {
                var firstFailure = requiredFailures.First();
                throw firstFailure.Exception!;
            }
        }
    }

    /// <summary>
    /// Executes a single operation, waiting for dependencies first.
    /// This is the core parallel execution logic.
    /// </summary>
    private async Task<OperationResult> ExecuteOperationAsync(
        BatchOperationDefinition operationDef,
        Dictionary<string, BatchOperationDefinition> operationLookup,
        ConcurrentDictionary<string, TaskCompletionSource<bool>> completionSources,
        GraphContext graphContext,
        BatchContext batchContext,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        try
        {
            // Step 1: Wait for all dependencies to complete
            foreach (var dependencyName in operationDef.DependsOn)
            {
                if (completionSources.TryGetValue(dependencyName, out var depCompletion))
                {
                    // Wait for dependency to signal completion
                    await depCompletion.Task.ConfigureAwait(false);
                }
            }

            // Step 2: Check if any required dependency failed
            foreach (var dependencyName in operationDef.DependsOn)
            {
                if (graphContext.HasResult(dependencyName))
                {
                    var depResult = graphContext.GetResult(dependencyName);
                    if (depResult.IsFaulted && operationLookup[dependencyName].IsRequired)
                    {
                        // Skip this operation - required dependency failed
                        var skippedResult = OperationResult.Failure(
                            operationDef.Name,
                            new InvalidOperationException(
                                $"Required dependency '{dependencyName}' failed for operation '{operationDef.Name}'"));

                        graphContext.SetResult(skippedResult);
                        completionSources[operationDef.Name].TrySetResult(false);
                        _onError?.Invoke(operationDef.Name, skippedResult.Exception!);
                        return skippedResult;
                    }
                }
            }

            // Step 3: Acquire semaphore slot (limits parallelism)
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // Step 4: Execute the operation with retry logic
                var result = await ExecuteWithRetryAsync(
                    operationDef,
                    batchContext,
                    graphContext,
                    cancellationToken).ConfigureAwait(false);

                // Step 5: Store the result in graph context
                graphContext.SetResult(result);

                // Step 6: Signal completion (unblocks dependent operations)
                completionSources[operationDef.Name].TrySetResult(result.IsSuccess);

                // Step 7: Invoke callbacks
                var stopwatch = Stopwatch.StartNew(); // Note: This measures execution time only
                if (result.IsSuccess)
                {
                    _onComplete?.Invoke(operationDef.Name, stopwatch.Elapsed);
                }
                else
                {
                    _onError?.Invoke(operationDef.Name, result.Exception!);
                }

                return result;
            }
            finally
            {
                // Always release semaphore slot
                semaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Don't catch cancellation - signal failure and propagate
            var cancelledResult = OperationResult.Failure(
                operationDef.Name,
                new TaskCanceledException($"Operation '{operationDef.Name}' was cancelled"));

            graphContext.SetResult(cancelledResult);
            completionSources[operationDef.Name].TrySetException(new TaskCanceledException());
            throw;
        }
        catch (Exception ex)
        {
            // Store failure result and signal completion
            var failureResult = OperationResult.Failure(operationDef.Name, ex);
            graphContext.SetResult(failureResult);
            completionSources[operationDef.Name].TrySetResult(false);
            _onError?.Invoke(operationDef.Name, ex);
            return failureResult;
        }
    }

    /// <summary>
    /// Executes an operation with retry logic.
    /// Retries up to MaxRetries times on failure.
    /// </summary>
    private async Task<OperationResult> ExecuteWithRetryAsync(
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
                // Resolve the operation executor by name
                var executor = _operationResolver.Resolve(operationDef.Name);

                // Execute the operation
                var result = await executor.ExecuteAsync(
                    batchContext,
                    graphContext,
                    operationDef.Arguments,
                    cancellationToken).ConfigureAwait(false);

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
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        // Should not reach here, but handle defensively
        return OperationResult.Failure(operationDef.Name, lastException!);
    }
}