using DataflowGraph.Abstractions;
using DataflowGraph.Execution;
using DataflowGraph.Resolution;

namespace DataflowGraph;

/// <summary>
/// Main orchestrator for batch graph execution.
/// Implements IBatchGraph and handles:
/// - Operation collection and dependency tracking
/// - Graph validation (circular dependencies, missing operations)
/// - Strategy selection (Serial/Parallel)
/// - Execution coordination
/// - Result aggregation
/// 
/// This is the core class that consuming code interacts with.
/// Typically created via IBatchGraphFactory.Create().
/// 
/// Usage:
/// var graph = batchGraphFactory.Create()
///     .AddOperation("FetchUsers")
///     .AddOperation("ValidateUsers")
///     .DependsOn("ValidateUsers", "FetchUsers");
/// 
/// var result = await graph.ExecuteAsync(batchContext);
/// 
/// </summary>
public class BatchGraph : IBatchGraph
{
    private readonly IOperationResolver _operationResolver;
    private readonly List<BatchOperationDefinition> _operations = new();
    private readonly Dictionary<string, List<string>> _dependencies = new(StringComparer.OrdinalIgnoreCase);
    private ProcessingType _processingType = ProcessingType.Parallel;
    private int _maxDegreeOfParallelism = 4;
    private Action<string, Exception>? _onError;
    private Action<string, TimeSpan>? _onComplete;

    /// <summary>
    /// Initializes a new instance of the BatchGraph class.
    /// </summary>
    /// <param name="operationResolver">
    /// Resolves operation names to IOperationExecutor instances.
    /// Injected via DI - typically a singleton.
    /// </param>
    public BatchGraph(IOperationResolver operationResolver)
    {
        _operationResolver = operationResolver ?? throw new ArgumentNullException(nameof(operationResolver));
    }

