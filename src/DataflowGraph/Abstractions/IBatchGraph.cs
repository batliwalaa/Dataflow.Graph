namespace DataflowGraph.Abstractions;

/// <summary>
/// Defines the public API for building and executing batch graphs.
/// This is the main interface that consuming code interacts with.
/// 
/// Key capabilities:
/// - Add operations to the batch (by name or definition)
/// - Define dependencies between operations
/// - Configure execution mode (Serial/Parallel)
/// - Configure parallelism limits
/// - Register error/completion callbacks
/// - Execute the batch and get results
/// 
/// Supports two patterns:
/// 1. Imperative: Build graph in code, then execute
/// 2. Declarative: Load graph from configuration/JSON, then execute
/// 
/// </summary>
public interface IBatchGraph
{
    /// <summary>
    /// Adds an operation to the batch graph by name.
    /// The operation must be registered in DI (via IOperationExecutor).
    /// 
    /// Usage:
    /// var graph = batchGraphFactory.Create()
    ///     .AddOperation("FetchUsers")
    ///     .AddOperation("ValidateUsers")
    ///     .DependsOn("ValidateUsers", "FetchUsers");
    /// </summary>
    /// <param name="operationName">
    /// The name of the operation to add.
    /// Must match the OperationName property of a registered IOperationExecutor.
    /// </param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when operation name doesn't match any registered operation.
    /// </exception>
    IBatchGraph AddOperation(string operationName);

    /// <summary>
    /// Adds an operation to the batch graph by name with arguments.
    /// The operation must be registered in DI (via IOperationExecutor).
    /// 
    /// Usage:
    /// var graph = batchGraphFactory.Create()
    ///     .AddOperation("FetchUsers", new Dictionary<string, object?>
    ///     {
    ///         ["tenantId"] = "tenant-123",
    ///         ["includeInactive"] = true
    ///     });
    /// </summary>
    /// <param name="operationName">
    /// The name of the operation to add.
    /// Must match the OperationName property of a registered IOperationExecutor.
    /// </param>
    /// <param name="arguments">
    /// Optional arguments to pass to the operation.
    /// Operations receive these via ExecuteAsync(arguments) parameter.
    /// </param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    IBatchGraph AddOperation(string operationName, Dictionary<string, object?>? arguments);

    /// <summary>
    /// Adds an operation to the batch graph with full configuration.
    /// Use this when you need to specify dependencies, retry count, etc.
    /// 
    /// Usage:
    /// var graph = batchGraphFactory.Create()
    ///     .AddOperation(new BatchOperationDefinition
    ///     {
    ///         Name = "FetchUsers",
    ///         Arguments = new Dictionary<string, object?> { ["tenantId"] = "123" },
    ///         DependsOn = new List<string>(),
    ///         IsRequired = true,
    ///         MaxRetries = 3
    ///     });
    /// </summary>
    /// <param name="operation">
    /// The operation definition with full configuration.
    /// Contains: Name, DependsOn, Arguments, IsRequired, MaxRetries.
    /// </param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    IBatchGraph AddOperation(BatchOperationDefinition operation);

    /// <summary>
    /// Adds multiple operations to the batch graph.
    /// Convenience method for adding several operations at once.
    /// </summary>
    /// <param name="operations">
    /// The list of operation definitions to add.
    /// </param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    IBatchGraph AddOperations(IEnumerable<BatchOperationDefinition> operations);

    /// <summary>
    /// Declares that an operation depends on one or more other operations.
    /// Dependent operations wait for their dependencies to complete before executing.
    /// In Parallel mode, independent operations run concurrently.
    /// 
    /// Usage:
    /// var graph = batchGraphFactory.Create()
    ///     .AddOperation("FetchUsers")
    ///     .AddOperation("FetchProducts")
    ///     .AddOperation("GenerateReport")
    ///     .DependsOn("GenerateReport", "FetchUsers", "FetchProducts");
    /// </summary>
    /// <param name="operationName">
    /// The name of the operation that has dependencies.
    /// Must be an operation that was previously added to the graph.
    /// </param>
    /// <param name="dependencies">
    /// The names of operations this operation depends on.
    /// Each must be an operation that was previously added to the graph.
    /// </param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when operationName or any dependency name doesn't exist in the graph.
    /// </exception>
    IBatchGraph DependsOn(string operationName, params string[] dependencies);

