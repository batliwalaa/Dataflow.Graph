namespace DataflowGraph;

/// <summary>
/// Representation of an operation to execute in a batch graph.
/// Used for building batch graphs programmatically.
/// 
/// Why this exists:
/// - Separates HTTP layer (AspNetCore package) from core library
/// - Core library doesn't depend on ASP.NET Core types
/// - Allows multiple input sources (HTTP, Hangfire, Quartz, Console, etc.)
/// - Provides a clean API for execution strategies
/// </summary>
public class BatchOperationDefinition
{
    /// <summary>
    /// Gets or sets the unique name of the operation.
    /// Used to resolve to IOperationExecutor via IOperationResolver.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of operation names this operation depends on.
    /// </summary>
    public List<string> DependsOn { get; set; } = [];

    /// <summary>
    /// Gets or sets optional arguments to pass to the operation.
    /// </summary>
    public Dictionary<string, object?> Arguments { get; set; } = [];

    /// <summary>
    /// Gets or sets whether this operation is required.
    /// </summary>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum retry count for this operation.
    /// </summary>
    public int MaxRetries { get; set; } = 0;

    /// <summary>
    /// Creates a BatchOperationDefinition from a StepDefinitionDto (declarative DTO).
    /// Used for declarative batch graph definitions.
    /// </summary>
    public static BatchOperationDefinition FromStepDefinition(StepDefinitionDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new BatchOperationDefinition
        {
            Name = dto.Name,
            DependsOn = dto.Dependencies,
            Arguments = dto.Parameters,
            IsRequired = dto.IsRequired,
            MaxRetries = dto.MaxRetries
        };
    }

    /// <summary>
    /// Creates a BatchOperationDefinition from individual parameters.
    /// Useful for imperative batch graph building.
    /// </summary>
    public static BatchOperationDefinition Create(
        string name,
        IEnumerable<string>? dependsOn = null,
        Dictionary<string, object?>? arguments = null,
        bool isRequired = true,
        int maxRetries = 0)
    {
        return new BatchOperationDefinition
        {
            Name = name,
            DependsOn = dependsOn?.ToList() ?? [],
            Arguments = arguments ?? [],
            IsRequired = isRequired,
            MaxRetries = maxRetries
        };
    }

    /// <summary>
    /// Returns a string representation of the operation definition.
    /// </summary>
    public override string ToString()
    {
        var deps = DependsOn.Count > 0 ? $" (depends on: {string.Join(", ", DependsOn)})" : "";
        return $"Operation('{Name}'{deps}, Required={IsRequired}, Retries={MaxRetries})";
    }
}
