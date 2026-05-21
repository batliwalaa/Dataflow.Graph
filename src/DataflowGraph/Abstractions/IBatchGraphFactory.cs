namespace DataflowGraph.Abstractions;

/// <summary>
/// Factory interface for creating IBatchGraph instances.
/// 
/// Why a factory:
/// - BatchGraph is short-lived (one instance per batch execution)
/// - BatchGraph has dependencies (IOperationResolver) that come from DI
/// - Factory allows clean separation: singleton resolver, transient graphs
/// - Testable: mock the factory in unit tests
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
public interface IBatchGraphFactory
{
    /// <summary>
    /// Creates a new IBatchGraph instance.
    /// Each call returns a fresh graph instance for a new batch execution.
    /// 
    /// Usage:
    /// var graph = batchGraphFactory.Create()
    ///     .AddOperation("FetchUsers")
    ///     .AddOperation("ValidateUsers")
    ///     .DependsOn("ValidateUsers", "FetchUsers");
    /// 
    /// var result = await graph.ExecuteAsync(batchContext);
    /// </summary>
    /// <returns>
    /// A new IBatchGraph instance ready for operation configuration.
    /// Each call returns a fresh instance (do not reuse across batches).
    /// </returns>
    /// <example>
    /// <code>
    /// // In a controller
    /// public class BatchController : ControllerBase
    /// {
    ///     private readonly IBatchGraphFactory _graphFactory;
    /// 
    ///     public BatchController(IBatchGraphFactory graphFactory)
    ///     {
    ///         _graphFactory = graphFactory;
    ///     }
    /// 
    ///     [HttpPost("batch")]
    ///     public async Task<IActionResult> ExecuteBatch(BatchOperationRequest request)
    ///     {
    ///         var graph = _graphFactory.Create()
    ///             .AddOperations(request.Operations.Select(o => 
    ///                 BatchOperationDefinition.FromOperationItem(o)));
    /// 
    ///         var result = await graph.ExecuteAsync(batchContext);
    ///         return Ok(result);
    ///     }
    /// }
    /// </code>
    /// </example>
    IBatchGraph Create();

    /// <summary>
    /// Creates a new IBatchGraph instance from a declarative definition.
    /// Convenience method that combines Create() and BuildFromDefinition().
    /// 
    /// Usage:
    /// var definition = configuration.GetSection("BatchDefinitions:UserOnboarding").Get<BatchGraphDefinition>();
    /// var graph = batchGraphFactory.Create(definition);
    /// var result = await graph.ExecuteAsync(batchContext);
    /// </summary>
    /// <param name="definition">
    /// The batch graph definition containing steps and configuration.
    /// Contains: Steps, ProcessingType, MaxDegreeOfParallelism, etc.
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
    /// // Load definition from configuration
    /// var definition = configuration.GetSection("BatchDefinitions:OrderFulfillment").Get<BatchGraphDefinition>();
    /// 
    /// // Create graph from definition
    /// var graph = batchGraphFactory.Create(definition);
    /// 
    /// // Execute
    /// var result = await graph.ExecuteAsync(batchContext);
    /// </code>
    /// </example>
    IBatchGraph Create(BatchGraphDefinition definition);
}