    /// <summary>
    /// Sets the processing type for this batch graph.
    /// Default is Parallel (independent operations run concurrently).
    /// 
    /// Usage:
    /// var graph = batchGraphFactory.Create()
    ///     .AddOperation("FetchUsers")
    ///     .AddOperation("ValidateUsers")
    ///     .WithProcessingType(ProcessingType.Serial);  // Run sequentially
    /// </summary>
    /// <param name="type">
    /// The processing type:
    /// - Serial: Operations execute one-by-one (no parallelism)
    /// - Parallel: Independent operations run concurrently (default)
    /// </param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    IBatchGraph WithProcessingType(ProcessingType type);

    /// <summary>
    /// Sets the maximum degree of parallelism when using Parallel mode.
    /// Default is 4 concurrent operations.
    /// 
    /// Usage:
    /// var graph = batchGraphFactory.Create()
    ///     .AddOperation("FetchUsers")
    ///     .AddOperation("FetchProducts")
    ///     .WithMaxDegreeOfParallelism(8);  // Allow up to 8 concurrent operations
    /// </summary>
    /// <param name="degree">
    /// Maximum number of operations that can run concurrently.
    /// Set to -1 or int.MaxValue for unlimited parallelism (use with caution).
    /// </param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    IBatchGraph WithMaxDegreeOfParallelism(int degree);

    /// <summary>
    /// Sets a callback to be invoked when an operation fails.
    /// Useful for logging, metrics, or custom error handling.
    /// 
    /// Usage:
    /// var graph = batchGraphFactory.Create()
    ///     .AddOperation("FetchUsers")
    ///     .OnError((name, ex) =>
    ///     {
    ///         logger.LogError(ex, "Operation {Name} failed", name);
    ///     });
    /// </summary>
    /// <param name="errorHandler">
    /// The error handler callback.
    /// Signature: (operationName, exception)
    /// </param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    IBatchGraph OnError(Action<string, Exception> errorHandler);

    /// <summary>
    /// Sets a callback to be invoked when an operation completes successfully.
    /// Useful for logging, metrics, or progress tracking.
    /// 
    /// Usage:
    /// var graph = batchGraphFactory.Create()
    ///     .AddOperation("FetchUsers")
    ///     .OnComplete((name, duration) =>
    ///     {
    ///         logger.LogInformation("Operation {Name} completed in {Duration}", name, duration);
    ///     });
    /// </summary>
    /// <param name="completeHandler">
    /// The completion handler callback.
    /// Signature: (operationName, duration)
    /// </param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    IBatchGraph OnComplete(Action<string, TimeSpan> completeHandler);

    /// <summary>
    /// Builds a batch graph from a declarative definition.
    /// Used for scenarios where the graph structure comes from configuration/JSON.
    /// 
    /// Usage:
    /// var definition = configuration.GetSection("BatchDefinitions:UserOnboarding").Get<BatchGraphDefinition>();
    /// var graph = batchGraphFactory.Create()
    ///     .BuildFromDefinition(definition);
    /// var result = await graph.ExecuteAsync(batchContext);
    /// </summary>
    /// <param name="definition">
    /// The batch graph definition containing steps and configuration.
    /// Contains: Steps (List<StepDefinitionDto>), ProcessingType, MaxDegreeOfParallelism, etc.
    /// </param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the definition is invalid (validation errors).
    /// </exception>
    IBatchGraph BuildFromDefinition(BatchGraphDefinition definition);

    /// <summary>
    /// Executes the batch graph and returns the results.
    /// This is the main entry point for batch execution.
    /// 
    /// Usage:
    /// var graph = batchGraphFactory.Create()
    ///     .AddOperation("FetchUsers")
    ///     .AddOperation("ValidateUsers")
    ///     .DependsOn("ValidateUsers", "FetchUsers");
    /// 
    /// var result = await graph.ExecuteAsync(batchContext);
    /// 
    /// if (result.IsSuccess)
    /// {
    ///     var users = result.GetValue<List<User>>("FetchUsers");
    /// }
    /// </summary>
    /// <param name="batchContext">
    /// The batch context with shared state and configuration.
    /// Contains: BatchId, UserId, TenantId, Data dictionary, CancellationToken.
    /// If null, a new BatchContext will be created.
    /// </param>
    /// <param name="cancellationToken">
    /// Optional cancellation token.
    /// If not provided, uses the CancellationToken from batchContext.
    /// </param>
    /// <returns>
    /// GraphResult containing all operation results and execution statistics.
    /// Contains: Results (dictionary), Duration, IsSuccess, SuccessCount, FailureCount.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when:
    /// - No operations have been added to the graph
    /// - Circular dependency detected
    /// - Required operation fails (and ContinueOnError = false)
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the batch is cancelled via cancellationToken.
    /// </exception>
    Task<GraphResult> ExecuteAsync(BatchContext? batchContext = null, CancellationToken cancellationToken = default);
}