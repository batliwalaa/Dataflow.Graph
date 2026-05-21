namespace DataflowGraph.Execution;

/// <summary>
/// Defines an execution strategy for batch operations.
/// Implementations handle the actual execution of operations with different concurrency patterns.
/// 
/// Strategy pattern benefits:
/// - Separates execution logic from orchestration (BatchGraph)
/// - Easy to add new execution modes (e.g., PriorityBased, Scheduled)
/// - Testable independently (mock the strategy)
/// - Swappable at runtime based on configuration
/// 
/// Built-in implementations:
/// - SerialExecutionStrategy: Execute operations one-by-one sequentially
/// - ParallelExecutionStrategy: Execute independent operations concurrently (hybrid)
/// 
/// </summary>
internal interface IExecutionStrategy
{
    /// <summary>
    /// Executes all operations in the batch according to the strategy.
    /// 
    /// Execution flow:
    /// 1. Validate operations (check dependencies, circular references)
    /// 2. Build execution plan (topological sort for dependency order)
    /// 3. Execute operations (according to strategy: serial or parallel)
    /// 4. Store results in GraphContext (for dependent operations to read)
    /// 5. Handle errors (respect IsRequired flag, retry logic)
    /// 6. Return when all operations complete or batch is cancelled
    /// 
    /// Implementation requirements:
    /// - Must respect operation dependencies (DependsOn list)
    /// - Must respect CancellationToken (cooperative cancellation)
    /// - Must store results in GraphContext via SetResult()
    /// - Must handle errors gracefully (don't crash the batch)
    /// - Must respect IsRequired flag (skip dependents if required op fails)
    /// - Must respect MaxRetries (retry failed operations)
    /// - Must be thread-safe (ParallelStrategy executes concurrently)
    /// </summary>
    /// <param name="operations">
    /// The list of operations to execute.
    /// Each operation contains: Name, DependsOn, Arguments, IsRequired, MaxRetries.
    /// Operations may have dependencies on other operations in the list.
    /// </param>
    /// <param name="graphContext">
    /// The graph context for storing operation results.
    /// Operations read from this context to access dependency results.
    /// Thread-safe for concurrent access (ParallelStrategy).
    /// </param>
    /// <param name="batchContext">
    /// The batch context with shared state and configuration.
    /// Passed to all operations for correlation, user/tenant info, custom data.
    /// Same instance for all operations in the batch.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token for the batch execution.
    /// All operations must respect this for cooperative cancellation.
    /// Triggered when:
    /// - Client cancels the HTTP request
    /// - Batch timeout is reached
    /// - Parent operation is cancelled
    /// </param>
    /// <returns>
    /// Task that completes when all operations are executed.
    /// Results are stored in graphContext (not returned directly).
    /// Throws if:
    /// - Circular dependency detected
    /// - Required operation fails (and ContinueOnError = false)
    /// - CancellationToken is triggered
    /// </returns>
    /// <example>
    /// <code>
    /// // Serial strategy
    /// var strategy = new SerialExecutionStrategy(resolver, onError, onComplete);
    /// await strategy.ExecuteAsync(operations, graphContext, batchContext, ct);
    /// 
    /// // Parallel strategy
    /// var strategy = new ParallelExecutionStrategy(resolver, maxParallelism: 4, onError, onComplete);
    /// await strategy.ExecuteAsync(operations, graphContext, batchContext, ct);
    /// </code>
    /// </example>
    Task ExecuteAsync(
        IReadOnlyList<BatchOperationDefinition> operations,
        GraphContext graphContext,
        BatchContext batchContext,
        CancellationToken cancellationToken);
}