using DataflowGraph.Abstractions;
using DataflowGraph.Resolution;

namespace DataflowGraph;

/// <summary>
/// Concrete implementation of IBatchGraphFactory.
/// Creates new BatchGraph instances with proper DI resolution.
/// 
/// Lifetime:
/// - Registered as Transient in DI (new instance per injection)
/// - Each Create() call returns a new BatchGraph (short-lived)
/// - IOperationResolver is singleton (shared across all graphs)
/// 
/// Registration (Program.cs):
/// services.AddSingleton<IOperationResolver, OperationResolver>();
/// services.AddTransient<IBatchGraphFactory, BatchGraphFactory>();
/// 
/// Usage:
/// var graph = batchGraphFactory.Create()
///     .AddOperation("FetchUsers")
///     .AddOperation("ValidateUsers");
/// 
/// var result = await graph.ExecuteAsync(batchContext);
/// 
/// </summary>
public class BatchGraphFactory : IBatchGraphFactory
{
    private readonly IOperationResolver _operationResolver;

    /// <summary>
    /// Initializes a new instance of the BatchGraphFactory class.
    /// </summary>
    /// <param name="operationResolver">
    /// Resolves operation names to IOperationExecutor instances.
    /// Injected as singleton - shared across all BatchGraph instances.
    /// </param>
    public BatchGraphFactory(IOperationResolver operationResolver)
    {
        _operationResolver = operationResolver ?? throw new ArgumentNullException(nameof(operationResolver));
    }

    /// <summary>
    /// Creates a new IBatchGraph instance.
    /// Each call returns a fresh graph instance for a new batch execution.
    /// </summary>
    /// <returns>
    /// A new IBatchGraph instance ready for operation configuration.
    /// Do not reuse across multiple batch executions.
    /// </returns>
    /// <example>
    /// <code>
    /// var graph = batchGraphFactory.Create()
    ///     .AddOperation("FetchUsers")
    ///     .AddOperation("ValidateUsers")
    ///     .DependsOn("ValidateUsers", "FetchUsers");
    /// 
    /// var result = await graph.ExecuteAsync(batchContext);
    /// </code>
    /// </example>
    public IBatchGraph Create()
    {
        return new BatchGraph(_operationResolver);
    }

    /// <summary>
    /// Creates a new IBatchGraph instance from a declarative definition.
    /// Convenience method that combines Create() and BuildFromDefinition().
    /// </summary>
    /// <param name="definition">
    /// The batch graph definition containing steps and configuration.
    /// </param>
    /// <returns>
    /// A new IBatchGraph instance configured from the definition.
    /// Ready for immediate execution.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the definition is invalid (validation errors).
    /// </exception>
    /// <example>
    /// <code>
    /// var definition = configuration.GetSection("BatchDefinitions:UserOnboarding").Get<BatchGraphDefinition>();
    /// var graph = batchGraphFactory.Create(definition);
    /// var result = await graph.ExecuteAsync(batchContext);
    /// </code>
    /// </example>
    public IBatchGraph Create(BatchGraphDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        var graph = Create();
        graph.BuildFromDefinition(definition);
        return graph;
    }
}