    /// <summary>
    /// Adds an operation to the batch graph by name.
    /// </summary>
    /// <param name="operationName">The name of the operation to add</param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    public IBatchGraph AddOperation(string operationName)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentNullException(nameof(operationName));
        }

        // Validate operation exists
        if (!_operationResolver.TryResolve(operationName, out _))
        {
            throw new InvalidOperationException(
                $"Operation '{operationName}' not found. " +
                $"Registered operations: [{string.Join(", ", _operationResolver.GetRegisteredOperationNames())}]");
        }

        _operations.Add(BatchOperationDefinition.Create(operationName));
        _dependencies[operationName] = new List<string>();
        return this;
    }

    /// <summary>
    /// Adds an operation to the batch graph by name with arguments.
    /// </summary>
    /// <param name="operationName">The name of the operation to add</param>
    /// <param name="arguments">Optional arguments to pass to the operation</param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    public IBatchGraph AddOperation(string operationName, Dictionary<string, object?>? arguments)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentNullException(nameof(operationName));
        }

        // Validate operation exists
        if (!_operationResolver.TryResolve(operationName, out _))
        {
            throw new InvalidOperationException(
                $"Operation '{operationName}' not found. " +
                $"Registered operations: [{string.Join(", ", _operationResolver.GetRegisteredOperationNames())}]");
        }

        _operations.Add(BatchOperationDefinition.Create(operationName, arguments: arguments));
        _dependencies[operationName] = new List<string>();
        return this;
    }

    /// <summary>
    /// Adds an operation to the batch graph with full configuration.
    /// </summary>
    /// <param name="operation">The operation definition with full configuration</param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    public IBatchGraph AddOperation(BatchOperationDefinition operation)
    {
        if (operation == null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        // Validate operation exists
        if (!_operationResolver.TryResolve(operation.Name, out _))
        {
            throw new InvalidOperationException(
                $"Operation '{operation.Name}' not found. " +
                $"Registered operations: [{string.Join(", ", _operationResolver.GetRegisteredOperationNames())}]");
        }

        _operations.Add(operation);
        _dependencies[operation.Name] = new List<string>(operation.DependsOn);
        return this;
    }

    /// <summary>
    /// Adds multiple operations to the batch graph.
    /// </summary>
    /// <param name="operations">The list of operation definitions to add</param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    public IBatchGraph AddOperations(IEnumerable<BatchOperationDefinition> operations)
    {
        if (operations == null)
        {
            throw new ArgumentNullException(nameof(operations));
        }

        foreach (var operation in operations)
        {
            AddOperation(operation);
        }
        return this;
    }

    /// <summary>
    /// Declares that an operation depends on one or more other operations.
    /// </summary>
    /// <param name="operationName">The name of the operation that has dependencies</param>
    /// <param name="dependencies">The names of operations this operation depends on</param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    public IBatchGraph DependsOn(string operationName, params string[] dependencies)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentNullException(nameof(operationName));
        }

        // Validate operation exists in graph
        if (!_operations.Any(op => op.Name.Equals(operationName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Operation '{operationName}' has not been added to the graph. " +
                $"Call AddOperation() before DependsOn().");
        }

        // Validate dependencies exist in graph
        foreach (var dependency in dependencies)
        {
            if (!_operations.Any(op => op.Name.Equals(dependency, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Dependency '{dependency}' has not been added to the graph. " +
                    $"Call AddOperation('{dependency}') before DependsOn().");
            }
        }

        _dependencies[operationName] = dependencies.ToList();

        // Update the operation definition with dependencies
        var operation = _operations.First(op => op.Name.Equals(operationName, StringComparison.OrdinalIgnoreCase));
        operation.DependsOn = dependencies.ToList();

        return this;
    }

    /// <summary>
    /// Sets the processing type for this batch graph.
    /// </summary>
    /// <param name="type">Serial or Parallel execution mode</param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    public IBatchGraph WithProcessingType(ProcessingType type)
    {
        _processingType = type;
        return this;
    }

    /// <summary>
    /// Sets the maximum degree of parallelism when using Parallel mode.
    /// </summary>
    /// <param name="degree">Maximum concurrent operations</param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    public IBatchGraph WithMaxDegreeOfParallelism(int degree)
    {
        if (degree <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(degree), "MaxDegreeOfParallelism must be greater than 0");
        }

        _maxDegreeOfParallelism = degree;
        return this;
    }

    /// <summary>
    /// Sets a callback to be invoked when an operation fails.
    /// </summary>
    /// <param name="errorHandler">The error handler callback</param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    public IBatchGraph OnError(Action<string, Exception> errorHandler)
    {
        _onError = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        return this;
    }

    /// <summary>
    /// Sets a callback to be invoked when an operation completes successfully.
    /// </summary>
    /// <param name="completeHandler">The completion handler callback</param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    public IBatchGraph OnComplete(Action<string, TimeSpan> completeHandler)
    {
        _onComplete = completeHandler ?? throw new ArgumentNullException(nameof(completeHandler));
        return this;
    }

    /// <summary>
    /// Builds a batch graph from a declarative definition.
    /// </summary>
    /// <param name="definition">The batch graph definition</param>
    /// <returns>This IBatchGraph instance for fluent chaining</returns>
    public IBatchGraph BuildFromDefinition(BatchGraphDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        // Validate the definition
        var registeredOps = _operationResolver.GetRegisteredOperationNames();
        var validationResult = definition.Validate(registeredOps);

        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException(
                $"Invalid batch graph definition: {string.Join("; ", validationResult.Errors)}");
        }

        // Apply configuration from definition
        _processingType = definition.ProcessingType;
        _maxDegreeOfParallelism = definition.MaxDegreeOfParallelism;

        // Add operations from definition
        var operations = definition.ToOperationDefinitions();
        AddOperations(operations);

        return this;
    }

    /// <summary>
    /// Executes the batch graph and returns the results.
    /// </summary>
    /// <param name="batchContext">
    /// The batch context with shared state and configuration.
    /// If null, a new BatchContext will be created.
    /// </param>
    /// <param name="cancellationToken">
    /// Optional cancellation token.
    /// If not provided, uses the CancellationToken from batchContext.
    /// </param>
    /// <returns>GraphResult containing all operation results and execution statistics</returns>
    public async Task<GraphResult> ExecuteAsync(BatchContext? batchContext = null, CancellationToken cancellationToken = default)
    {
        // Validate graph has operations
        if (_operations.Count == 0)
        {
            throw new InvalidOperationException(
                "No operations have been added to the batch graph. " +
                "Call AddOperation() before ExecuteAsync().");
        }

        // Create batch context if not provided
        batchContext ??= new BatchContext(cancellationToken: cancellationToken);

        // Use cancellation token from batch context if not provided
        if (cancellationToken == default)
        {
            cancellationToken = batchContext.CancellationToken;
        }

        // Create graph context for storing results
        var graphContext = new GraphContext();

        // Validate no circular dependencies
        var cycleResult = DetectCircularDependencies();
        if (!cycleResult.IsValid)
        {
            throw new InvalidOperationException(
                $"Circular dependency detected in batch graph: {cycleResult.CyclePath}");
        }

        // Create execution strategy
        var strategy = ExecutionStrategyFactory.Create(
            _processingType,
            _operationResolver,
            _maxDegreeOfParallelism,
            _onError,
            _onComplete);

        // Execute the batch
        await strategy.ExecuteAsync(_operations, graphContext, batchContext, cancellationToken);

        // Create and return result
        return GraphResult.FromContext(graphContext, batchContext.StartTime);
    }

    /// <summary>
    /// Detects circular dependencies in the batch graph.
    /// Uses depth-first search (DFS) with coloring algorithm.
    /// </summary>
    /// <returns>BatchValidationResult with cycle information if found</returns>
    private BatchValidationResult DetectCircularDependencies()
    {
        // Build adjacency list from dependencies
        var adjacencyList = _operations.ToDictionary(
            op => op.Name,
            op => op.DependsOn,
            StringComparer.OrdinalIgnoreCase);

        // DFS with coloring: 0 = white (unvisited), 1 = gray (visiting), 2 = black (visited)
        var color = _operations.ToDictionary(op => op.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
        var parent = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        string? FindCycle(string node, string? parentNode)
        {
            color[node] = 1; // Mark as visiting
            parent[node] = parentNode;

            if (adjacencyList.TryGetValue(node, out var dependencies))
            {
                foreach (var dependency in dependencies)
                {
                    if (color.GetValueOrDefault(dependency, 0) == 1)
                    {
                        // Found cycle - build cycle path
                        var cyclePath = new List<string> { dependency };
                        var current = node;
                        while (current != dependency && current != null)
                        {
                            cyclePath.Add(current);
                            current = parent.GetValueOrDefault(current);
                        }
                        cyclePath.Add(dependency);
                        cyclePath.Reverse();
                        return string.Join(" → ", cyclePath);
                    }
                    else if (color.GetValueOrDefault(dependency, 0) == 0)
                    {
                        var cycle = FindCycle(dependency, node);
                        if (cycle != null)
                        {
                            return cycle;
                        }
                    }
                }
            }

            color[node] = 2; // Mark as visited
            return null;
        }

        // Run DFS from each unvisited node
        foreach (var operation in _operations)
        {
            if (color.GetValueOrDefault(operation.Name, 0) == 0)
            {
                var cycle = FindCycle(operation.Name, null);
                if (cycle != null)
                {
                    return new BatchValidationResult(false, new List<string>(), new List<string>(), cycle);
                }
            }
        }

        return new BatchValidationResult(true, new List<string>(), new List<string>());
    }

    /// <summary>
    /// Clears the batch graph (for reuse scenarios).
    /// Internal method - typically not needed as graphs are short-lived.
    /// </summary>
    internal void Clear()
    {
        _operations.Clear();
        _dependencies.Clear();
        _onError = null;
        _onComplete = null;
        _processingType = ProcessingType.Parallel;
        _maxDegreeOfParallelism = 4;
    }
}